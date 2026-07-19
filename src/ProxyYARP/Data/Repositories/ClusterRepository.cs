using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public partial class ClusterRepository : BaseRepository<ProxyClusterEntity>
{
    public ClusterRepository(IDbProvider provider) : base(provider) { }

    public List<ProxyClusterEntity> GetAllEnabled(string groupId)
    {
        return WithConnection(c => c.Query<ProxyClusterEntity>(
            """SELECT * FROM "ProxyYARP_Clusters" WHERE "GroupId" = @GroupId AND "IsEnabled" = @IsEnabled""", new { GroupId = groupId, IsEnabled = true })
            .AsList());
    }

    public List<ProxyClusterEntity> GetAll(string groupId)
    {
        return WithConnection(c => c.Query<ProxyClusterEntity>(
            """SELECT * FROM "ProxyYARP_Clusters" WHERE "GroupId" = @GroupId ORDER BY "CreatedAt" DESC""", new { GroupId = groupId })
            .AsList());
    }

    public ProxyClusterEntity? GetById(string id, string groupId)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ProxyClusterEntity>(
            """SELECT * FROM "ProxyYARP_Clusters" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }

    public void Insert(ProxyClusterEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyYARP_Clusters" ("Id", "ClusterId", "GroupId", "LoadBalancing", "HealthCheckEnabled", "IsEnabled", "CreatedAt", "UpdatedAt")
            VALUES (@Id, @ClusterId, @GroupId, @LoadBalancing, @HealthCheckEnabled, @IsEnabled, @CreatedAt, @UpdatedAt)
            """,
            entity));
    }

    public void Update(ProxyClusterEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyYARP_Clusters"
            SET "ClusterId" = @ClusterId, "LoadBalancing" = @LoadBalancing,
                "HealthCheckEnabled" = @HealthCheckEnabled, "IsEnabled" = @IsEnabled, "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id AND "GroupId" = @GroupId
            """,
            entity));
    }

    public void Delete(string id, string groupId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyYARP_Clusters" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }
}

