using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ProxyYARP.Data.Models;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ProxyYARP.Proxy.Yarp;

/// <summary>
/// 网站代理：以 /{scheme}://{authority}/路径 的形式访问已登记的网站。
/// 只有出现在白名单中的 authority 会被转发，其余一律拒绝，避免网关变成开放代理（SSRF）。
/// </summary>
public sealed partial class WebsiteProxyTransformProvider : ITransformProvider
{
    /// <summary>白名单 key（RouteConfig.Metadata）——只有带此标记的路由才会挂载本转换</summary>
    public const string MetadataKey = "WebsiteProxy";

    private readonly WebsiteAllowList _allow;
    private readonly ILogger<WebsiteProxyTransformProvider> _logger;

    public WebsiteProxyTransformProvider(WebsiteAllowList allow, ILogger<WebsiteProxyTransformProvider> logger)
    {
        _allow = allow;
        _logger = logger;
    }

    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        // 仅对打了 WebsiteProxy 标记的路由生效，普通 L7 路由完全不受影响
        if (context.Route?.Metadata == null ||
            !context.Route.Metadata.TryGetValue(MetadataKey, out var flag) ||
            !string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 读取该路由下所有站点的改写偏好：任一站点开启了对应改写，就挂载对应转换
        var anyBodyRewrite = context.Route.Metadata.TryGetValue(MetadataKey + ".AnyBodyRewrite", out var br)
                              && string.Equals(br, "true", StringComparison.OrdinalIgnoreCase);
        var anyCookieRewrite = context.Route.Metadata.TryGetValue(MetadataKey + ".AnyCookieRewrite", out var cr)
                                && string.Equals(cr, "true", StringComparison.OrdinalIgnoreCase);

        context.AddRequestTransform(ctx => RewriteRequestAsync(ctx));

        if (anyBodyRewrite)
            context.AddResponseTransform(ctx => RewriteBodyAsync(ctx));

        if (anyCookieRewrite)
            context.AddResponseTransform(ctx => RewriteCookiesAsync(ctx));

        if (anyBodyRewrite || anyCookieRewrite)
            context.AddResponseTransform(RewriteLocationAsync);
    }

    // ─────────── 请求：解析内嵌 URL 并改写转发目标 ───────────

    private const string AllowEntryKey = "__WebsiteProxyEntry";
    private const string MatchedPrefixKey = "__WebsiteProxyMatchedPrefix";

    private ValueTask RewriteRequestAsync(RequestTransformContext ctx)
    {
        var http = ctx.HttpContext;
        var raw = http.Request.Path.Value ?? "";
        var rest = raw.Length > 0 && raw[0] == '/' ? raw[1..] : raw;

        // 1. 匹配 /proxy/{key} (支持 /proxy/domain 和 /proxy/alias)
        if (rest.StartsWith("proxy/", StringComparison.OrdinalIgnoreCase) || string.Equals(rest, "proxy", StringComparison.OrdinalIgnoreCase))
        {
            var afterProxy = rest.Length > 6 ? rest[6..] : "";
            var slash = afterProxy.IndexOf('/');
            var key = slash < 0 ? afterProxy : afterProxy[..slash];
            var tail = slash < 0 ? "/" : afterProxy[slash..];

            if (string.IsNullOrWhiteSpace(key) || key.Contains('?') || key.Contains('#'))
            {
                _logger.LogDebug("[WebsiteProxy] 拒绝：/proxy/ 后目标非法 {Raw}", raw);
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return ValueTask.CompletedTask;
            }

            var authKey = NormalizeAuthority(key);
            if (_allow.TryGetByAuthority(authKey, out var entry))
            {
                if (!entry!.AllowsMode("Prefix"))
                {
                    _logger.LogWarning("[WebsiteProxy] 拒绝：站点 '{Authority}' 未启用 Prefix 代理模式", authKey);
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return ValueTask.CompletedTask;
                }
                ApplyProxyRoute(ctx, entry, tail, $"/proxy/{entry.Authority}");
                return ValueTask.CompletedTask;
            }

            if (_allow.TryGetByAlias(key, out entry))
            {
                if (!entry!.AllowsMode("Alias"))
                {
                    _logger.LogWarning("[WebsiteProxy] 拒绝：站点别名 '{Alias}' 未启用 Alias 代理模式", key);
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return ValueTask.CompletedTask;
                }
                ApplyProxyRoute(ctx, entry, tail, $"/proxy/{entry.Alias}");
                return ValueTask.CompletedTask;
            }

            _logger.LogWarning("[WebsiteProxy] 拒绝：'{Key}' 不在白名单中来自 {RemoteIp}", key, http.Connection.RemoteIpAddress);
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return ValueTask.CompletedTask;
        }

        // 2. 匹配 /s/{alias} (短别名模式)
        if (rest.StartsWith("s/", StringComparison.OrdinalIgnoreCase) || string.Equals(rest, "s", StringComparison.OrdinalIgnoreCase))
        {
            var afterS = rest.Length > 2 ? rest[2..] : "";
            var slash = afterS.IndexOf('/');
            var alias = slash < 0 ? afterS : afterS[..slash];
            var tail = slash < 0 ? "/" : afterS[slash..];

            if (string.IsNullOrWhiteSpace(alias) || alias.Contains('?') || alias.Contains('#'))
            {
                _logger.LogDebug("[WebsiteProxy] 拒绝：/s/ 后别名非法 {Raw}", raw);
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return ValueTask.CompletedTask;
            }

            if (_allow.TryGetByAlias(alias, out var entry))
            {
                if (!entry!.AllowsMode("Alias"))
                {
                    _logger.LogWarning("[WebsiteProxy] 拒绝：站点别名 '{Alias}' 未启用 Alias 代理模式", alias);
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return ValueTask.CompletedTask;
                }
                ApplyProxyRoute(ctx, entry, tail, $"/s/{entry.Alias}");
                return ValueTask.CompletedTask;
            }

            _logger.LogWarning("[WebsiteProxy] 拒绝：别名 '{Alias}' 不在白名单中来自 {RemoteIp}", alias, http.Connection.RemoteIpAddress);
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return ValueTask.CompletedTask;
        }

        // 3. 匹配 /{scheme}://{authority} (协议内嵌模式)
        var sep = rest.IndexOf("://", StringComparison.Ordinal);
        if (sep > 0)
        {
            var schemeText = rest[..sep];
            if (!string.Equals(schemeText, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(schemeText, "https", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("[WebsiteProxy] 拒绝：不支持的协议 {Scheme}", schemeText);
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return ValueTask.CompletedTask;
            }

            var afterScheme = rest[(sep + 3)..];
            var slash = afterScheme.IndexOf('/');
            var authority = slash < 0 ? afterScheme : afterScheme[..slash];
            var tail = slash < 0 ? "/" : afterScheme[slash..];

            if (authority.Contains('?') || authority.Contains('#') || string.IsNullOrWhiteSpace(authority))
            {
                _logger.LogDebug("[WebsiteProxy] 拒绝：authority 非法 {Authority}", authority);
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return ValueTask.CompletedTask;
            }

            var authorityKey = NormalizeAuthority(authority);
            if (_allow.TryGetByAuthority(authorityKey, out var entry))
            {
                if (!entry!.AllowsMode("Scheme"))
                {
                    _logger.LogWarning("[WebsiteProxy] 拒绝：站点 '{Authority}' 未启用 Scheme 代理模式", authorityKey);
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return ValueTask.CompletedTask;
                }
                ApplyProxyRoute(ctx, entry, tail, $"/{entry.UpstreamScheme}://{entry.Authority}");
                return ValueTask.CompletedTask;
            }

            _logger.LogWarning(
                "[WebsiteProxy] 拒绝：authority '{Authority}' 不在白名单中（疑似开放代理/SSRF 探测）来自 {RemoteIp}",
                authorityKey, http.Connection.RemoteIpAddress);
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return ValueTask.CompletedTask;
        }

        _logger.LogDebug("[WebsiteProxy] 拒绝：路径未匹配任何代理方案 {Raw}", raw);
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return ValueTask.CompletedTask;
    }

    private static void ApplyProxyRoute(RequestTransformContext ctx, WebsiteEntry entry, string tail, string matchedPrefix)
    {
        var upstreamScheme = entry.UseHttps ? "https" : "http";
        ctx.DestinationPrefix = $"{upstreamScheme}://{entry.Authority}";
        ctx.Path = tail; // 查询串由 YARP 原样透传，无需处理

        // 目标站点期望 Host 是自身域名，改写 Host 头
        ctx.ProxyRequest.Headers.Remove("Host");
        ctx.ProxyRequest.Headers.TryAddWithoutValidation("Host", entry.Authority);

        // 让下游转换能拿到当前站点配置及匹配的前缀
        ctx.HttpContext.Items[AllowEntryKey] = entry;
        ctx.HttpContext.Items[MatchedPrefixKey] = matchedPrefix;
    }

    // ─────────── 响应：改写 HTML/CSS 内的绝对路径 ───────────

    /// <summary>HTML 中 root-relative 引用，如 ="\/xxx"（不含协议相对的 // ）</summary>
    [GeneratedRegex("""(?<==["'])/(?!/)""")]
    private static partial Regex HtmlAttrRootRelativeRegex();

    /// <summary>CSS url(/xxx)，如 url(\/xxx)</summary>
    [GeneratedRegex("""(?<=url\(["']?)/(?!/)""")]
    private static partial Regex CssUrlRootRelativeRegex();

    private ValueTask RewriteBodyAsync(ResponseTransformContext ctx)
    {
        var entry = ctx.HttpContext.Items[AllowEntryKey] as WebsiteEntry;
        if (entry is not { RewriteBody: true }) return ValueTask.CompletedTask;

        // 压缩响应体改写代价高且容易损坏，直接跳过
        if (!string.IsNullOrEmpty(ctx.HttpContext.Response.Headers.ContentEncoding.ToString()))
        {
            _logger.LogDebug("[WebsiteProxy] 跳过响应体改写：响应已被压缩 (Content-Encoding 存在)");
            return ValueTask.CompletedTask;
        }

        var media = ctx.ProxyResponse?.Content.Headers.ContentType?.MediaType;
        if (media is null) return ValueTask.CompletedTask;

        var isHtml = media.Contains("html", StringComparison.OrdinalIgnoreCase);
        var isCss = media.Contains("css", StringComparison.OrdinalIgnoreCase);
        if (!isHtml && !isCss) return ValueTask.CompletedTask;

        // 网关前缀使用请求命中的前缀
        var prefix = (ctx.HttpContext.Items[MatchedPrefixKey] as string)
                     ?? $"/{entry.UpstreamScheme}://{entry.Authority}";

        ctx.SuppressResponseBody = true;
        return RewriteBodyCoreAsync(ctx, prefix, isHtml);
    }

    private static async ValueTask RewriteBodyCoreAsync(ResponseTransformContext ctx, string prefix, bool isHtml)
    {
        var response = ctx.ProxyResponse!;
        var stream = await response.Content.ReadAsStreamAsync(ctx.CancellationToken);
        string body;
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync(ctx.CancellationToken);
        }

        // 把 "=\"/site.css\"" 变成 "=\"/prefix/site.css\""：
        // 正则只吃掉 asset 自带的那一个 '/'，因此用 prefix + '/' 补回，避免出现双斜杠（// 会被浏览器当成协议相对 URL）
        body = isHtml
            ? HtmlAttrRootRelativeRegex().Replace(body, prefix + "/")
            : CssUrlRootRelativeRegex().Replace(body, prefix + "/");

        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.HttpContext.Response.ContentLength = bytes.Length;
        await ctx.HttpContext.Response.Body.WriteAsync(bytes, ctx.CancellationToken);
    }

    // ─────────── 响应：改写 Location 头（重定向） ───────────

    private ValueTask RewriteLocationAsync(ResponseTransformContext ctx)
    {
        var entry = ctx.HttpContext.Items[AllowEntryKey] as WebsiteEntry;
        if (entry is null) return ValueTask.CompletedTask;

        var headers = ctx.HttpContext.Response.Headers;
        var loc = headers.Location.ToString();
        if (string.IsNullOrEmpty(loc)) return ValueTask.CompletedTask;

        var prefix = (ctx.HttpContext.Items[MatchedPrefixKey] as string)
                     ?? $"/{entry.UpstreamScheme}://{entry.Authority}";

        // 站内绝对路径 → 加上网关前缀
        if (loc.StartsWith('/') && !loc.StartsWith("//", StringComparison.Ordinal))
        {
            headers.Location = $"{prefix}{loc}";
        }
        // 指向站内自身的绝对 URL → 转成网关前缀形式
        else if (Uri.TryCreate(loc, UriKind.Absolute, out var abs) &&
                 string.Equals(NormalizeAuthority(abs.Authority), entry.Authority, StringComparison.OrdinalIgnoreCase))
        {
            headers.Location = $"{prefix}{abs.PathAndQuery}";
        }
        // 跨域绝对 URL：保留原样，交给浏览器正常跳转
        return ValueTask.CompletedTask;
    }

    // ─────────── 响应：按站点隔离 Cookie 作用域 ───────────

    private ValueTask RewriteCookiesAsync(ResponseTransformContext ctx)
    {
        var entry = ctx.HttpContext.Items[AllowEntryKey] as WebsiteEntry;
        if (entry is not { RewriteCookies: true }) return ValueTask.CompletedTask;

        var headers = ctx.HttpContext.Response.Headers;
        if (!headers.TryGetValue("Set-Cookie", out var values)) return ValueTask.CompletedTask;

        var prefix = (ctx.HttpContext.Items[MatchedPrefixKey] as string)
                     ?? $"/{entry.UpstreamScheme}://{entry.Authority}";
        // Path=/ 或 Path=/xxx → Path=/prefix/...（保留 asset 自身的斜杠，不产生 //）
        var pathPrefix = prefix + "/";
        var rewritten = new List<string>();

        foreach (var raw in values)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            // (?i)(;\s*path=) 捕获 Path= 属性，"/(?!/)" 只吃一个斜杠
            rewritten.Add(CookiePathRegex().Replace(raw, "$1" + pathPrefix.Replace("$", "$$")));
        }

        headers.SetCookie = new Microsoft.Extensions.Primitives.StringValues(rewritten.ToArray());
        return ValueTask.CompletedTask;
    }

    /// <summary>Set-Cookie 中的 Path 属性（仅站内路径，跳过协议相对的 //）</summary>
    [GeneratedRegex("(?i)(;\\s*path=)/(?!/)")]
    private static partial Regex CookiePathRegex();

    /// <summary>归一化 authority：小写、省略默认端口。"/https://example.com" 与 "/http://Example.com:80" 视为同一站点。</summary>
    public static string NormalizeAuthority(string authority)
    {
        if (string.IsNullOrWhiteSpace(authority)) return "";

        var host = authority;
        var portText = "";
        var colon = authority.LastIndexOf(':');
        // 处理 IPv6 字面量，如 [::1]:80
        var bracketEnd = authority.LastIndexOf(']');
        if (colon > bracketEnd)
        {
            host = authority[..colon];
            var rest = authority[(colon + 1)..];
            if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                portText = p.ToString(CultureInfo.InvariantCulture);
            else
                portText = rest;
        }

        host = host.ToLowerInvariant();

        // 省略默认端口：http 用 80，https 用 443。
        // 上游 scheme 由白名单条目决定，这里对两种默认端口都归一，保证用户输入不会因端口写法不同而绕过白名单。
        if (portText is "80" or "443")
            return host;

        return string.IsNullOrEmpty(portText) ? host : $"{host}:{portText}";
    }
}

/// <summary>
/// 内存中的网站代理白名单快照，由 DatabaseProxyConfigProvider 在每次配置重载时整体替换。
/// </summary>
public sealed class WebsiteAllowList
{
    private volatile Dictionary<string, WebsiteEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private volatile Dictionary<string, WebsiteEntry> _aliasMap = new(StringComparer.OrdinalIgnoreCase);

    public void Replace(IEnumerable<WebsiteEntity> entities)
    {
        var map = new Dictionary<string, WebsiteEntry>(StringComparer.OrdinalIgnoreCase);
        var aliasMap = new Dictionary<string, WebsiteEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entities)
        {
            var authority = WebsiteProxyTransformProvider.NormalizeAuthority(e.HostAuthority);
            if (string.IsNullOrEmpty(authority)) continue;
            if (map.ContainsKey(authority)) continue; // 重复 authority 已在 Service 层拦截，这里兜底

            var entry = new WebsiteEntry(
                authority,
                e.TargetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                e.RewriteBody,
                e.RewriteCookies,
                e.AllowedModes ?? "Scheme,Prefix",
                e.Alias);

            map[authority] = entry;

            if (!string.IsNullOrWhiteSpace(e.Alias))
            {
                var cleanAlias = e.Alias.Trim().ToLowerInvariant();
                if (!aliasMap.ContainsKey(cleanAlias))
                    aliasMap[cleanAlias] = entry;
            }
        }

        _entries = map;
        _aliasMap = aliasMap;
    }

    public bool TryGet(string key, out WebsiteEntry? entry)
        => _entries.TryGetValue(key, out entry) || _aliasMap.TryGetValue(key, out entry);

    public bool TryGetByAuthority(string authorityKey, out WebsiteEntry? entry)
        => _entries.TryGetValue(authorityKey, out entry);

    public bool TryGetByAlias(string aliasKey, out WebsiteEntry? entry)
        => _aliasMap.TryGetValue(aliasKey, out entry);

    public int Count => _entries.Count;
}

/// <summary>单个已登记站点的代理参数</summary>
/// <param name="Authority">归一化后的 authority，如 example.com:8080</param>
/// <param name="UseHttps">上游是否走 TLS</param>
/// <param name="RewriteBody">是否改写 HTML/CSS 内的绝对路径</param>
/// <param name="RewriteCookies">是否改写 Set-Cookie 的 Path</param>
/// <param name="AllowedModes">启用的代理模式（如 Scheme,Prefix,Alias）</param>
/// <param name="Alias">可选短别名</param>
public sealed record WebsiteEntry(
    string Authority,
    bool UseHttps,
    bool RewriteBody,
    bool RewriteCookies,
    string AllowedModes,
    string? Alias)
{
    /// <summary>覆盖客户端输入 scheme，只取登记时确定的 scheme</summary>
    public string UpstreamScheme => UseHttps ? "https" : "http";

    /// <summary>校验该站点是否启用了特定的代理模式</summary>
    public bool AllowsMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(AllowedModes)) return false;
        return AllowedModes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase));
    }
}
