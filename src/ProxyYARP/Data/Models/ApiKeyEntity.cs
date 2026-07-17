#pragma warning disable CS8618

using ProxyYARP.Auth;

namespace ProxyYARP.Data.Models;

/// <summary>API Key 数据实体</summary>
public class ApiKeyEntity
{
    public string Id { get; set; }
    public string KeyValue { get; set; }     // 明文存储（可选 SHA256）
    public string Name { get; set; }         // 描述名称
    public string Role { get; set; }         // Admin | ReadOnly
    public int IsEnabled { get; set; }       // 1=启用, 0=禁用
    public string CreatedAt { get; set; }    // ISO 8601
    public string? LastUsedAt { get; set; }

    public bool IsAdmin => KeyRole.Admin.Equals(Role, StringComparison.OrdinalIgnoreCase);
}
