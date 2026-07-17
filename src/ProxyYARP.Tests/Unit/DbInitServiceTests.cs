using System.Data;
using FluentAssertions;
using ProxyYARP.Tests.TestHelpers;

namespace ProxyYARP.Tests.Unit;

/// <summary>
/// DbInitService 单元测试
/// 验证数据库初始化、建表、种子数据逻辑
/// </summary>
public class DbInitServiceTests : IDisposable
{
    private readonly TestDatabase _db;

    public DbInitServiceTests() => _db = new TestDatabase();
    public void Dispose() => _db.Dispose();

    // ── 建表 ────────────────────────────────────────────────────

    [Fact]
    public void InitTables_Should_Create_All_Tables()
    {
        // 构造函数已调用 InitTables，验证各表可查询
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        tables.Should().Contain("ApiKeys");
        tables.Should().Contain("ProxyRoutes");
        tables.Should().Contain("ProxyClusters");
        tables.Should().Contain("ProxyDestinations");
    }

    [Fact]
    public void InitTables_Is_Idempotent()
    {
        // 多次调用不应报错（使用 IF NOT EXISTS）
        var act = () => _db.InitService.InitTables();
        act.Should().NotThrow();
        act.Should().NotThrow();
    }

    [Fact]
    public void Tables_Should_Have_Correct_Columns()
    {
        using var conn = _db.GetConnection();

        // ApiKeys 表应有 7 列
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ApiKeys')";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        count.Should().Be(7, "ApiKeys 表有 Id,KeyValue,Name,Role,IsEnabled,CreatedAt,LastUsedAt 共 7 列");
    }

    // ── SeedAdminKey ────────────────────────────────────────────

    [Fact]
    public void SeedAdminKey_Should_Insert_Key_When_DB_Empty()
    {
        _db.InitService.SeedAdminKey("InitialAdminKey");

        var keys = _db.KeyRepo.GetAll();
        keys.Should().HaveCount(1);
        keys[0].KeyValue.Should().Be("InitialAdminKey");
        keys[0].Role.Should().Be("Admin");
        keys[0].IsEnabled.Should().Be(1);
    }

    [Fact]
    public void SeedAdminKey_Should_Skip_When_Keys_Exist()
    {
        // 先插入一个 Key
        _db.InitService.SeedAdminKey("FirstKey");
        // 再次调用不应再插入
        _db.InitService.SeedAdminKey("SecondKey");

        var keys = _db.KeyRepo.GetAll();
        keys.Should().HaveCount(1);
        keys[0].KeyValue.Should().Be("FirstKey");
    }

    [Fact]
    public void SeedAdminKey_Creates_Key_That_Validates_Successfully()
    {
        _db.InitService.SeedAdminKey("SeedTestKey");

        var validated = _db.KeyService.Validate("SeedTestKey");
        validated.Should().NotBeNull();
        validated!.IsAdmin.Should().BeTrue();
    }

    // ── SeedDemoData ────────────────────────────────────────────

    [Fact]
    public void SeedDemoData_Should_Insert_Route_Cluster_Destination()
    {
        _db.InitService.SeedDemoData();

        _db.ClusterRepo.GetAll().Should().HaveCount(1);
        _db.RouteRepo.GetAll().Should().HaveCount(1);
        _db.DestRepo.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void SeedDemoData_Should_Skip_When_Routes_Exist()
    {
        _db.InitService.SeedDemoData();
        _db.InitService.SeedDemoData(); // 第二次调用

        _db.RouteRepo.GetAll().Should().HaveCount(1); // 仍然只有1条
    }

    [Fact]
    public void SeedDemoData_Route_Should_Be_Enabled()
    {
        _db.InitService.SeedDemoData();
        var routes = _db.RouteRepo.GetAll();
        routes[0].IsEnabled.Should().Be(1);
    }

    [Fact]
    public void SeedDemoData_Destination_Should_Belong_To_Cluster()
    {
        _db.InitService.SeedDemoData();
        var cluster = _db.ClusterRepo.GetAll()[0];
        var dest    = _db.DestRepo.GetAll()[0];
        dest.ClusterId.Should().Be(cluster.ClusterId);
    }
}
