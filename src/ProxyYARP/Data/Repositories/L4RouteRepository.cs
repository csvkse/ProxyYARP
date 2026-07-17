using Dapper;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class L4RouteRepository : BaseRepository<L4ProxyRouteEntity>
{
    public L4RouteRepository(System.Data.IDbConnection? connection = null) : base(connection) { }

    public void CreateTable()
    {
        WithConnection(c => c.Execute(@"
            CREATE TABLE IF NOT EXISTS ProxyL4Routes (
                Id TEXT PRIMARY KEY,
                RouteId TEXT NOT NULL,
                ListenPort INTEGER NOT NULL UNIQUE,
                LoadBalancingPolicy TEXT NOT NULL DEFAULT 'RoundRobin',
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
        "));
    }

    public List<L4ProxyRouteEntity> GetAll()
    {
        return WithConnection(c => c.Query<L4ProxyRouteEntity>(
            "SELECT * FROM ProxyL4Routes ORDER BY ListenPort ASC")
            .AsList());
    }

    public List<L4ProxyRouteEntity> GetAllEnabled()
    {
        return WithConnection(c => c.Query<L4ProxyRouteEntity>(
            "SELECT * FROM ProxyL4Routes WHERE IsEnabled = 1 ORDER BY ListenPort ASC")
            .AsList());
    }

    public L4ProxyRouteEntity? GetById(string id)
    {
        return WithConnection(c => c.QueryFirstOrDefault<L4ProxyRouteEntity>(
            "SELECT * FROM ProxyL4Routes WHERE Id = @Id", new { Id = id }));
    }
    
    public L4ProxyRouteEntity? GetByListenPort(int port)
    {
        return WithConnection(c => c.QueryFirstOrDefault<L4ProxyRouteEntity>(
            "SELECT * FROM ProxyL4Routes WHERE ListenPort = @ListenPort", new { ListenPort = port }));
    }

    public void Insert(L4ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute(@"
            INSERT INTO ProxyL4Routes 
            (Id, RouteId, ListenPort, LoadBalancingPolicy, IsEnabled, CreatedAt, UpdatedAt)
            VALUES 
            (@Id, @RouteId, @ListenPort, @LoadBalancingPolicy, @IsEnabled, @CreatedAt, @UpdatedAt)",
            entity));
    }

    public void Update(L4ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute(@"
            UPDATE ProxyL4Routes SET 
                RouteId = @RouteId,
                ListenPort = @ListenPort,
                LoadBalancingPolicy = @LoadBalancingPolicy,
                IsEnabled = @IsEnabled,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id",
            entity));
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("DELETE FROM ProxyL4Routes WHERE Id = @Id", new { Id = id }));
    }
}
