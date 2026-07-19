using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public partial class ProxyConfigGroupRepository : BaseRepository<ProxyConfigGroupEntity>
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

    public List<ProxyYARP.Api.GroupDetailDto> GetGroupDetails()
    {
        return WithConnection(c => c.Query<ProxyYARP.Api.GroupDetailDto>("""
            SELECT 
                g."Id" as GroupId, 
                g."ConfigVersion" as Version,
                (SELECT COUNT(*) FROM "ProxyYARP_Nodes" n WHERE n."GroupId" = g."Id") as NodeCount,
                (SELECT COUNT(*) FROM "ProxyYARP_Routes" r WHERE r."GroupId" = g."Id") as RouteCount,
                (SELECT COUNT(*) FROM "ProxyYARP_Clusters" c WHERE c."GroupId" = g."Id") as ClusterCount,
                (SELECT COUNT(*) FROM "ProxyYARP_L4Routes" l4 WHERE l4."GroupId" = g."Id") as L4RouteCount
            FROM "ProxyYARP_ConfigGroups" g
            ORDER BY g."Id" ASC
            """).AsList());
    }

    public void DeleteGroup(string groupId)
    {
        WithConnection(c => c.Execute("""
            DELETE FROM "ProxyYARP_Destinations" WHERE "GroupId" = @Id;
            DELETE FROM "ProxyYARP_Routes" WHERE "GroupId" = @Id;
            DELETE FROM "ProxyYARP_Clusters" WHERE "GroupId" = @Id;
            DELETE FROM "ProxyYARP_L4Destinations" WHERE "GroupId" = @Id;
            DELETE FROM "ProxyYARP_L4Routes" WHERE "GroupId" = @Id;
            DELETE FROM "ProxyYARP_Nodes" WHERE "GroupId" = @Id;
            DELETE FROM "ProxyYARP_ConfigGroups" WHERE "Id" = @Id;
            """, new { Id = groupId }));
    }
}

