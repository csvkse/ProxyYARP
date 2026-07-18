using Dapper;
using FluentAssertions;
using ProxyYARP.Data.Db;

namespace ProxyYARP.Tests.Unit;

/// <summary>MigrationRunner 单元测试（SQLite 临时库）</summary>
public class MigrationRunnerTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"proxyyarp_mig_{Guid.NewGuid():N}.db");

    private SqliteDbProvider NewProvider() => new($"Data Source={_dbPath};");

    [Fact]
    public void Migrate_On_Empty_Db_Should_Create_All_Tables()
    {
        var provider = NewProvider();
        MigrationRunner.Migrate(provider);

        using var conn = provider.CreateConnection();
        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").AsList();
        tables.Should().Contain([
            "ProxyYARP_ApiKeys", "ProxyYARP_Routes", "ProxyYARP_Clusters",
            "ProxyYARP_Destinations", "ProxyYARP_L4Routes", "ProxyYARP_L4Destinations",
            "ProxyYARP_SchemaMigrations"
        ]);
    }

    [Fact]
    public void Migrate_Twice_Should_Be_Idempotent()
    {
        var provider = NewProvider();
        MigrationRunner.Migrate(provider);
        var act = () => MigrationRunner.Migrate(provider);
        act.Should().NotThrow();

        using var conn = provider.CreateConnection();
        conn.ExecuteScalar<int>("""SELECT COUNT(*) FROM "ProxyYARP_SchemaMigrations" """).Should().Be(1);
    }

    [Fact]
    public void Migrate_Should_Record_Migration_Name()
    {
        var provider = NewProvider();
        MigrationRunner.Migrate(provider);

        using var conn = provider.CreateConnection();
        conn.QueryFirst<string>("""SELECT "Name" FROM "ProxyYARP_SchemaMigrations" WHERE "Version" = 1""")
            .Should().Be("InitialSchema");
    }

    public void Dispose()
    {
        Thread.Sleep(100);
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* 忽略清理错误 */ }
    }
}
