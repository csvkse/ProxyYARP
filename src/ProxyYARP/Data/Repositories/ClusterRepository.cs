using Dapper;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class ClusterRepository : BaseRepository<ProxyClusterEntity>
{
    public ClusterRepository(System.Data.IDbConnection? connection = null) : base(connection) { }

    public void CreateTable()
    {
        WithConnection(c => c.Execute(@"
            CREATE TABLE IF NOT EXISTS ProxyClusters (
                Id                  TEXT PRIMARY KEY,
                ClusterId           TEXT NOT NULL UNIQUE,
                LoadBalancing       TEXT NOT NULL DEFAULT 'RoundRobin',
                HealthCheckEnabled  TEXT,
                IsEnabled           INTEGER NOT NULL DEFAULT 1,
                CreatedAt           TEXT NOT NULL,
                UpdatedAt           TEXT NOT NULL
            );
        "));
    }

    public List<ProxyClusterEntity> GetAllEnabled()
    {
        return WithConnection(c => c.Query<ProxyClusterEntity>(
            "SELECT * FROM ProxyClusters WHERE IsEnabled = 1")
            .AsList());
    }

    public List<ProxyClusterEntity> GetAll()
    {
        return WithConnection(c => c.Query<ProxyClusterEntity>(
            "SELECT * FROM ProxyClusters ORDER BY CreatedAt DESC")
            .AsList());
    }

    public ProxyClusterEntity? GetById(string id)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ProxyClusterEntity>(
            "SELECT * FROM ProxyClusters WHERE Id = @Id", new { Id = id }));
    }

    public void Insert(ProxyClusterEntity entity)
    {
        WithConnection(c => c.Execute(@"
            INSERT INTO ProxyClusters (Id, ClusterId, LoadBalancing, HealthCheckEnabled, IsEnabled, CreatedAt, UpdatedAt)
            VALUES (@Id, @ClusterId, @LoadBalancing, @HealthCheckEnabled, @IsEnabled, @CreatedAt, @UpdatedAt)",
            entity));
    }

    public void Update(ProxyClusterEntity entity)
    {
        WithConnection(c => c.Execute(@"
            UPDATE ProxyClusters
            SET ClusterId = @ClusterId, LoadBalancing = @LoadBalancing,
                HealthCheckEnabled = @HealthCheckEnabled, IsEnabled = @IsEnabled, UpdatedAt = @UpdatedAt
            WHERE Id = @Id",
            entity));
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("DELETE FROM ProxyClusters WHERE Id = @Id", new { Id = id }));
    }
}
