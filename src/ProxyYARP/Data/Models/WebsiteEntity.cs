#pragma warning disable CS8618

namespace ProxyYARP.Data.Models;

/// <summary>
/// 网站代理配置实体。
/// 多个网站共用一条 catch-all 路由，本实体即该路由的白名单条目。
/// </summary>
public class WebsiteEntity
{
    public string Id { get; set; }
    public string GroupId { get; set; }        // 所属配置组 ID
    public string Name { get; set; }          // 展示名称，如 "公司官网"
    public string TargetUrl { get; set; }     // 归一化后的目标站点，如 https://example.com
    public string HostAuthority { get; set; } // 匹配用 authority（小写、省略默认端口），如 example.com:8080
    public bool RewriteBody { get; set; } = true;     // 改写 HTML/CSS 内的绝对路径
    public bool RewriteCookies { get; set; } = true;  // 按站点隔离 Cookie 作用域
    public bool IsEnabled { get; set; } = true;
    private DateTime _createdAt;
    public DateTime CreatedAt { get => _createdAt; set => _createdAt = DateTime.SpecifyKind(value, DateTimeKind.Utc); }

    private DateTime _updatedAt;
    public DateTime UpdatedAt { get => _updatedAt; set => _updatedAt = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}
