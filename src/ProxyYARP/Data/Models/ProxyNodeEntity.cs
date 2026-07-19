#pragma warning disable CS8618
namespace ProxyYARP.Data.Models;

public class ProxyNodeEntity
{
    public string Id { get; set; }
    public string GroupId { get; set; }
    public string? TargetGroupId { get; set; }
    public string Name { get; set; }
    public string? ManagementUrl { get; set; }
    public bool IsManagementEnabled { get; set; } = true;
    
    private DateTime? _lastHeartbeat;
    public DateTime? LastHeartbeat 
    { 
        get => _lastHeartbeat; 
        set => _lastHeartbeat = value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null; 
    }
}
