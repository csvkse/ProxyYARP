using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class DestinationRepository : BaseRepository<ProxyDestinationEntity>
{
    public DestinationRepository(IDbProvider provider) : base(provider) { }

    public List<ProxyDestinationEntity> GetByClusterId(string clusterId)
    {
        return WithConnection(c => c.Query<ProxyDestinationEntity>(
            """SELECT * FROM "ProxyDestinations" WHERE "ClusterId" = @ClusterId AND "IsEnabled" = TRUE""",
            new { ClusterId = clusterId })
            .AsList());
    }

    public List<ProxyDestinationEntity> GetAllByClusterId(string clusterId)
    {
        return WithConnection(c => c.Query<ProxyDestinationEntity>(
            """SELECT * FROM "ProxyDestinations" WHERE "ClusterId" = @ClusterId ORDER BY "CreatedAt" """,
            new { ClusterId = clusterId })
            .AsList());
    }

    public List<ProxyDestinationEntity> GetAll()
    {
        return WithConnection(c => c.Query<ProxyDestinationEntity>(
            """SELECT * FROM "ProxyDestinations" ORDER BY "ClusterId", "CreatedAt" """)
            .AsList());
    }

    public ProxyDestinationEntity? GetById(string id)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ProxyDestinationEntity>(
            """SELECT * FROM "ProxyDestinations" WHERE "Id" = @Id""", new { Id = id }));
    }

    public void Insert(ProxyDestinationEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyDestinations" ("Id", "ClusterId", "DestId", "Address", "Health", "Metadata", "IsEnabled", "CreatedAt")
            VALUES (@Id, @ClusterId, @DestId, @Address, @Health, @Metadata, @IsEnabled, @CreatedAt)
            """,
            entity));
    }

    public void Update(ProxyDestinationEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyDestinations"
            SET "DestId" = @DestId, "Address" = @Address, "Health" = @Health,
                "Metadata" = @Metadata, "IsEnabled" = @IsEnabled
            WHERE "Id" = @Id
            """,
            entity));
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyDestinations" WHERE "Id" = @Id""", new { Id = id }));
    }

    public void DeleteByClusterId(string clusterId)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyDestinations" WHERE "ClusterId" = @ClusterId""",
            new { ClusterId = clusterId }));
    }
}
