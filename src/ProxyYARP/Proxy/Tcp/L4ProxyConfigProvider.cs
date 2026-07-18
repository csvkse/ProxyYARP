using Microsoft.Extensions.Logging;
using ProxyYARP.Data.Services;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Cluster;

namespace ProxyYARP.Proxy.Tcp;

public class L4ProxyDestination
{
    public string TargetHost { get; set; } = "";
    public int TargetPort { get; set; }
    public int Weight { get; set; } = 1;
}

public class L4ProxyRoute
{
    public int ListenPort { get; set; }
    public string Protocol { get; set; } = "TCP";
    public int IdleTimeoutSeconds { get; set; } = 60;
    public string LoadBalancingPolicy { get; set; } = "RoundRobin";
    public IReadOnlyList<L4ProxyDestination> Destinations { get; set; } = Array.Empty<L4ProxyDestination>();
    public IL4LoadBalancerPolicy? Policy { get; set; }
}

/// <summary>
/// L4 代理配置提供者
/// 负责加载 L4 路由规则，对接 SQLite 数据库
/// </summary>
public class L4ProxyConfigProvider
{
    private readonly L4ConfigService _configService;
    private readonly ProxyConfigGroupRepository _groupRepo;
    private readonly NodeIdentityManager _identityManager;
    private readonly ILogger<L4ProxyConfigProvider> _logger;

    private readonly Timer _timer;
    private int _lastVersion = -1;

    public event Action? OnConfigChanged;

    public L4ProxyConfigProvider(
        L4ConfigService configService, 
        ProxyConfigGroupRepository groupRepo,
        NodeIdentityManager identityManager,
        ILogger<L4ProxyConfigProvider> logger)
    {
        _configService = configService;
        _groupRepo = groupRepo;
        _identityManager = identityManager;
        _logger = logger;
        
        _timer = new Timer(CheckForUpdates, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    private void CheckForUpdates(object? state)
    {
        try
        {
            var currentVersion = _groupRepo.GetVersion(_identityManager.GroupId);
            if (currentVersion != _lastVersion)
            {
                _lastVersion = currentVersion;
                OnConfigChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[L4 Proxy] Failed to check for configuration updates.");
        }
    }

    public IReadOnlyList<L4ProxyRoute> GetRoutes()
    {
        var dtos = _configService.GetEnabledRoutesWithDestinations(_identityManager.GroupId);
        return dtos.Select(d => new L4ProxyRoute
        {
            ListenPort = d.Route.ListenPort,
            Protocol = d.Route.Protocol ?? "TCP",
            IdleTimeoutSeconds = d.Route.IdleTimeoutSeconds > 0 ? d.Route.IdleTimeoutSeconds : 60,
            LoadBalancingPolicy = d.Route.LoadBalancingPolicy,
            Destinations = d.Destinations.Select(dest => new L4ProxyDestination
            {
                TargetHost = dest.TargetHost,
                TargetPort = dest.TargetPort,
                Weight = dest.Weight
            }).ToList(),
            Policy = L4LoadBalancerPolicyFactory.GetPolicy(d.Route.LoadBalancingPolicy)
        }).ToList();
    }
}