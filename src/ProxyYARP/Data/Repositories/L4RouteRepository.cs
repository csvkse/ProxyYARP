using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public partial class L4RouteRepository : BaseRepository<L4ProxyRouteEntity>
{
    public L4RouteRepository(IDbProvider provider) : base(provider) { }

    public List<L4ProxyRouteEntity> GetAll(string groupId)
    {
        return WithConnection(c => c.Query<L4ProxyRouteEntity>(
            """SELECT * FROM "ProxyYARP_L4Routes" WHERE "GroupId" = @GroupId ORDER BY "ListenPort" ASC""", new { GroupId = groupId })
            .AsList());
    }

    public List<L4ProxyRouteEntity> GetAllEnabled(string groupId)
    {
        return WithConnection(c => c.Query<L4ProxyRouteEntity>(
            """SELECT * FROM "ProxyYARP_L4Routes" WHERE "GroupId" = @GroupId AND "IsEnabled" = @IsEnabled ORDER BY "ListenPort" ASC""", new { GroupId = groupId, IsEnabled = true })
            .AsList());
    }

    public L4ProxyRouteEntity? GetById(string id, string groupId)
    {
        return WithConnection(c => c.QueryFirstOrDefault<L4ProxyRouteEntity>(
            """SELECT * FROM "ProxyYARP_L4Routes" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }

    public L4ProxyRouteEntity? GetByListenPort(int port, string groupId)
    {
        return WithConnection(c => c.QueryFirstOrDefault<L4ProxyRouteEntity>(
            """SELECT * FROM "ProxyYARP_L4Routes" WHERE "ListenPort" = @ListenPort AND "GroupId" = @GroupId""", new { ListenPort = port, GroupId = groupId }));
    }

    public void Insert(L4ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyYARP_L4Routes"
            ("Id", "RouteId", "GroupId", "ListenPort", "Protocol", "LoadBalancingPolicy", "IdleTimeoutSeconds", "IsEnabled", "CreatedAt", "UpdatedAt")
            VALUES
            (@Id, @RouteId, @GroupId, @ListenPort, @Protocol, @LoadBalancingPolicy, @IdleTimeoutSeconds, @IsEnabled, @CreatedAt, @UpdatedAt)
            """,
            entity));
    }

    public void Update(L4ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyYARP_L4Routes" SET
                "RouteId" = @RouteId,
                "ListenPort" = @ListenPort,
                "Protocol" = @Protocol,
                "LoadBalancingPolicy" = @LoadBalancingPolicy,
                "IdleTimeoutSeconds" = @IdleTimeoutSeconds,
                "IsEnabled" = @IsEnabled,
                "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id AND "GroupId" = @GroupId
            """,
            entity));
    }

    public void Delete(string id, string groupId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyYARP_L4Routes" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }
}

