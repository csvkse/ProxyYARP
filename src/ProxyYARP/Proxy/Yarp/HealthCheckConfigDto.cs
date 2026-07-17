namespace ProxyYARP.Proxy.Yarp;

/// <summary>
/// 集群健康检查配置 DTO（对应 DB 中 HealthCheckEnabled 字段存储的 JSON）
/// 示例：{"active":{"enabled":true,"interval":"00:00:10","timeout":"00:00:05","path":"/health","policy":"ConsecutiveFailures"},
///        "passive":{"enabled":true,"policy":"TransportFailureRate","reactivationPeriod":"00:00:30"}}
/// </summary>
public sealed class HealthCheckConfigDto
{
    public ActiveHealthCheckDto? Active { get; set; }
    public PassiveHealthCheckDto? Passive { get; set; }
}

public sealed class ActiveHealthCheckDto
{
    public bool Enabled { get; set; }
    /// <summary>探测间隔，TimeSpan 格式（如 "00:00:10"）</summary>
    public string? Interval { get; set; }
    /// <summary>单次探测超时，TimeSpan 格式</summary>
    public string? Timeout { get; set; }
    /// <summary>探测路径（如 "/health"）</summary>
    public string? Path { get; set; }
    /// <summary>策略名，默认 ConsecutiveFailures</summary>
    public string? Policy { get; set; }
}

public sealed class PassiveHealthCheckDto
{
    public bool Enabled { get; set; }
    /// <summary>策略名，默认 TransportFailureRate</summary>
    public string? Policy { get; set; }
    /// <summary>失败节点重新启用前的冷却时间，TimeSpan 格式</summary>
    public string? ReactivationPeriod { get; set; }
}
