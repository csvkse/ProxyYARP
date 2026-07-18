using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class ApiKeyRepository : BaseRepository<ApiKeyEntity>
{
    public ApiKeyRepository(IDbProvider provider) : base(provider) { }

    public ApiKeyEntity? GetByKeyValue(string keyValue)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ApiKeyEntity>(
            """SELECT * FROM "ApiKeys" WHERE "KeyValue" = @KeyValue AND "IsEnabled" = TRUE""",
            new { KeyValue = keyValue }));
    }

    public List<ApiKeyEntity> GetAll()
    {
        return WithConnection(c => c.Query<ApiKeyEntity>(
            """SELECT * FROM "ApiKeys" ORDER BY "CreatedAt" DESC""")
            .AsList());
    }

    public ApiKeyEntity? GetById(string id)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ApiKeyEntity>(
            """SELECT * FROM "ApiKeys" WHERE "Id" = @Id""", new { Id = id }));
    }

    public void Insert(ApiKeyEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ApiKeys" ("Id", "KeyValue", "Name", "Role", "IsEnabled", "CreatedAt")
            VALUES (@Id, @KeyValue, @Name, @Role, @IsEnabled, @CreatedAt)
            """,
            entity));
    }

    public void Update(ApiKeyEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ApiKeys"
            SET "Name" = @Name, "Role" = @Role, "IsEnabled" = @IsEnabled
            WHERE "Id" = @Id
            """,
            entity));
    }

    public void UpdateLastUsed(string keyValue)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ApiKeys" SET "LastUsedAt" = @Now WHERE "KeyValue" = @KeyValue
            """,
            new { Now = DateTime.UtcNow, KeyValue = keyValue }));
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ApiKeys" WHERE "Id" = @Id""", new { Id = id }));
    }

    public bool Exists()
    {
        return WithConnection(c => c.ExecuteScalar<int>("""SELECT COUNT(*) FROM "ApiKeys" """) > 0);
    }
}
