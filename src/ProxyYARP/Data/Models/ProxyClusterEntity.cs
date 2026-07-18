#pragma warning disable CS8618

namespace ProxyYARP.Data.Models;

/// <summary>YARP 集群（后端服务组）配置实体</summary>
public class ProxyClusterEntity
{
    public string Id { get; set; }
    public string ClusterId { get; set; }      // YARP 集群 ID（组内唯一）
    public string GroupId { get; set; }        // 所属配置组 ID
    public string LoadBalancing { get; set; }  // RoundRobin | LeastRequests | Random | FirstAlphabetical | PowerOfTwoChoices
    public string? HealthCheckEnabled { get; set; } // JSON: 健康检查配置（可选）
    public bool IsEnabled { get; set; } = true;
    private DateTime _createdAt;
    public DateTime CreatedAt { get => _createdAt; set => _createdAt = DateTime.SpecifyKind(value, DateTimeKind.Utc); }

    private DateTime _updatedAt;
    public DateTime UpdatedAt { get => _updatedAt; set => _updatedAt = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}
