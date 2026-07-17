using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Data.Services;

/// <summary>代理配置业务服务层（路由 + 集群 + 目标节点）</summary>
public class ProxyConfigService
{
    private readonly RouteRepository _routeRepo;
    private readonly ClusterRepository _clusterRepo;
    private readonly DestinationRepository _destRepo;

    // 当配置变更时触发（由 DatabaseProxyConfigProvider 订阅）
    public event Action? OnConfigChanged;

    public ProxyConfigService(
        RouteRepository routeRepo,
        ClusterRepository clusterRepo,
        DestinationRepository destRepo)
    {
        _routeRepo = routeRepo;
        _clusterRepo = clusterRepo;
        _destRepo = destRepo;
    }

    // ───────── Routes ─────────

    public List<ProxyRouteEntity> GetAllRoutes() => _routeRepo.GetAll();
    public List<ProxyRouteEntity> GetEnabledRoutes() => _routeRepo.GetAllEnabled();
    public ProxyRouteEntity? GetRouteById(string id) => _routeRepo.GetById(id);

    public ProxyRouteEntity CreateRoute(string routeId, string clusterId, string path,
        string? methods, string? hosts, int order, string? metadata)
    {
        var now = DateTime.UtcNow.ToString("o");
        var entity = new ProxyRouteEntity
        {
            Id = Guid.NewGuid().ToString(),
            RouteId = routeId,
            ClusterId = clusterId,
            Path = path,
            Methods = methods,
            Hosts = hosts,
            Order = order,
            IsEnabled = 1,
            Metadata = metadata,
            CreatedAt = now,
            UpdatedAt = now
        };
        _routeRepo.Insert(entity);
        NotifyChanged();
        return entity;
    }

    public bool UpdateRoute(string id, string routeId, string clusterId, string path,
        string? methods, string? hosts, int order, bool isEnabled, string? metadata)
    {
        var entity = _routeRepo.GetById(id);
        if (entity == null) return false;
        entity.RouteId = string.IsNullOrWhiteSpace(routeId) ? entity.RouteId : routeId;
        entity.ClusterId = string.IsNullOrWhiteSpace(clusterId) ? entity.ClusterId : clusterId;
        entity.Path = string.IsNullOrWhiteSpace(path) ? entity.Path : path;
        entity.Methods = methods;
        entity.Hosts = hosts;
        entity.Order = order;
        entity.IsEnabled = isEnabled ? 1 : 0;
        entity.Metadata = metadata;
        entity.UpdatedAt = DateTime.UtcNow.ToString("o");
        _routeRepo.Update(entity);
        NotifyChanged();
        return true;
    }

    public bool DeleteRoute(string id)
    {
        var entity = _routeRepo.GetById(id);
        if (entity == null) return false;
        _routeRepo.Delete(id);
        NotifyChanged();
        return true;
    }

    // ───────── Clusters ─────────

    public List<ProxyClusterEntity> GetAllClusters() => _clusterRepo.GetAll();
    public List<ProxyClusterEntity> GetEnabledClusters() => _clusterRepo.GetAllEnabled();
    public ProxyClusterEntity? GetClusterById(string id) => _clusterRepo.GetById(id);

    public ProxyClusterEntity CreateCluster(string clusterId, string loadBalancing, string? healthCheckEnabled)
    {
        var now = DateTime.UtcNow.ToString("o");
        var entity = new ProxyClusterEntity
        {
            Id = Guid.NewGuid().ToString(),
            ClusterId = clusterId,
            LoadBalancing = loadBalancing,
            HealthCheckEnabled = healthCheckEnabled,
            IsEnabled = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        _clusterRepo.Insert(entity);
        NotifyChanged();
        return entity;
    }

    public bool UpdateCluster(string id, string clusterId, string loadBalancing, string? healthCheckEnabled, bool isEnabled)
    {
        var entity = _clusterRepo.GetById(id);
        if (entity == null) return false;
        entity.ClusterId = string.IsNullOrWhiteSpace(clusterId) ? entity.ClusterId : clusterId;
        entity.LoadBalancing = string.IsNullOrWhiteSpace(loadBalancing) ? entity.LoadBalancing : loadBalancing;
        entity.HealthCheckEnabled = healthCheckEnabled;
        entity.IsEnabled = isEnabled ? 1 : 0;
        entity.UpdatedAt = DateTime.UtcNow.ToString("o");
        _clusterRepo.Update(entity);
        NotifyChanged();
        return true;
    }

    public bool DeleteCluster(string id)
    {
        var entity = _clusterRepo.GetById(id);
        if (entity == null) return false;
        _destRepo.DeleteByClusterId(entity.ClusterId);
        _clusterRepo.Delete(id);
        NotifyChanged();
        return true;
    }

    // ───────── Destinations ─────────

    public List<ProxyDestinationEntity> GetDestinationsByCluster(string clusterId)
        => _destRepo.GetAllByClusterId(clusterId);

    public List<ProxyDestinationEntity> GetEnabledDestinationsByCluster(string clusterId)
        => _destRepo.GetByClusterId(clusterId);

    public List<ProxyDestinationEntity> GetAllDestinations()
        => _destRepo.GetAll();

    public ProxyDestinationEntity? GetDestinationById(string id) => _destRepo.GetById(id);

    public ProxyDestinationEntity CreateDestination(string clusterId, string destId,
        string address, string? health, string? metadata)
    {
        var entity = new ProxyDestinationEntity
        {
            Id = Guid.NewGuid().ToString(),
            ClusterId = clusterId,
            DestId = destId,
            Address = address,
            Health = health,
            Metadata = metadata,
            IsEnabled = 1,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        _destRepo.Insert(entity);
        NotifyChanged();
        return entity;
    }

    public bool UpdateDestination(string id, string destId, string address,
        string? health, string? metadata, bool isEnabled)
    {
        var entity = _destRepo.GetById(id);
        if (entity == null) return false;
        entity.DestId = destId;
        entity.Address = address;
        entity.Health = health;
        entity.Metadata = metadata;
        entity.IsEnabled = isEnabled ? 1 : 0;
        _destRepo.Update(entity);
        NotifyChanged();
        return true;
    }

    public bool DeleteDestination(string id)
    {
        var entity = _destRepo.GetById(id);
        if (entity == null) return false;
        _destRepo.Delete(id);
        NotifyChanged();
        return true;
    }

    // ───────── Internal ─────────

    private void NotifyChanged() => OnConfigChanged?.Invoke();
}
