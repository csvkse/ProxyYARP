using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;
using System.Data;
using System.Net;
using System.Text.RegularExpressions;

namespace ProxyYARP.Data.Services;

/// <summary>网站代理配置业务服务</summary>
[DapperAot]
public partial class WebsiteConfigService
{
    private readonly IDbProvider _provider;
    private readonly WebsiteRepository _repo;

    public WebsiteConfigService(IDbProvider provider, WebsiteRepository repo)
    {
        _provider = provider;
        _repo = repo;
    }

    private void ExecuteWithVersionBump(string groupId, Action<IDbConnection, IDbTransaction> action)
    {
        using var conn = _provider.CreateConnection();
        if (conn.State != ConnectionState.Open) conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            action(conn, tx);
            conn.Execute("""UPDATE "ProxyYARP_ConfigGroups" SET "ConfigVersion" = "ConfigVersion" + 1 WHERE "Id" = @Id""",
                new { Id = groupId }, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public List<WebsiteEntity> GetAll(string groupId) => _repo.GetAll(groupId);

    public List<WebsiteEntity> GetAllEnabled(string groupId) => _repo.GetAllEnabled(groupId);

    public WebsiteEntity? GetById(string id, string groupId) => _repo.GetById(id, groupId);

    /// <summary>
    /// 规范化用户输入的网址：补全 scheme、去掉尾部斜杠、取出 authority。
    /// 非法输入抛 ArgumentException。
    /// </summary>
    public static (string TargetUrl, string HostAuthority) Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("网站地址不能为空");

        var raw = url.Trim();
        // scheme:// 被视为完整输入；缺省时补 https
        var candidate = raw.Contains("://", StringComparison.Ordinal) ? raw : "https://" + raw;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            throw new ArgumentException($"网站地址 '{raw}' 不是合法 URL");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException($"仅支持 http/https 协议，当前为 {uri.Scheme}://");

        if (!uri.IsDefaultPort && uri.Port == 0)
            throw new ArgumentException("端口无效");

        // 归一化 authority：小写，省略默认端口，保证 "http://a.com" 与 "http://A.com:80" 匹配同一条
        var authority = uri.IsDefaultPort
            ? uri.Host.ToLowerInvariant()
            : $"{uri.Host.ToLowerInvariant()}:{uri.Port}";

        // TargetUrl 保留 scheme 与 authority（决定访问时是 http:// 还是 https:// 上游）
        var scheme = uri.Scheme;
        var target = $"{scheme}://{authority}";

        return (target, authority);
    }

    /// <summary>
    /// 规范化短别名：小写、去除首尾空格、合法性校验与保留字拦截。
    /// 为空时返回 null。
    /// </summary>
    public static string? NormalizeAlias(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return null;
        var trimmed = alias.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(trimmed, @"^[a-z0-9][a-z0-9\-_]{0,63}$"))
            throw new ArgumentException("短别名必须以字母或数字开头，仅包含小写字母、数字、中划线和下划线，长度在 1 到 64 之间");

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "proxy", "http", "https", "api", "scalar", "openapi", "swagger", "static", "ws"
        };
        if (reserved.Contains(trimmed))
            throw new ArgumentException($"短别名 '{trimmed}' 为系统保留关键字，不能使用");

        return trimmed;
    }

    /// <summary>
    /// 规范化允许的代理模式集合，默认 "Scheme,Prefix"。
    /// 可选模式：Scheme (协议内嵌), Prefix (路径前缀 /proxy/), Alias (短别名 /s/)。
    /// </summary>
    public static string NormalizeAllowedModes(string? modes, string? alias)
    {
        if (string.IsNullOrWhiteSpace(modes))
            modes = "Scheme,Prefix";

        var validModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Scheme", "Prefix", "Alias" };
        var list = modes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(m => validModes.Contains(m))
                        .Select(m => m switch
                        {
                            _ when string.Equals(m, "Scheme", StringComparison.OrdinalIgnoreCase) => "Scheme",
                            _ when string.Equals(m, "Prefix", StringComparison.OrdinalIgnoreCase) => "Prefix",
                            _ when string.Equals(m, "Alias", StringComparison.OrdinalIgnoreCase) => "Alias",
                            _ => m
                        })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

        if (list.Count == 0)
            throw new ArgumentException("至少需要选择一种代理模式");

        if (list.Contains("Alias") && string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("启用短别名模式时必须指定短别名");

        return string.Join(",", list);
    }

    public WebsiteEntity Create(string groupId, string name, string url, bool rewriteBody, bool rewriteCookies, string? allowedModes = null, string? alias = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("名称不能为空");

        var (target, authority) = Normalize(url);
        var normAlias = NormalizeAlias(alias);
        var normModes = NormalizeAllowedModes(allowedModes, normAlias);

        var existing = _repo.GetByHostAuthority(authority, groupId);
        if (existing != null)
            throw new ArgumentException($"目标网站 '{authority}' 已存在（{existing.Name}），不能重复添加");

        if (normAlias != null)
        {
            var dupAlias = _repo.GetByAlias(normAlias, groupId);
            if (dupAlias != null)
                throw new ArgumentException($"短别名 '{normAlias}' 已被其他条目占用（{dupAlias.Name}），不能重复使用");
        }

        var now = DateTime.UtcNow;
        var entity = new WebsiteEntity
        {
            Id = Guid.NewGuid().ToString(),
            GroupId = groupId,
            Name = name.Trim(),
            TargetUrl = target,
            HostAuthority = authority,
            RewriteBody = rewriteBody,
            RewriteCookies = rewriteCookies,
            AllowedModes = normModes,
            Alias = normAlias,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        ExecuteWithVersionBump(groupId, (conn, tx) => _repo.InsertTx(entity, conn, tx));
        return entity;
    }

    public bool Update(string id, string groupId, string name, string url, bool rewriteBody, bool rewriteCookies, bool isEnabled, string? allowedModes = null, string? alias = null)
    {
        var entity = _repo.GetById(id, groupId);
        if (entity == null) return false;

        var (target, authority) = Normalize(url);
        var normAlias = NormalizeAlias(alias);
        var normModes = NormalizeAllowedModes(allowedModes, normAlias);

        var duplicate = _repo.GetByHostAuthority(authority, groupId);
        if (duplicate != null && duplicate.Id != id)
            throw new ArgumentException($"目标网站 '{authority}' 已被其他条目占用（{duplicate.Name}）");

        if (normAlias != null)
        {
            var dupAlias = _repo.GetByAlias(normAlias, groupId);
            if (dupAlias != null && dupAlias.Id != id)
                throw new ArgumentException($"短别名 '{normAlias}' 已被其他条目占用（{dupAlias.Name}）");
        }

        entity.Name = string.IsNullOrWhiteSpace(name) ? entity.Name : name.Trim();
        entity.TargetUrl = target;
        entity.HostAuthority = authority;
        entity.RewriteBody = rewriteBody;
        entity.RewriteCookies = rewriteCookies;
        entity.AllowedModes = normModes;
        entity.Alias = normAlias;
        entity.IsEnabled = isEnabled;
        entity.UpdatedAt = DateTime.UtcNow;

        ExecuteWithVersionBump(groupId, (conn, tx) => _repo.UpdateTx(entity, conn, tx));
        return true;
    }

    public bool Delete(string id, string groupId)
    {
        var entity = _repo.GetById(id, groupId);
        if (entity == null) return false;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""DELETE FROM "ProxyYARP_Websites" WHERE "Id" = @Id AND "GroupId" = @GroupId""",
                new { Id = id, GroupId = groupId }, tx);
        });
        return true;
    }
}
