using Dapper;

namespace ProxyYARP.Data.Db;

/// <summary>版本化 schema 迁移执行器（AOT 安全，零第三方依赖，幂等）</summary>
public static class MigrationRunner
{
    public static void Migrate(IDbProvider provider)
    {
        using var conn = provider.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        if (provider.Name == "sqlite")
        {
            conn.Execute("PRAGMA journal_mode=WAL;");
        }

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS "ProxyYARP_SchemaMigrations" (
                "Version"   INTEGER PRIMARY KEY,
                "Name"      TEXT NOT NULL,
                "AppliedAt" TEXT NOT NULL
            );
            """);

        var applied = conn.Query<int>("""SELECT "Version" FROM "ProxyYARP_SchemaMigrations" """).ToHashSet();

        foreach (var migration in provider.Migrations.OrderBy(m => m.Version))
        {
            if (applied.Contains(migration.Version)) continue;

            using var tx = conn.BeginTransaction();
            try
            {
                conn.Execute(migration.Sql, transaction: tx);
                conn.Execute("""
                    INSERT INTO "ProxyYARP_SchemaMigrations" ("Version", "Name", "AppliedAt")
                    VALUES (@Version, @Name, @AppliedAt)
                    """,
                    new { migration.Version, migration.Name, AppliedAt = DateTime.UtcNow.ToString("o") }, transaction: tx);
                
                tx.Commit();
                Console.WriteLine($"[DB] Applied migration {migration.Version}: {migration.Name} ({provider.Name})");
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
