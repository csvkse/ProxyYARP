using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;
using System.Data;
using System.Net;

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

    public WebsiteEntity Create(string groupId, string name, string url, bool rewriteBody, bool rewriteCookies)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("名称不能为空");

        var (target, authority) = Normalize(url);

        var existing = _repo.GetByHostAuthority(authority, groupId);
        if (existing != null)
            throw new ArgumentException($"目标网站 '{authority}' 已存在（{existing.Name}），不能重复添加");

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
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        ExecuteWithVersionBump(groupId, (conn, tx) => _repo.InsertTx(entity, conn, tx));
        return entity;
    }

    public bool Update(string id, string groupId, string name, string url, bool rewriteBody, bool rewriteCookies, bool isEnabled)
    {
        var entity = _repo.GetById(id, groupId);
        if (entity == null) return false;

        var (target, authority) = Normalize(url);

        var duplicate = _repo.GetByHostAuthority(authority, groupId);
        if (duplicate != null && duplicate.Id != id)
            throw new ArgumentException($"目标网站 '{authority}' 已被其他条目占用（{duplicate.Name}）");

        entity.Name = string.IsNullOrWhiteSpace(name) ? entity.Name : name.Trim();
        entity.TargetUrl = target;
        entity.HostAuthority = authority;
        entity.RewriteBody = rewriteBody;
        entity.RewriteCookies = rewriteCookies;
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
