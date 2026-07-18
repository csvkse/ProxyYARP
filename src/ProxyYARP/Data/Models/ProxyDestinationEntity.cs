#pragma warning disable CS8618

namespace ProxyYARP.Data.Models;

/// <summary>YARP 目标节点（后端实例）实体</summary>
public class ProxyDestinationEntity
{
    public string Id { get; set; }
    public string ClusterId { get; set; }   // 关联集群 ID
    public string DestId { get; set; }      // 目标节点 ID（集群内唯一）
    public string Address { get; set; }     // 目标地址 http://backend:8080
    public string? Health { get; set; }     // 健康检查端点（可选）
    public string? Metadata { get; set; }   // JSON 附加元数据
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
