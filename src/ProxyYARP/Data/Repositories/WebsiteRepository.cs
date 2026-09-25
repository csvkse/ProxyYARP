using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;
using System.Data;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public partial class WebsiteRepository : BaseRepository<WebsiteEntity>
{
    public WebsiteRepository(IDbProvider provider) : base(provider) { }

    public List<WebsiteEntity> GetAll(string groupId)
    {
        return WithConnection(c => c.Query<WebsiteEntity>(
            """SELECT * FROM "ProxyYARP_Websites" WHERE "GroupId" = @GroupId ORDER BY "CreatedAt" ASC""", new { GroupId = groupId })
            .AsList());
    }

    public List<WebsiteEntity> GetAllEnabled(string groupId)
    {
        return WithConnection(c => c.Query<WebsiteEntity>(
            """SELECT * FROM "ProxyYARP_Websites" WHERE "GroupId" = @GroupId AND "IsEnabled" = @IsEnabled ORDER BY "CreatedAt" ASC""",
            new { GroupId = groupId, IsEnabled = true })
            .AsList());
    }

    public WebsiteEntity? GetById(string id, string groupId)
    {
        return WithConnection(c => c.QueryFirstOrDefault<WebsiteEntity>(
            """SELECT * FROM "ProxyYARP_Websites" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }

    /// <summary>按 authority 查找，用于创建/更新时的重复校验</summary>
    public WebsiteEntity? GetByHostAuthority(string hostAuthority, string groupId)
    {
        return WithConnection(c => c.QueryFirstOrDefault<WebsiteEntity>(
            """SELECT * FROM "ProxyYARP_Websites" WHERE "HostAuthority" = @HostAuthority AND "GroupId" = @GroupId""",
            new { HostAuthority = hostAuthority, GroupId = groupId }));
    }

    public void Insert(WebsiteEntity entity)
        => WithConnection(c => InsertTx(entity, c, null));

    /// <summary>带事务的插入（供 Service 与版本号自增同事务提交）</summary>
    public void InsertTx(WebsiteEntity entity, IDbConnection conn, IDbTransaction? tx)
    {
        conn.Execute("""
            INSERT INTO "ProxyYARP_Websites"
            ("Id", "GroupId", "Name", "TargetUrl", "HostAuthority", "RewriteBody", "RewriteCookies", "IsEnabled", "CreatedAt", "UpdatedAt")
            VALUES
            (@Id, @GroupId, @Name, @TargetUrl, @HostAuthority, @RewriteBody, @RewriteCookies, @IsEnabled, @CreatedAt, @UpdatedAt)
            """, entity, tx);
    }

    public void Update(WebsiteEntity entity)
        => WithConnection(c => UpdateTx(entity, c, null));

    /// <summary>带事务的更新</summary>
    public void UpdateTx(WebsiteEntity entity, IDbConnection conn, IDbTransaction? tx)
    {
        conn.Execute("""
            UPDATE "ProxyYARP_Websites" SET
                "Name" = @Name,
                "TargetUrl" = @TargetUrl,
                "HostAuthority" = @HostAuthority,
                "RewriteBody" = @RewriteBody,
                "RewriteCookies" = @RewriteCookies,
                "IsEnabled" = @IsEnabled,
                "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id AND "GroupId" = @GroupId
            """, entity, tx);
    }

    public void Delete(string id, string groupId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyYARP_Websites" WHERE "Id" = @Id AND "GroupId" = @GroupId""",
            new { Id = id, GroupId = groupId }));
    }
}
