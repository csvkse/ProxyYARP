using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Proxy.Yarp;

/// <summary>
/// YARP 动态配置提供者
/// 从 SQLite 读取路由/集群配置，并通过 IChangeToken 触发热重载
/// </summary>
public class DatabaseProxyConfigProvider : IProxyConfigProvider
{
    private readonly ProxyConfigService _configService;
    private readonly ILogger<DatabaseProxyConfigProvider> _logger;

    private volatile DatabaseProxyConfig _currentConfig;
    private volatile CancellationTokenSource _cts;

    public DatabaseProxyConfigProvider(
        ProxyConfigService configService,
        ILogger<DatabaseProxyConfigProvider> logger)
    {
        _configService = configService;
        _logger = logger;
        _cts = new CancellationTokenSource();
        _currentConfig = BuildConfig();

        // 订阅配置变更事件
        _configService.OnConfigChanged += Reload;
    }

    public IProxyConfig GetConfig() => _currentConfig;

    /// <summary>从 DB 重新构建配置并触发 YARP 热重载</summary>
    public void Reload()
    {
        try
        {
            _logger.LogInformation("[YARP] Configuration reload triggered from database");
            // 1. 创建新的 CancellationTokenSource 并替换旧的
            var oldCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
            // 2. 使用新 CancellationTokenSource 的 Token 构建新配置
            _currentConfig = BuildConfig();
            // 3. Cancel 旧 token → 触发 YARP 热重载
            oldCts.Cancel();
            oldCts.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[YARP] Failed to reload configuration from database");
        }
    }

    private DatabaseProxyConfig BuildConfig()
    {
        var routes = BuildRoutes();
        var clusters = BuildClusters();
        return new DatabaseProxyConfig(routes, clusters, _cts.Token);
    }

    private List<RouteConfig> BuildRoutes()
    {
        var entities = _configService.GetEnabledRoutes();
        var result = new List<RouteConfig>(entities.Count);

        foreach (var e in entities)
        {
            // 解析 Methods
            string[]? methods = null;
            if (!string.IsNullOrWhiteSpace(e.Methods))
                methods = e.Methods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // 解析 Hosts
            string[]? hosts = null;
            if (!string.IsNullOrWhiteSpace(e.Hosts))
                hosts = e.Hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            IReadOnlyList<IReadOnlyDictionary<string, string>>? transforms = null;
            if (!string.IsNullOrWhiteSpace(e.Metadata))
            {
                try
                {
                    transforms = System.Text.Json.JsonSerializer.Deserialize(
                        e.Metadata, 
                        ProxyYARP.Serialization.AppJsonContext.Default.ListDictionaryStringString)
                        ?.Cast<IReadOnlyDictionary<string, string>>()
                        ?.ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[YARP] Failed to parse Metadata as transforms for route {RouteId}", e.RouteId);
                }
            }

            result.Add(new RouteConfig
            {
                RouteId = e.RouteId,
                ClusterId = e.ClusterId,
                Order = e.Order,
                Match = new RouteMatch
                {
                    Path = e.Path,
                    Methods = methods,
                    Hosts = hosts
                },
                Transforms = transforms
            });
        }

        return result;
    }

    private List<ClusterConfig> BuildClusters()
    {
        var entities = _configService.GetEnabledClusters();
        var result = new List<ClusterConfig>(entities.Count);

        foreach (var c in entities)
        {
            var destEntities = _configService.GetEnabledDestinationsByCluster(c.ClusterId);
            var destinations = new Dictionary<string, DestinationConfig>(destEntities.Count);

            foreach (var d in destEntities)
            {
                destinations[d.DestId] = new DestinationConfig
                {
                    Address = d.Address,
                    Health = d.Health
                };
            }

            result.Add(new ClusterConfig
            {
                ClusterId = c.ClusterId,
                LoadBalancingPolicy = c.LoadBalancing,
                Destinations = destinations
            });
        }

        return result;
    }
}

/// <summary>YARP IProxyConfig 实现：持有当前路由/集群快照和 ChangeToken</summary>
internal sealed class DatabaseProxyConfig : IProxyConfig
{
    private readonly CancellationToken _ct;

    public DatabaseProxyConfig(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        CancellationToken ct)
    {
        Routes = routes;
        Clusters = clusters;
        _ct = ct;
        ChangeToken = new CancellationChangeToken(ct);
    }

    public IReadOnlyList<RouteConfig> Routes { get; }
    public IReadOnlyList<ClusterConfig> Clusters { get; }
    public IChangeToken ChangeToken { get; }
}
