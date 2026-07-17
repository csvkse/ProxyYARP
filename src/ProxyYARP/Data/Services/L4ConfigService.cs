using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Data.Services;

public class TcpRouteDto
{
    public L4ProxyRouteEntity Route { get; set; } = null!;
    public List<L4ProxyDestinationEntity> Destinations { get; set; } = new();
}

/// <summary>TCP 配置业务服务�?/summary>
public class L4ConfigService
{
    private readonly L4RouteRepository _routeRepo;
    private readonly L4DestinationRepository _destRepo;

    public event Action? OnConfigChanged;

    public L4ConfigService(L4RouteRepository routeRepo, L4DestinationRepository destRepo)
    {
        _routeRepo = routeRepo;
        _destRepo = destRepo;
    }

    public List<TcpRouteDto> GetAllRoutesWithDestinations()
    {
        var routes = _routeRepo.GetAll();
        var allDests = _destRepo.GetAll().GroupBy(d => d.RouteId).ToDictionary(g => g.Key, g => g.ToList());
        
        return routes.Select(r => new TcpRouteDto
        {
            Route = r,
            Destinations = allDests.GetValueOrDefault(r.Id) ?? new List<L4ProxyDestinationEntity>()
        }).ToList();
    }
    
    public List<TcpRouteDto> GetEnabledRoutesWithDestinations()
    {
        var routes = _routeRepo.GetAllEnabled();
        var allDests = _destRepo.GetAll().Where(d => d.IsEnabled == 1).GroupBy(d => d.RouteId).ToDictionary(g => g.Key, g => g.ToList());
        
        return routes.Select(r => new TcpRouteDto
        {
            Route = r,
            Destinations = allDests.GetValueOrDefault(r.Id) ?? new List<L4ProxyDestinationEntity>()
        }).ToList();
    }
    
    public TcpRouteDto? GetRouteById(string id)
    {
        var route = _routeRepo.GetById(id);
        if (route == null) return null;
        return new TcpRouteDto
        {
            Route = route,
            Destinations = _destRepo.GetByRouteId(id)
        };
    }

    public L4ProxyRouteEntity CreateRoute(string routeId, int listenPort, string loadBalancingPolicy, List<L4ProxyDestinationEntity> destinations)
    {
        // 检查端口是否已被占�
        var existing = _routeRepo.GetByListenPort(listenPort);
        if (existing != null)
            throw new Exception($"Listen port {listenPort} is already in use by another TCP route.");

        var now = DateTime.UtcNow.ToString("o");
        var routeInternalId = Guid.NewGuid().ToString();
        var entity = new L4ProxyRouteEntity
        {
            Id = routeInternalId,
            RouteId = routeId,
            ListenPort = listenPort,
            LoadBalancingPolicy = loadBalancingPolicy,
            IsEnabled = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        _routeRepo.Insert(entity);

        foreach (var dest in destinations)
        {
            dest.Id = Guid.NewGuid().ToString();
            dest.RouteId = routeInternalId;
            dest.CreatedAt = now;
            dest.UpdatedAt = now;
            _destRepo.Insert(dest);
        }

        NotifyChanged();
        return entity;
    }

    public bool UpdateRoute(string id, string routeId, int listenPort, string loadBalancingPolicy, bool isEnabled, List<L4ProxyDestinationEntity> destinations)
    {
        var entity = _routeRepo.GetById(id);
        if (entity == null) return false;

        // 检查端口是否已被其他记录占�
        var existing = _routeRepo.GetByListenPort(listenPort);
        if (existing != null && existing.Id != id)
            throw new Exception($"Listen port {listenPort} is already in use by another TCP route.");

        var now = DateTime.UtcNow.ToString("o");
        entity.RouteId = string.IsNullOrWhiteSpace(routeId) ? entity.RouteId : routeId;
        entity.ListenPort = listenPort == 0 ? entity.ListenPort : listenPort;
        entity.LoadBalancingPolicy = string.IsNullOrWhiteSpace(loadBalancingPolicy) ? entity.LoadBalancingPolicy : loadBalancingPolicy;
        entity.IsEnabled = isEnabled ? 1 : 0;
        entity.UpdatedAt = now;
        _routeRepo.Update(entity);

        // 重建 Destinations
        _destRepo.DeleteByRouteId(id);
        foreach (var dest in destinations)
        {
            dest.Id = Guid.NewGuid().ToString();
            dest.RouteId = id;
            dest.CreatedAt = now;
            dest.UpdatedAt = now;
            _destRepo.Insert(dest);
        }

        NotifyChanged();
        return true;
    }

    public bool DeleteRoute(string id)
    {
        var entity = _routeRepo.GetById(id);
        if (entity == null) return false;
        _routeRepo.Delete(id);
        _destRepo.DeleteByRouteId(id);
        NotifyChanged();
        return true;
    }

    private void NotifyChanged() => OnConfigChanged?.Invoke();
}
