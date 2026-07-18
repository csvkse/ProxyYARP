using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ProxyYARP.Cluster;

public class NodeIdentityManager
{
    public string NodeId { get; }
    public string GroupId { get; }
    public string NodeName { get; }
    public string? ManagementUrl { get; }
    public bool IsManagementEnabled { get; }

    public bool IsNodeNameExplicit { get; }
    public bool IsManagementUrlExplicit { get; }

    private const string IDENTITY_FILE = ".proxy_node_id";

    public NodeIdentityManager(IConfiguration config, ILogger<NodeIdentityManager> logger)
    {
        GroupId = config["ProxyConfig:GroupId"] ?? config["GROUP_ID"] ?? "default";
        
        var nodeName = config["ProxyConfig:NodeName"] ?? config["NODE_NAME"];
        IsNodeNameExplicit = !string.IsNullOrWhiteSpace(nodeName);
        
        ManagementUrl = config["ProxyConfig:ManagementUrl"] ?? config["MANAGEMENT_URL"];
        IsManagementUrlExplicit = !string.IsNullOrWhiteSpace(ManagementUrl);
        
        IsManagementEnabled = config.GetValue<bool>("Management:Enabled", true);

        // Resolve NodeId
        var envNodeId = config["NODE_ID"];
        if (!string.IsNullOrWhiteSpace(envNodeId))
        {
            NodeId = envNodeId;
            logger.LogInformation("NodeId resolved from Environment Variable: {NodeId}", NodeId);
        }
        else
        {
            if (File.Exists(IDENTITY_FILE))
            {
                NodeId = File.ReadAllText(IDENTITY_FILE).Trim();
                logger.LogInformation("NodeId resolved from file: {NodeId}", NodeId);
            }
            else
            {
                NodeId = $"node-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                try
                {
                    File.WriteAllText(IDENTITY_FILE, NodeId);
                    logger.LogInformation("Generated and saved new NodeId: {NodeId}", NodeId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to persist NodeId to {File}. Running in ephemeral mode! Node identity will change on restart.", IDENTITY_FILE);
                }
            }
        }

        NodeName = string.IsNullOrWhiteSpace(nodeName) ? NodeId : nodeName;
    }
}
