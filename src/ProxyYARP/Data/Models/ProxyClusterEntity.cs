#pragma warning disable CS8618

namespace ProxyYARP.Data.Models;

/// <summary>YARP 集群（后端服务组）配置实体</summary>
public class ProxyClusterEntity
{
    public string Id { get; set; }
    public string ClusterId { get; set; }      // YARP 集群 ID（唯一）
    public string LoadBalancing { get; set; }  // RoundRobin | LeastRequests | Random | FirstAlphabetical | PowerOfTwoChoices
    public string? HealthCheckEnabled { get; set; } // JSON: 健康检查配置（可选）
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
