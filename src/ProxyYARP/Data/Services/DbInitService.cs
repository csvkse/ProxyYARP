using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Data.Services;

/// <summary>数据库初始化服务：建�?+ 种子数据</summary>
public class DbInitService
{
    private readonly ApiKeyRepository _keyRepo;
    private readonly RouteRepository _routeRepo;
    private readonly ClusterRepository _clusterRepo;
    private readonly DestinationRepository _destRepo;
    private readonly L4RouteRepository _tcpRouteRepo;
    private readonly L4DestinationRepository _tcpDestRepo;

    public DbInitService(
        ApiKeyRepository keyRepo,
        RouteRepository routeRepo,
        ClusterRepository clusterRepo,
        DestinationRepository destRepo,
        L4RouteRepository tcpRouteRepo,
        L4DestinationRepository tcpDestRepo)
    {
        _keyRepo = keyRepo;
        _routeRepo = routeRepo;
        _clusterRepo = clusterRepo;
        _destRepo = destRepo;
        _tcpRouteRepo = tcpRouteRepo;
        _tcpDestRepo = tcpDestRepo;
    }

    /// <summary>初始化所有表结构</summary>
    public void InitTables()
    {
        _keyRepo.CreateTable();
        _routeRepo.CreateTable();
        _clusterRepo.CreateTable();
        _destRepo.CreateTable();
        _tcpRouteRepo.CreateTable();
        _tcpDestRepo.CreateTable();
    }

    /// <summary>如果没有 Key，注入初始管理员 Key（种子数据）</summary>
    public void SeedAdminKey(string adminKey)
    {
        if (_keyRepo.Exists()) return;

        _keyRepo.Insert(new ApiKeyEntity
        {
            Id = Guid.NewGuid().ToString(),
            KeyValue = adminKey,
            Name = "Default Admin",
            Role = "Admin",
            IsEnabled = 1,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            LastUsedAt = null
        });

        Console.WriteLine($"[DB] Seeded admin key: {adminKey}");
    }

    /// <summary>注入示例路由和集群（仅当 DB 全空时）</summary>
    public void SeedDemoData()
    {
        var routes = _routeRepo.GetAll();
        if (routes.Count > 0) return;

        var now = DateTime.UtcNow.ToString("o");
        var clusterId = "demo-cluster";

        _clusterRepo.Insert(new ProxyClusterEntity
        {
            Id = Guid.NewGuid().ToString(),
            ClusterId = clusterId,
            LoadBalancing = "RoundRobin",
            IsEnabled = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

        _destRepo.Insert(new ProxyDestinationEntity
        {
            Id = Guid.NewGuid().ToString(),
            ClusterId = clusterId,
            DestId = "dest-1",
            Address = "https://httpbin.org",
            IsEnabled = 1,
            CreatedAt = now
        });

        _routeRepo.Insert(new ProxyRouteEntity
        {
            Id = Guid.NewGuid().ToString(),
            RouteId = "demo-route",
            ClusterId = clusterId,
            Path = "/demo/{**catch-all}",
            Methods = null,
            Order = 0,
            IsEnabled = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

        Console.WriteLine("[DB] Seeded demo route -> cluster -> destination");
    }
}
