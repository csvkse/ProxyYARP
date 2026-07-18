#pragma warning disable CS8618
namespace ProxyYARP.Data.Models;

public class ProxyConfigGroupEntity
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int ConfigVersion { get; set; } = 1;
}
