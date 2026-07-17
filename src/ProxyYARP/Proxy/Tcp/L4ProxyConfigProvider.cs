using Microsoft.Extensions.Logging;
using ProxyYARP.Data.Services;

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
}

/// <summary>
/// L4 代理配置提供者
/// 负责加载 L4 路由规则，对接 SQLite 数据库
/// </summary>
public class L4ProxyConfigProvider
{
    private readonly L4ConfigService _configService;
    private readonly ILogger<L4ProxyConfigProvider> _logger;

    public event Action? OnConfigChanged;

    public L4ProxyConfigProvider(L4ConfigService configService, ILogger<L4ProxyConfigProvider> logger)
    {
        _configService = configService;
        _logger = logger;
        
        // 当数据库配置变更时，触发内存更新
        _configService.OnConfigChanged += () => OnConfigChanged?.Invoke();
    }

    public IReadOnlyList<L4ProxyRoute> GetRoutes()
    {
        var dtos = _configService.GetEnabledRoutesWithDestinations();
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
            }).ToList()
        }).ToList();
    }
}