using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class RouteRepository : BaseRepository<ProxyRouteEntity>
{
    public RouteRepository(IDbProvider provider) : base(provider) { }

    public List<ProxyRouteEntity> GetAllEnabled(string groupId)
    {
        return WithConnection(c => c.Query<ProxyRouteEntity>(
            """SELECT * FROM "ProxyYARP_Routes" WHERE "GroupId" = @GroupId AND "IsEnabled" = @IsEnabled ORDER BY "Order" ASC""", new { GroupId = groupId, IsEnabled = true })
            .AsList());
    }

    public List<ProxyRouteEntity> GetAll(string groupId)
    {
        return WithConnection(c => c.Query<ProxyRouteEntity>(
            """SELECT * FROM "ProxyYARP_Routes" WHERE "GroupId" = @GroupId ORDER BY "CreatedAt" DESC""", new { GroupId = groupId })
            .AsList());
    }

    public ProxyRouteEntity? GetById(string id, string groupId)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ProxyRouteEntity>(
            """SELECT * FROM "ProxyYARP_Routes" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }

    public void Insert(ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyYARP_Routes" ("Id", "RouteId", "GroupId", "ClusterId", "Path", "Methods", "Hosts", "Order", "IsEnabled", "Metadata", "CreatedAt", "UpdatedAt")
            VALUES (@Id, @RouteId, @GroupId, @ClusterId, @Path, @Methods, @Hosts, @Order, @IsEnabled, @Metadata, @CreatedAt, @UpdatedAt)
            """,
            entity));
    }

    public void Update(ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyYARP_Routes"
            SET "RouteId" = @RouteId, "ClusterId" = @ClusterId, "Path" = @Path,
                "Methods" = @Methods, "Hosts" = @Hosts, "Order" = @Order,
                "IsEnabled" = @IsEnabled, "Metadata" = @Metadata, "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id AND "GroupId" = @GroupId
            """,
            entity));
    }

    public void Delete(string id, string groupId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyYARP_Routes" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }
}
