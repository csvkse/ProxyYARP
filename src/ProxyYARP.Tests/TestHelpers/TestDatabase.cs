using ProxyYARP.Data.Db;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Tests.TestHelpers;

/// <summary>
/// 为每个测试提供独立的临时 SQLite 数据库（测试结束后自动清理）
/// BaseRepository 通过 DbContext.Configure 的全局静态连接字符串创建连接
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public string DbPath { get; }

    // 仓储实例
    public ApiKeyRepository      KeyRepo     { get; }
    public RouteRepository       RouteRepo   { get; }
    public ClusterRepository     ClusterRepo { get; }
    public DestinationRepository DestRepo    { get; }

    // 服务实例
    public ApiKeyService      KeyService    { get; }
    public ProxyConfigService ConfigService { get; }
    public DbInitService      InitService   { get; }

    public TestDatabase()
    {
        // 每个测试使用独立的临时文件
        DbPath = Path.Combine(Path.GetTempPath(), $"proxyyarp_test_{Guid.NewGuid():N}.db");

        // 显式创建独立的 SQLite 连接，确保单元测试之间的完全隔离，不受静态 DbContext 影响
        var connectionString = $"Data Source={DbPath};";
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);

        // 初始化仓储实例时，注入独立连接
        KeyRepo     = new ApiKeyRepository(connection);
        RouteRepo   = new RouteRepository(connection);
        ClusterRepo = new ClusterRepository(connection);
        DestRepo    = new DestinationRepository(connection);
        var tcpRouteRepo = new L4RouteRepository(connection);
        var tcpDestRepo  = new L4DestinationRepository(connection);

        KeyService    = new ApiKeyService(KeyRepo);
        ConfigService = new ProxyConfigService(RouteRepo, ClusterRepo, DestRepo);
        InitService   = new DbInitService(KeyRepo, RouteRepo, ClusterRepo, DestRepo, tcpRouteRepo, tcpDestRepo);

        // 初始化表结构
        InitService.InitTables();
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