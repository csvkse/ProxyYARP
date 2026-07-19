using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public partial class ProxyNodeRepository : BaseRepository<ProxyNodeEntity>
{
    public ProxyNodeRepository(IDbProvider provider) : base(provider) { }

    public void UpsertHeartbeat(string id, string groupId, string name, string? managementUrl, bool isManagementEnabled, bool isNameExplicit, bool isUrlExplicit)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyYARP_Nodes" ("Id", "GroupId", "Name", "ManagementUrl", "IsManagementEnabled", "LastHeartbeat")
            VALUES (@Id, @GroupId, @Name, @ManagementUrl, @IsManagementEnabled, @LastHeartbeat)
            ON CONFLICT("Id") DO UPDATE SET
                "GroupId" = @GroupId,
                "Name" = CASE WHEN @IsNameExplicit THEN @Name ELSE "ProxyYARP_Nodes"."Name" END,
                "ManagementUrl" = CASE WHEN @IsUrlExplicit THEN @ManagementUrl ELSE "ProxyYARP_Nodes"."ManagementUrl" END,
                "IsManagementEnabled" = @IsManagementEnabled,

                "LastHeartbeat" = @LastHeartbeat
            """,
            new 
            { 
                Id = id, 
                GroupId = groupId, 
                Name = name, 
                ManagementUrl = managementUrl, 
                IsManagementEnabled = isManagementEnabled, 
                IsNameExplicit = isNameExplicit,
                IsUrlExplicit = isUrlExplicit,
                LastHeartbeat = DateTime.UtcNow 

            }));
    }

    public List<ProxyNodeEntity> GetAll()
    {
        return WithConnection(c => c.Query<ProxyNodeEntity>("""
            SELECT * FROM "ProxyYARP_Nodes" ORDER BY "LastHeartbeat" DESC
            """).AsList());
    }

    public List<ProxyNodeEntity> GetByGroupId(string groupId)
    {
        return WithConnection(c => c.Query<ProxyNodeEntity>("""
            SELECT * FROM "ProxyYARP_Nodes" WHERE "GroupId" = @GroupId ORDER BY "LastHeartbeat" DESC
            """,
            new { GroupId = groupId }).AsList());
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyYARP_Nodes" WHERE "Id" = @Id""", new { Id = id }));
    }

    public void UpdateNameAndUrl(string id, string name, string? managementUrl)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyYARP_Nodes" 
            SET "Name" = @Name, "ManagementUrl" = @ManagementUrl 
            WHERE "Id" = @Id
            """, new { Id = id, Name = name, ManagementUrl = managementUrl }));
    }

    public string? GetTargetGroupId(string id)
    {
        return WithConnection(c => c.QuerySingleOrDefault<string?>("""
            SELECT "TargetGroupId" FROM "ProxyYARP_Nodes" WHERE "Id" = @Id
            """, new { Id = id }));
    }

    public void SetTargetGroupId(string id, string targetGroupId)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyYARP_Nodes" SET "TargetGroupId" = @TargetGroupId WHERE "Id" = @Id
            """, new { Id = id, TargetGroupId = targetGroupId }));
    }

    public void AcknowledgeGroupChange(string id, string newGroupId)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyYARP_Nodes" SET "GroupId" = @GroupId, "TargetGroupId" = NULL WHERE "Id" = @Id
            """, new { Id = id, GroupId = newGroupId }));
    }
}

