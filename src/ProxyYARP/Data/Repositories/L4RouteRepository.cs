using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class L4RouteRepository : BaseRepository<L4ProxyRouteEntity>
{
    public L4RouteRepository(IDbProvider provider) : base(provider) { }

    public List<L4ProxyRouteEntity> GetAll()
    {
        return WithConnection(c => c.Query<L4ProxyRouteEntity>(
            """SELECT * FROM "ProxyL4Routes" ORDER BY "ListenPort" ASC""")
            .AsList());
    }

    public List<L4ProxyRouteEntity> GetAllEnabled()
    {
        return WithConnection(c => c.Query<L4ProxyRouteEntity>(
            """SELECT * FROM "ProxyL4Routes" WHERE "IsEnabled" = TRUE ORDER BY "ListenPort" ASC""")
            .AsList());
    }

    public L4ProxyRouteEntity? GetById(string id)
    {
        return WithConnection(c => c.QueryFirstOrDefault<L4ProxyRouteEntity>(
            """SELECT * FROM "ProxyL4Routes" WHERE "Id" = @Id""", new { Id = id }));
    }

    public L4ProxyRouteEntity? GetByListenPort(int port)
    {
        return WithConnection(c => c.QueryFirstOrDefault<L4ProxyRouteEntity>(
            """SELECT * FROM "ProxyL4Routes" WHERE "ListenPort" = @ListenPort""", new { ListenPort = port }));
    }

    public void Insert(L4ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyL4Routes"
            ("Id", "RouteId", "ListenPort", "Protocol", "LoadBalancingPolicy", "IdleTimeoutSeconds", "IsEnabled", "CreatedAt", "UpdatedAt")
            VALUES
            (@Id, @RouteId, @ListenPort, @Protocol, @LoadBalancingPolicy, @IdleTimeoutSeconds, @IsEnabled, @CreatedAt, @UpdatedAt)
            """,
            entity));
    }

    public void Update(L4ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyL4Routes" SET
                "RouteId" = @RouteId,
                "ListenPort" = @ListenPort,
                "Protocol" = @Protocol,
                "LoadBalancingPolicy" = @LoadBalancingPolicy,
                "IdleTimeoutSeconds" = @IdleTimeoutSeconds,
                "IsEnabled" = @IsEnabled,
                "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id
            """,
            entity));
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyL4Routes" WHERE "Id" = @Id""", new { Id = id }));
    }
}
