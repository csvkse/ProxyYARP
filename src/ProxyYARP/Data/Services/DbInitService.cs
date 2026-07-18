using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Data.Services;

/// <summary>数据库初始化服务：建表 + 种子数据</summary>
public class DbInitService
{
    private readonly ApiKeyRepository _keyRepo;
    private readonly RouteRepository _routeRepo;
    private readonly ClusterRepository _clusterRepo;
    private readonly DestinationRepository _destRepo;
    private readonly L4RouteRepository _l4RouteRepo;
    private readonly L4DestinationRepository _l4DestRepo;
    private readonly ProxyConfigGroupRepository _groupRepo;
    private readonly ProxyYARP.Cluster.NodeIdentityManager _identityManager;

    public DbInitService(
        ApiKeyRepository keyRepo,
        RouteRepository routeRepo,
        ClusterRepository clusterRepo,
        DestinationRepository destRepo,
        L4RouteRepository l4RouteRepo,
        L4DestinationRepository l4DestRepo,
        ProxyConfigGroupRepository groupRepo,
        ProxyYARP.Cluster.NodeIdentityManager identityManager)
    {
        _keyRepo = keyRepo;
        _routeRepo = routeRepo;
        _clusterRepo = clusterRepo;
        _destRepo = destRepo;
        _l4RouteRepo = l4RouteRepo;
        _l4DestRepo = l4DestRepo;
        _groupRepo = groupRepo;
        _identityManager = identityManager;
    }

    /// <summary>如果没有 Key，注入初始管理员 Key（种子数据）。返回是否实际写入。</summary>
    public bool SeedAdminKey(string adminKey)
    {
        if (_keyRepo.Exists()) return false;

        _keyRepo.Insert(new ApiKeyEntity
        {
            Id = Guid.NewGuid().ToString(),
            KeyValue = adminKey,
            Name = "Default Admin",
            Role = "Admin",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = null
        });

        Console.WriteLine("[DB] Seeded default admin key.");
        return true;
    }

    /// <summary>注入示例路由和集群（仅当 DB 全空时）</summary>
    public void SeedDemoData()
    {
        var groupId = _identityManager.GroupId;
        
        // Ensure group exists before seeding data to avoid Foreign Key constraint failures
        _groupRepo.Upsert(groupId);

        var routes = _routeRepo.GetAll(groupId);
        if (routes.Count > 0) return;

        var now = DateTime.UtcNow;
        var clusterId = "demo-cluster";

        if (!_clusterRepo.GetAll(groupId).Any(c => c.ClusterId == clusterId))
        {
            _clusterRepo.Insert(new ProxyClusterEntity
            {
                Id = Guid.NewGuid().ToString(),
                GroupId = groupId,
                ClusterId = clusterId,
                LoadBalancing = "RoundRobin",
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!_destRepo.GetAll(groupId).Any(d => d.ClusterId == clusterId))
        {
            _destRepo.Insert(new ProxyDestinationEntity
            {
                Id = Guid.NewGuid().ToString(),
                GroupId = groupId,
                ClusterId = clusterId,
                DestId = "dest-1",
                Address = "https://httpbin.org",
                IsEnabled = true,
                CreatedAt = now
            });
        }

        _routeRepo.Insert(new ProxyRouteEntity
        {
            Id = Guid.NewGuid().ToString(),
            GroupId = groupId,
            RouteId = "demo-route",
            ClusterId = clusterId,
            Path = "/demo/{**catch-all}",
            Methods = null,
            Order = 0,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });

        Console.WriteLine($"[DB] Seeded demo route -> cluster -> destination (Group: {groupId})");
    }
}
