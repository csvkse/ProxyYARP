#pragma warning disable CS8618
namespace ProxyYARP.Data.Models;

public class L4ProxyDestinationEntity
{
    public string Id { get; set; }
    public string RouteId { get; set; }
    public string GroupId { get; set; }
    public string TargetHost { get; set; }
    public int TargetPort { get; set; }
    public int Weight { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    private DateTime _createdAt;
    public DateTime CreatedAt { get => _createdAt; set => _createdAt = DateTime.SpecifyKind(value, DateTimeKind.Utc); }

    private DateTime _updatedAt;
    public DateTime UpdatedAt { get => _updatedAt; set => _updatedAt = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}
