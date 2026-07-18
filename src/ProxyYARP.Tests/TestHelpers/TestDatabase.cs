using Microsoft.Extensions.Configuration;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Tests.TestHelpers;

/// <summary>
/// 为每个测试提供独立的临时 SQLite 数据库（测试结束后自动清理）
/// Repository 通过注入的 SqliteDbProvider 创建连接
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public string DbPath { get; }
    public SqliteDbProvider Provider { get; }

    // 仓储实例
    public ApiKeyRepository      KeyRepo     { get; }
    public RouteRepository       RouteRepo   { get; }
    public ClusterRepository     ClusterRepo { get; }
    public DestinationRepository DestRepo    { get; }
    public ProxyConfigGroupRepository GroupRepo { get; }

    // 服务实例
    public ApiKeyService      KeyService    { get; }
    public ProxyConfigService ConfigService { get; }
    public DbInitService      InitService   { get; }

    public TestDatabase()
    {
        // 每个测试使用独立的临时文件
        DbPath = Path.Combine(Path.GetTempPath(), $"proxyyarp_test_{Guid.NewGuid():N}.db");
        Provider = new SqliteDbProvider($"Data Source={DbPath};Cache=Shared;");

        // 执行迁移建表
        MigrationRunner.Migrate(Provider);

        KeyRepo     = new ApiKeyRepository(Provider);
        RouteRepo   = new RouteRepository(Provider);
        ClusterRepo = new ClusterRepository(Provider);
        DestRepo    = new DestinationRepository(Provider);
        GroupRepo   = new ProxyConfigGroupRepository(Provider);
        var l4RouteRepo = new L4RouteRepository(Provider);
        var l4DestRepo  = new L4DestinationRepository(Provider);

        KeyService    = new ApiKeyService(KeyRepo);
        ConfigService = new ProxyConfigService(Provider, RouteRepo, ClusterRepo, DestRepo);
        InitService   = new DbInitService(KeyRepo, RouteRepo, ClusterRepo, DestRepo, l4RouteRepo, l4DestRepo, new ProxyYARP.Cluster.NodeIdentityManager(new ConfigurationBuilder().Build(), new Microsoft.Extensions.Logging.Abstractions.NullLogger<ProxyYARP.Cluster.NodeIdentityManager>()));
    }

    /// <summary>获取一个新打开的 SQLite 连接（测试用于原生 SQL 验证）</summary>
    public Microsoft.Data.Sqlite.SqliteConnection GetConnection()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath};");
        conn.Open();
        return conn;
    }

    public void Dispose()
    {
        // 短暂等待，给 SQLite 时间关闭文件句柄
        Thread.Sleep(100);
        try { if (File.Exists(DbPath)) File.Delete(DbPath); } catch { /* 忽略清理错误 */ }
    }
}
