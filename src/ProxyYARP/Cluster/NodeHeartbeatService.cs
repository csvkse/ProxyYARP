using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Cluster;

public class NodeHeartbeatService : BackgroundService
{
    private readonly NodeIdentityManager _identityManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NodeHeartbeatService> _logger;

    public NodeHeartbeatService(
        NodeIdentityManager identityManager,
        IServiceProvider serviceProvider,
        ILogger<NodeHeartbeatService> logger)
    {
        _identityManager = identityManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // wait a moment for DB migrations to finish (usually synchronous, but just to be safe)
        await Task.Delay(2000, stoppingToken);

        _logger.LogInformation("NodeHeartbeatService starting for Node {NodeId} in Group {GroupId}", _identityManager.NodeId, _identityManager.GroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                
                var groupRepo = scope.ServiceProvider.GetRequiredService<ProxyConfigGroupRepository>();
                var nodeRepo = scope.ServiceProvider.GetRequiredService<ProxyNodeRepository>();

                // 0. Auto-detect local IP and Port if not explicitly configured
                if (!_identityManager.IsManagementUrlExplicit)
                {
                    try
                    {
                        var server = _serviceProvider.GetService<IServer>();
                        var addressFeature = server?.Features.Get<IServerAddressesFeature>();
                        if (addressFeature != null && addressFeature.Addresses.Any())
                        {
                            var addr = addressFeature.Addresses.First();
                            
                            string localIp = "127.0.0.1";
                            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                            foreach (var ip in host.AddressList)
                            {
                                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                {
                                    localIp = ip.ToString();
                                    break;
                                }
                            }
                            
                            var uri = new Uri(addr.Replace("[::]", "localhost").Replace("*", "localhost").Replace("+", "localhost"));
                            var newUrl = $"{uri.Scheme}://{localIp}:{uri.Port}";
                            _identityManager.SetAutoManagementUrl(newUrl);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Failed to auto-detect management URL.");
                    }
                }

                // 1. Check for TargetGroupId (Admin assigned a new group to this node)
                var targetGroup = nodeRepo.GetTargetGroupId(_identityManager.NodeId);
                if (!string.IsNullOrWhiteSpace(targetGroup) && targetGroup != _identityManager.GroupId)
                {
                    _logger.LogWarning("Node {NodeId} received migration command to Group '{TargetGroup}'. Migrating now...", _identityManager.NodeId, targetGroup);
                    
                    // a) Ensure the new group exists
                    groupRepo.Upsert(targetGroup);
                    
                    // b) Update local persistent identity
                    _identityManager.UpdateGroupId(targetGroup);
                    
                    // c) Acknowledge the change in DB (Clear TargetGroupId, Update GroupId)
                    nodeRepo.AcknowledgeGroupChange(_identityManager.NodeId, targetGroup);
                    
                    // d) Force YARP to reload the new group's config
                    var configProvider = scope.ServiceProvider.GetService<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>() as Proxy.Yarp.DatabaseProxyConfigProvider;
                    configProvider?.ForceReload();
                    
                    _logger.LogInformation("Node {NodeId} successfully migrated to Group '{GroupId}'", _identityManager.NodeId, _identityManager.GroupId);
                }

                // 2. Auto-create Group if it doesn't exist (useful for brand new nodes or default)
                groupRepo.Upsert(_identityManager.GroupId);

                // 3. Register/Update Node Heartbeat
                nodeRepo.UpsertHeartbeat(
                    _identityManager.NodeId,
                    _identityManager.GroupId,
                    _identityManager.NodeName,
                    _identityManager.ManagementUrl,
                    _identityManager.IsManagementEnabled,
                    _identityManager.IsNodeNameExplicit,
                    _identityManager.IsManagementUrlExplicit);
                
                _logger.LogDebug("Heartbeat sent successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Node Heartbeat.");
            }

            // Sleep for 10 seconds
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
