using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class L4DestinationRepository : BaseRepository<L4ProxyDestinationEntity>
{
    public L4DestinationRepository(IDbProvider provider) : base(provider) { }

    public List<L4ProxyDestinationEntity> GetByRouteId(string routeId, string groupId)
    {
        return WithConnection(c => c.Query<L4ProxyDestinationEntity>(
            """SELECT * FROM "ProxyYARP_L4Destinations" WHERE "RouteId" = @RouteId AND "GroupId" = @GroupId AND "IsEnabled" = @IsEnabled""", new { RouteId = routeId, GroupId = groupId, IsEnabled = true })
            .AsList());
    }

    public List<L4ProxyDestinationEntity> GetAll(string groupId)
    {
        return WithConnection(c => c.Query<L4ProxyDestinationEntity>(
            """SELECT * FROM "ProxyYARP_L4Destinations" WHERE "GroupId" = @GroupId""", new { GroupId = groupId }).AsList());
    }

    public void Insert(L4ProxyDestinationEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyYARP_L4Destinations"
            ("Id", "RouteId", "GroupId", "TargetHost", "TargetPort", "Weight", "IsEnabled", "CreatedAt", "UpdatedAt")
            VALUES
            (@Id, @RouteId, @GroupId, @TargetHost, @TargetPort, @Weight, @IsEnabled, @CreatedAt, @UpdatedAt)
            """,
            entity));
    }

    public void DeleteByRouteId(string routeId, string groupId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyYARP_L4Destinations" WHERE "RouteId" = @RouteId AND "GroupId" = @GroupId""", new { RouteId = routeId, GroupId = groupId }));
    }
}
