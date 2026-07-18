#pragma warning disable CS8618
namespace ProxyYARP.Data.Models;

public class L4ProxyDestinationEntity
{
    public string Id { get; set; }
    public string RouteId { get; set; }
    public string TargetHost { get; set; }
    public int TargetPort { get; set; }
    public int Weight { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
