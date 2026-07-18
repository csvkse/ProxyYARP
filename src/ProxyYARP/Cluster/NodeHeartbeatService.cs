using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

                // 1. Auto-create Group if it doesn't exist
                groupRepo.Upsert(_identityManager.GroupId);

                // 2. Register/Update Node Heartbeat
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
