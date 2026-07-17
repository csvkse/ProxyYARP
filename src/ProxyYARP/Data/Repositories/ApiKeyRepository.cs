using Dapper;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class ApiKeyRepository : BaseRepository<ApiKeyEntity>
{
    public ApiKeyRepository(System.Data.IDbConnection? connection = null) : base(connection) { }

    public void CreateTable()
    {
        WithConnection(c => c.Execute(@"
            CREATE TABLE IF NOT EXISTS ApiKeys (
                Id          TEXT PRIMARY KEY,
                KeyValue    TEXT NOT NULL UNIQUE,
                Name        TEXT NOT NULL,
                Role        TEXT NOT NULL DEFAULT 'ReadOnly',
                IsEnabled   INTEGER NOT NULL DEFAULT 1,
                CreatedAt   TEXT NOT NULL,
                LastUsedAt  TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_apikeys_keyvalue ON ApiKeys(KeyValue);
        "));
    }

    public ApiKeyEntity? GetByKeyValue(string keyValue)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ApiKeyEntity>(
            "SELECT * FROM ApiKeys WHERE KeyValue = @KeyValue AND IsEnabled = 1",
            new { KeyValue = keyValue }));
    }

    public List<ApiKeyEntity> GetAll()
    {
        return WithConnection(c => c.Query<ApiKeyEntity>("SELECT * FROM ApiKeys ORDER BY CreatedAt DESC")
                                    .AsList());
    }

    public ApiKeyEntity? GetById(string id)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ApiKeyEntity>(
            "SELECT * FROM ApiKeys WHERE Id = @Id", new { Id = id }));
    }

    public void Insert(ApiKeyEntity entity)
    {
        WithConnection(c => c.Execute(@"
            INSERT INTO ApiKeys (Id, KeyValue, Name, Role, IsEnabled, CreatedAt)
            VALUES (@Id, @KeyValue, @Name, @Role, @IsEnabled, @CreatedAt)",
            entity));
    }

    public void Update(ApiKeyEntity entity)
    {
        WithConnection(c => c.Execute(@"
            UPDATE ApiKeys
            SET Name = @Name, Role = @Role, IsEnabled = @IsEnabled
            WHERE Id = @Id",
            entity));
    }

    public void UpdateLastUsed(string keyValue)
    {
        WithConnection(c => c.Execute(@"
            UPDATE ApiKeys SET LastUsedAt = @Now WHERE KeyValue = @KeyValue",
            new { Now = DateTime.UtcNow.ToString("o"), KeyValue = keyValue }));
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("DELETE FROM ApiKeys WHERE Id = @Id", new { Id = id }));
    }

    public bool Exists()
    {
        return WithConnection(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM ApiKeys") > 0);
    }
}
