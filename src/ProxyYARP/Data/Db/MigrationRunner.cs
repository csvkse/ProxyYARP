using Dapper;

namespace ProxyYARP.Data.Db;

/// <summary>版本化 schema 迁移执行器（AOT 安全，零第三方依赖，幂等）</summary>
public static class MigrationRunner
{
    /// <summary>执行所有未应用的迁移</summary>
    public static void Migrate(IDbProvider provider)
    {
        using var conn = provider.CreateConnection();
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS "__SchemaMigrations" (
                "Version"   INTEGER PRIMARY KEY,
                "Name"      TEXT NOT NULL,
                "AppliedAt" TEXT NOT NULL
            );
            """);

        var applied = conn.Query<int>("""SELECT "Version" FROM "__SchemaMigrations" """).ToHashSet();

        foreach (var migration in provider.Migrations.OrderBy(m => m.Version))
        {
            if (applied.Contains(migration.Version)) continue;

            conn.Execute(migration.Sql);
            conn.Execute("""
                INSERT INTO "__SchemaMigrations" ("Version", "Name", "AppliedAt")
                VALUES (@Version, @Name, @AppliedAt)
                """,
                new { migration.Version, migration.Name, AppliedAt = DateTime.UtcNow.ToString("o") });

            Console.WriteLine($"[DB] Applied migration {migration.Version}: {migration.Name} ({provider.Name})");
        }
    }
}
