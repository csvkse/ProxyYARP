using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public partial class DestinationRepository : BaseRepository<ProxyDestinationEntity>
{
    public DestinationRepository(IDbProvider provider) : base(provider) { }

    public List<ProxyDestinationEntity> GetByClusterId(string clusterId, string groupId)
    {
        return WithConnection(c => c.Query<ProxyDestinationEntity>(
            """SELECT * FROM "ProxyYARP_Destinations" WHERE "ClusterId" = @ClusterId AND "GroupId" = @GroupId AND "IsEnabled" = @IsEnabled""",
            new { ClusterId = clusterId, GroupId = groupId, IsEnabled = true })
            .AsList());
    }

    public List<ProxyDestinationEntity> GetAllByClusterId(string clusterId, string groupId)
    {
        return WithConnection(c => c.Query<ProxyDestinationEntity>(
            """SELECT * FROM "ProxyYARP_Destinations" WHERE "ClusterId" = @ClusterId AND "GroupId" = @GroupId ORDER BY "CreatedAt" """,
            new { ClusterId = clusterId, GroupId = groupId })
            .AsList());
    }

    public List<ProxyDestinationEntity> GetAll(string groupId)
    {
        return WithConnection(c => c.Query<ProxyDestinationEntity>(
            """SELECT * FROM "ProxyYARP_Destinations" WHERE "GroupId" = @GroupId ORDER BY "ClusterId", "CreatedAt" """, new { GroupId = groupId })
            .AsList());
    }

    public ProxyDestinationEntity? GetById(string id, string groupId)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ProxyDestinationEntity>(
            """SELECT * FROM "ProxyYARP_Destinations" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }

    public void Insert(ProxyDestinationEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyYARP_Destinations" ("Id", "ClusterId", "GroupId", "DestId", "Address", "Health", "Metadata", "IsEnabled", "CreatedAt")
            VALUES (@Id, @ClusterId, @GroupId, @DestId, @Address, @Health, @Metadata, @IsEnabled, @CreatedAt)
            """,
            entity));
    }

    public void Update(ProxyDestinationEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyYARP_Destinations"
            SET "DestId" = @DestId, "Address" = @Address, "Health" = @Health,
                "Metadata" = @Metadata, "IsEnabled" = @IsEnabled
            WHERE "Id" = @Id AND "GroupId" = @GroupId
            """,
            entity));
    }

    public void Delete(string id, string groupId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyYARP_Destinations" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }));
    }

    public void DeleteByClusterId(string clusterId, string groupId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyYARP_Destinations" WHERE "ClusterId" = @ClusterId AND "GroupId" = @GroupId""",
            new { ClusterId = clusterId, GroupId = groupId }));
    }
}

