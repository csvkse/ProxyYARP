using Dapper;
using System.Data;

namespace ProxyYARP.Data.Db;

/// <summary>版本化 schema 迁移执行器（AOT 安全，零第三方依赖，幂等）</summary>
[DapperAot]
public partial class MigrationRunner
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
                
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO "ProxyYARP_SchemaMigrations" ("Version", "Name", "AppliedAt")
                        VALUES (@Version, @Name, @AppliedAt)
                        """;
                    
                    var pVersion = cmd.CreateParameter();
                    pVersion.ParameterName = "Version";
                    pVersion.Value = migration.Version;
                    cmd.Parameters.Add(pVersion);

                    var pName = cmd.CreateParameter();
                    pName.ParameterName = "Name";
                    pName.Value = migration.Name;
                    cmd.Parameters.Add(pName);

                    var pAppliedAt = cmd.CreateParameter();
                    pAppliedAt.ParameterName = "AppliedAt";
                    pAppliedAt.Value = DateTime.UtcNow.ToString("o");
                    cmd.Parameters.Add(pAppliedAt);

                    cmd.ExecuteNonQuery();
                }
                
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
