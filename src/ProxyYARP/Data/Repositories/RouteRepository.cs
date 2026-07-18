using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class RouteRepository : BaseRepository<ProxyRouteEntity>
{
    public RouteRepository(IDbProvider provider) : base(provider) { }

    public List<ProxyRouteEntity> GetAllEnabled()
    {
        return WithConnection(c => c.Query<ProxyRouteEntity>(
            """SELECT * FROM "ProxyRoutes" WHERE "IsEnabled" = TRUE ORDER BY "Order" ASC""")
            .AsList());
    }

    public List<ProxyRouteEntity> GetAll()
    {
        return WithConnection(c => c.Query<ProxyRouteEntity>(
            """SELECT * FROM "ProxyRoutes" ORDER BY "CreatedAt" DESC""")
            .AsList());
    }

    public ProxyRouteEntity? GetById(string id)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ProxyRouteEntity>(
            """SELECT * FROM "ProxyRoutes" WHERE "Id" = @Id""", new { Id = id }));
    }

    public void Insert(ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyRoutes" ("Id", "RouteId", "ClusterId", "Path", "Methods", "Hosts", "Order", "IsEnabled", "Metadata", "CreatedAt", "UpdatedAt")
            VALUES (@Id, @RouteId, @ClusterId, @Path, @Methods, @Hosts, @Order, @IsEnabled, @Metadata, @CreatedAt, @UpdatedAt)
            """,
            entity));
    }

    public void Update(ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyRoutes"
            SET "RouteId" = @RouteId, "ClusterId" = @ClusterId, "Path" = @Path,
                "Methods" = @Methods, "Hosts" = @Hosts, "Order" = @Order,
                "IsEnabled" = @IsEnabled, "Metadata" = @Metadata, "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id
            """,
            entity));
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyRoutes" WHERE "Id" = @Id""", new { Id = id }));
    }
}
