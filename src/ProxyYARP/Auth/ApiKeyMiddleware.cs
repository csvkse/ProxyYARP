using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Auth;

/// <summary>
/// API Key 认证中间件
/// 支持三种来源：X-Api-Key Header / ?key= QueryString / api_key Cookie
/// 管理 API（/api/*）强制要求认证
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly string _managementPath;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        var mPath = config["Management:PathBase"] ?? "";
        if (!string.IsNullOrWhiteSpace(mPath) && !mPath.StartsWith("/")) 
            mPath = "/" + mPath;
        _managementPath = mPath;
    }

    public async Task InvokeAsync(HttpContext context, ApiKeyService keyService)
    {
        var path = context.Request.Path.Value ?? "";

        // 静态资源和登录端点直接放行
        if (IsPublicPath(path, _managementPath))
        {
            await _next(context);
            return;
        }

        // 读取 Key
        var key = ExtractKey(context);
        
        bool isApiRoute = path.StartsWith(_managementPath + "/api/", StringComparison.OrdinalIgnoreCase) || 
                          (_managementPath == "" && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase));

        _logger.LogWarning($"[DEBUG] Path: '{path}', _managementPath: '{_managementPath}', isApiRoute: {isApiRoute}, key length: {key?.Length}");

        // 如果不是 API 路由，且即使带了无效的 Key，我们也不应该拦截它，直接放行给 YARP 或其他处理器
        if (!isApiRoute)
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Unauthorized: API key required\"}");
            return;
        }

        // 验证 Key
        var keyEntity = keyService.Validate(key);
        if (keyEntity == null)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Unauthorized: invalid or disabled API key\"}");
            return;
        }

        // 注入 Key 信息到 HttpContext（供后续 API 使用）
        context.Items["ApiKey"] = keyEntity;
        context.Items["ApiKeyRole"] = keyEntity.Role;

        await _next(context);
    }

    private static string? ExtractKey(HttpContext context)
    {
        // 1. Header: X-Api-Key
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var headerKey)
            && !string.IsNullOrWhiteSpace(headerKey))
            return headerKey.ToString().Trim();

        // 2. QueryString: ?key=
        if (context.Request.Query.TryGetValue("key", out var queryKey)
            && !string.IsNullOrWhiteSpace(queryKey))
            return queryKey.ToString().Trim();

        // 3. Cookie: api_key
        if (context.Request.Cookies.TryGetValue("api_key", out var cookieKey)
            && !string.IsNullOrWhiteSpace(cookieKey))
            return cookieKey.Trim();

        return null;
    }

    private static bool IsPublicPath(string path, string managementPath)
    {
        var basePath = string.IsNullOrEmpty(managementPath) ? "" : managementPath;

        if (path == basePath + "/" || path == basePath || path.StartsWith(basePath + "/login", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals(basePath + "/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals(basePath + "/api/version", StringComparison.OrdinalIgnoreCase))
            return true;

        // 静态资源文件
        var staticExtensions = new[] { ".html", ".js", ".css", ".ico", ".png", ".svg", ".woff", ".woff2" };
        foreach (var ext in staticExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>HttpContext 扩展方法：方便从 Items 获取 Key 信息</summary>
public static class HttpContextKeyExtensions
{
    public static ApiKeyEntity? GetApiKey(this HttpContext context)
        => context.Items["ApiKey"] as ApiKeyEntity;

    public static bool IsAdmin(this HttpContext context)
        => context.Items["ApiKeyRole"] as string == KeyRole.Admin;

    public static bool IsAuthenticated(this HttpContext context)
        => context.Items["ApiKey"] != null;
}
