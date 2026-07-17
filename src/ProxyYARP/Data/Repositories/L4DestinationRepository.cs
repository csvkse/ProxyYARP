using Dapper;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class L4DestinationRepository : BaseRepository<L4ProxyDestinationEntity>
{
    public L4DestinationRepository(System.Data.IDbConnection? connection = null) : base(connection) { }

    public void CreateTable()
    {
        WithConnection(c => c.Execute(@"
            CREATE TABLE IF NOT EXISTS ProxyL4Destinations (
                Id TEXT PRIMARY KEY,
                RouteId TEXT NOT NULL,
                TargetHost TEXT NOT NULL,
                TargetPort INTEGER NOT NULL,
                Weight INTEGER NOT NULL DEFAULT 1,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
        "));
    }

    public List<L4ProxyDestinationEntity> GetByRouteId(string routeId)
    {
        return WithConnection(c => c.Query<L4ProxyDestinationEntity>(
            "SELECT * FROM ProxyL4Destinations WHERE RouteId = @RouteId AND IsEnabled = 1", new { RouteId = routeId })
            .AsList());
    }
    
    public List<L4ProxyDestinationEntity> GetAll()
    {
        return WithConnection(c => c.Query<L4ProxyDestinationEntity>("SELECT * FROM ProxyL4Destinations").AsList());
    }

    public void Insert(L4ProxyDestinationEntity entity)
    {
        WithConnection(c => c.Execute(@"
            INSERT INTO ProxyL4Destinations 
            (Id, RouteId, TargetHost, TargetPort, Weight, IsEnabled, CreatedAt, UpdatedAt)
            VALUES 
            (@Id, @RouteId, @TargetHost, @TargetPort, @Weight, @IsEnabled, @CreatedAt, @UpdatedAt)",
            entity));
    }

    public void DeleteByRouteId(string routeId)
    {
        WithConnection(c => c.Execute("DELETE FROM ProxyL4Destinations WHERE RouteId = @RouteId", new { RouteId = routeId }));
    }
}
