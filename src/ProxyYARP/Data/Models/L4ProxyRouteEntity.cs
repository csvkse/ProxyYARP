#pragma warning disable CS8618
namespace ProxyYARP.Data.Models;

public class L4ProxyRouteEntity
{
    public string Id { get; set; }
    public string RouteId { get; set; }
    public int ListenPort { get; set; }
    public string Protocol { get; set; } = "TCP";
    public string LoadBalancingPolicy { get; set; } = "RoundRobin";
    public int IdleTimeoutSeconds { get; set; } = 60;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
