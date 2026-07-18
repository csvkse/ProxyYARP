using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class L4DestinationRepository : BaseRepository<L4ProxyDestinationEntity>
{
    public L4DestinationRepository(IDbProvider provider) : base(provider) { }

    public List<L4ProxyDestinationEntity> GetByRouteId(string routeId)
    {
        return WithConnection(c => c.Query<L4ProxyDestinationEntity>(
            """SELECT * FROM "ProxyL4Destinations" WHERE "RouteId" = @RouteId AND "IsEnabled" = TRUE""", new { RouteId = routeId })
            .AsList());
    }

    public List<L4ProxyDestinationEntity> GetAll()
    {
        return WithConnection(c => c.Query<L4ProxyDestinationEntity>(
            """SELECT * FROM "ProxyL4Destinations" """).AsList());
    }

    public void Insert(L4ProxyDestinationEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyL4Destinations"
            ("Id", "RouteId", "TargetHost", "TargetPort", "Weight", "IsEnabled", "CreatedAt", "UpdatedAt")
            VALUES
            (@Id, @RouteId, @TargetHost, @TargetPort, @Weight, @IsEnabled, @CreatedAt, @UpdatedAt)
            """,
            entity));
    }

    public void DeleteByRouteId(string routeId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyL4Destinations" WHERE "RouteId" = @RouteId""", new { RouteId = routeId }));
    }
}
