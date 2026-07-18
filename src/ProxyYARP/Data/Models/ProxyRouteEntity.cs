#pragma warning disable CS8618

namespace ProxyYARP.Data.Models;

/// <summary>YARP 路由配置实体</summary>
public class ProxyRouteEntity
{
    public string Id { get; set; }
    public string RouteId { get; set; }      // YARP 路由 ID（唯一）
    public string ClusterId { get; set; }    // 关联集群 ID
    public string Path { get; set; }         // 路径匹配模式，如 /api/{**catch-all}
    public string? Methods { get; set; }     // 逗号分隔的 HTTP 方法，如 GET,POST（null=全部）
    public string? Hosts { get; set; }       // 逗号分隔的 Host 匹配，如 example.com
    public int Order { get; set; }           // 优先级（越小越高）
    public bool IsEnabled { get; set; } = true;  // true=启用, false=禁用
    public string? Metadata { get; set; }        // JSON: 附加元数据（传递给 YARP transforms）
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
