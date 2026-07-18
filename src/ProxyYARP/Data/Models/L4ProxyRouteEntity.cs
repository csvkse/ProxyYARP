#pragma warning disable CS8618
namespace ProxyYARP.Data.Models;

public class L4ProxyRouteEntity
{
    public string Id { get; set; }
    public string RouteId { get; set; }
    public string GroupId { get; set; }
    public int ListenPort { get; set; }
    public string Protocol { get; set; } = "TCP";
    public string LoadBalancingPolicy { get; set; } = "RoundRobin";
    public int IdleTimeoutSeconds { get; set; } = 60;
    public bool IsEnabled { get; set; } = true;
    private DateTime _createdAt;
    public DateTime CreatedAt { get => _createdAt; set => _createdAt = DateTime.SpecifyKind(value, DateTimeKind.Utc); }

    private DateTime _updatedAt;
    public DateTime UpdatedAt { get => _updatedAt; set => _updatedAt = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}
