using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Cluster;

namespace ProxyYARP.Proxy.Yarp;

/// <summary>
/// YARP 动态配置提供者
/// 从 SQLite 读取路由/集群配置，并通过 IChangeToken 触发热重载
/// </summary>
public class DatabaseProxyConfigProvider : IProxyConfigProvider
{
    private readonly ProxyConfigService _configService;
    private readonly ProxyConfigGroupRepository _groupRepo;
    private readonly NodeIdentityManager _identityManager;
    private readonly WebsiteConfigService _websiteService;
    private readonly WebsiteAllowList _allowList;
    private readonly ILogger<DatabaseProxyConfigProvider> _logger;

    private volatile DatabaseProxyConfig _currentConfig;
    private volatile CancellationTokenSource _cts;
    private readonly Timer _timer;
    private int _lastVersion = -1;
    private bool _websiteRoutesInjected;

    public DatabaseProxyConfigProvider(
        ProxyConfigService configService,
        ProxyConfigGroupRepository groupRepo,
        NodeIdentityManager identityManager,
        WebsiteConfigService websiteService,
        WebsiteAllowList allowList,
        ILogger<DatabaseProxyConfigProvider> logger)
    {
        _configService = configService;
        _groupRepo = groupRepo;
        _identityManager = identityManager;
        _websiteService = websiteService;
        _allowList = allowList;
        _logger = logger;
        _cts = new CancellationTokenSource();
        _currentConfig = BuildConfig();

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
                Reload();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[YARP] Failed to check for configuration updates.");
        }
    }

    public void ForceReload()
    {
        _lastVersion = -1;
        Reload();
    }

    public IProxyConfig GetConfig() => _currentConfig;

    /// <summary>从 DB 重新构建配置并触发 YARP 热重载</summary>
    public void Reload()
    {
        try
        {
            _logger.LogInformation("[YARP] Configuration reload triggered from database (Group: {GroupId})", _identityManager.GroupId);
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

    /// <summary>网站代理路由的固定 ID 与集群 ID（整组共享一条路由，避免与 EnsureNoRouteConflict 冲突）</summary>
    public const string WebsiteRouteId = "website-proxy";
    public const string WebsiteClusterId = "website-proxy-cluster";

    /// <summary>
    /// 网站代理路由的匹配路径与 Order。
    /// 必须排在最后（Order 越大优先级越低），避免抢走管理 API 与既有业务路由。
    /// </summary>
    public const int WebsiteRouteOrder = int.MaxValue;

    private List<RouteConfig> BuildRoutes()
    {
        var entities = _configService.GetEnabledRoutes(_identityManager.GroupId);
        var result = new List<RouteConfig>(entities.Count + 1);

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
                Transforms = transforms,
                // Metadata 原样下传给 transform provider（WebsiteProxyTransformProvider 依赖它做路由级门控）
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["DatabaseRouteId"] = e.RouteId
                }
            });
        }

        // 网站代理：注入全局唯一的 catch-all 路由（Order 最低，绝不抢占既有路由）
        AppendWebsiteProxy(result);

        return result;
    }

    /// <summary>
    /// 注入网站代理所需的 Route / Cluster（带 Memory destination 占位，运行期由 transform 改写目标）。
    /// 没有可用站点时不注入路由，避免暴露无意义的 403 入口。
    /// </summary>
    private void AppendWebsiteProxy(List<RouteConfig> routes)
    {
        try
        {
            var groupId = _identityManager.GroupId;
            var websites = _websiteService.GetAllEnabled(groupId);

            // 更新白名单快照（无论是否注入路由都同步，保证禁用/删除立即生效）
            _allowList.Replace(websites);

            if (websites.Count == 0)
            {
                _websiteRoutesInjected = false;
                return;
            }

        var anyBodyRewrite = false;
        var anyCookieRewrite = false;
        foreach (var w in websites)
        {
            if (w.RewriteBody) anyBodyRewrite = true;
            if (w.RewriteCookies) anyCookieRewrite = true;
        }

        routes.Add(new RouteConfig
        {
            RouteId = WebsiteRouteId,
            ClusterId = WebsiteClusterId,
            Order = WebsiteRouteOrder,
            Match = new RouteMatch
            {
                Path = "/{**catchall}",
                Methods = null,
                Hosts = null
            },
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [WebsiteProxyTransformProvider.MetadataKey] = "true",
                [WebsiteProxyTransformProvider.MetadataKey + ".AnyBodyRewrite"] =
                    anyBodyRewrite ? "true" : "false",
                [WebsiteProxyTransformProvider.MetadataKey + ".AnyCookieRewrite"] =
                    anyCookieRewrite ? "true" : "false"
            }
        });

        if (!_websiteRoutesInjected)
        {
            // 注意：日志模板里不要出现字面量花括号，ILogger 会把它当命名占位符并抛异常
            _logger.LogInformation(
                "[WebsiteProxy] 已启用 {WebsiteCount} 个站点（访问形式：网关域名/scheme://目标站/路径）",
                websites.Count);
            _websiteRoutesInjected = true;
        }
        }
        catch (Exception ex)
        {
            // 网站代理装配失败绝不能连带整个 L7 配置重载失败（否则所有路由都会失效）
            _logger.LogError(ex, "[WebsiteProxy] 装配网站代理路由失败，已跳过");
        }
    }

    private List<ClusterConfig> BuildClusters()
    {
        var entities = _configService.GetEnabledClusters(_identityManager.GroupId);
        var result = new List<ClusterConfig>(entities.Count);

        foreach (var c in entities)
        {
            var destEntities = _configService.GetEnabledDestinationsByCluster(c.ClusterId, _identityManager.GroupId);
            var destinations = new Dictionary<string, DestinationConfig>(destEntities.Count);

            foreach (var d in destEntities)
            {
                // 防御：Address 必须是含 scheme 的合法绝对 URI（如 https://...）
                if (string.IsNullOrWhiteSpace(d.Address) ||
                    !Uri.TryCreate(d.Address, UriKind.Absolute, out _))
                {
                    _logger.LogWarning(
                        "[YARP] Destination '{DestId}' in cluster '{ClusterId}' has invalid Address '{Address}' (missing scheme?), skipping.",
                        d.DestId, c.ClusterId, d.Address);
                    continue;
                }

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
                Destinations = destinations,
                HealthCheck = BuildHealthCheck(c.HealthCheckEnabled, c.ClusterId)
            });
        }

        // 网站代理集群：占位 destination（运行时被改写），不做健康检查
        try
        {
            if (_websiteService.GetAllEnabled(_identityManager.GroupId).Count > 0 &&
                !result.Any(c => c.ClusterId == WebsiteClusterId))
            {
                result.Add(new ClusterConfig
                {
                    ClusterId = WebsiteClusterId,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["placeholder"] = new DestinationConfig { Address = "http://127.0.0.1:1/" }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebsiteProxy] 装配网站代理集群失败，已跳过");
        }

        return result;
    }

    /// <summary>解析集群健康检查 JSON（主动 + 被动），解析失败时降级为 null 不阻断配置加载</summary>
    private HealthCheckConfig? BuildHealthCheck(string? json, string clusterId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize(
                json, ProxyYARP.Serialization.AppJsonContext.Default.HealthCheckConfigDto);
            if (dto == null) return null;

            ActiveHealthCheckConfig? active = null;
            if (dto.Active is { Enabled: true } a)
            {
                active = new ActiveHealthCheckConfig
                {
                    Enabled = true,
                    Interval = ParseTimeSpan(a.Interval),
                    Timeout = ParseTimeSpan(a.Timeout),
                    Path = a.Path,
                    Policy = string.IsNullOrWhiteSpace(a.Policy) ? "ConsecutiveFailures" : a.Policy
                };
            }

            PassiveHealthCheckConfig? passive = null;
            if (dto.Passive is { Enabled: true } p)
            {
                passive = new PassiveHealthCheckConfig
                {
                    Enabled = true,
                    Policy = string.IsNullOrWhiteSpace(p.Policy) ? "TransportFailureRate" : p.Policy,
                    ReactivationPeriod = ParseTimeSpan(p.ReactivationPeriod)
                };
            }

            if (active == null && passive == null) return null;
            return new HealthCheckConfig { Active = active, Passive = passive };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[YARP] Invalid health check JSON for cluster {ClusterId}", clusterId);
            return null;
        }
    }

    private static TimeSpan? ParseTimeSpan(string? value)
        => TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var ts) ? ts : null;
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
