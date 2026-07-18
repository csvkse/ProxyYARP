using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class ProxyConfigGroupRepository : BaseRepository<ProxyConfigGroupEntity>
{
    public ProxyConfigGroupRepository(IDbProvider provider) : base(provider) { }

    public void Upsert(string groupId)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyYARP_ConfigGroups" ("Id", "Name", "ConfigVersion")
            SELECT @Id, @Id, 1
            WHERE NOT EXISTS (SELECT 1 FROM "ProxyYARP_ConfigGroups" WHERE "Id" = @Id)
            """, new { Id = groupId }));
    }

    public int GetVersion(string groupId)
    {
        return WithConnection(c => c.ExecuteScalar<int>("""SELECT "ConfigVersion" FROM "ProxyYARP_ConfigGroups" WHERE "Id" = @Id""", new { Id = groupId }));
    }

    public void IncrementVersion(string groupId)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyYARP_ConfigGroups" SET "ConfigVersion" = "ConfigVersion" + 1 WHERE "Id" = @Id
            """, new { Id = groupId }));
    }

    public List<ProxyConfigGroupEntity> GetAll()
    {
        return WithConnection(c => c.Query<ProxyConfigGroupEntity>("""SELECT * FROM "ProxyYARP_ConfigGroups" ORDER BY "Id" ASC""").AsList());
    }
}
