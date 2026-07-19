using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ProxyYARP.Cluster;

public class NodeIdentityManager
{
    public string NodeId { get; }
    public string GroupId { get; private set; }
    public string NodeName { get; }
    public string? ManagementUrl { get; private set; }
    public bool IsManagementEnabled { get; }

    public bool IsNodeNameExplicit { get; }
    public bool IsManagementUrlExplicit { get; }

    private const string IDENTITY_FILE = ".proxy_node_id";
    private const string GROUP_FILE = ".proxy_node_group";
    private readonly ILogger<NodeIdentityManager> _logger;

    public NodeIdentityManager(IConfiguration config, ILogger<NodeIdentityManager> logger)
    {
        _logger = logger;
        
        // 1. Resolve NodeId first
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

        // 2. Resolve GroupId using NodeId to isolate files
        var groupFile = $"{GROUP_FILE}_{NodeId}";
        if (File.Exists(groupFile))
        {
            GroupId = File.ReadAllText(groupFile).Trim();
            logger.LogInformation("GroupId loaded from persistent file: {GroupId}", GroupId);
        }
        else
        {
            GroupId = config["ProxyConfig:GroupId"] ?? config["GROUP_ID"] ?? "default";
        }
        
        var nodeName = config["ProxyConfig:NodeName"] ?? config["NODE_NAME"];
        IsNodeNameExplicit = !string.IsNullOrWhiteSpace(nodeName);
        
        ManagementUrl = config["ProxyConfig:ManagementUrl"] ?? config["MANAGEMENT_URL"];
        IsManagementUrlExplicit = !string.IsNullOrWhiteSpace(ManagementUrl);
        
        IsManagementEnabled = config.GetValue<bool>("Management:Enabled", true);

        NodeName = string.IsNullOrWhiteSpace(nodeName) ? NodeId : nodeName;
    }

    public void UpdateGroupId(string newGroupId)
    {
        if (string.IsNullOrWhiteSpace(newGroupId) || GroupId == newGroupId)
            return;

        GroupId = newGroupId;
        var groupFile = $"{GROUP_FILE}_{NodeId}";
        try
        {
            File.WriteAllText(groupFile, GroupId);
            _logger.LogInformation("Node group successfully changed and persisted to {GroupId}", GroupId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist new GroupId to {File}. It will revert on restart.", groupFile);
        }
    }

    public void SetAutoManagementUrl(string url)
    {
        if (!IsManagementUrlExplicit && ManagementUrl != url)
        {
            ManagementUrl = url;
            _logger.LogInformation("ManagementUrl auto-detected as: {Url}", url);
        }
    }
}

