using ProxyYARP.Data.Services;
using ProxyYARP.Proxy.Yarp;
using Xunit;

namespace ProxyYARP.Tests.Unit;

/// <summary>
/// 网站代理的 URL 归一化与 authority 归一化单元测试。
/// 这些函数是白名单匹配与防绕过的核心，必须有回归保护。
/// </summary>
public class WebsiteProxyTests
{
    // ─────────── Normalize（用户输入 → 归一化 TargetUrl / authority） ───────────

    [Theory]
    [InlineData("http://example.com", "http://example.com", "example.com")]
    [InlineData("https://example.com", "https://example.com", "example.com")]
    [InlineData("https://example.com/", "https://example.com", "example.com")]
    [InlineData("example.com", "https://example.com", "example.com")]                       // 缺省补 https
    [InlineData("HTTPS://EXAMPLE.COM", "https://example.com", "example.com")]               // scheme/host 大小写
    [InlineData("http://example.com:80", "http://example.com", "example.com")]              // 默认端口省略
    [InlineData("https://example.com:443", "https://example.com", "example.com")]
    [InlineData("http://example.com:8080", "http://example.com:8080", "example.com:8080")]  // 非默认端口保留
    [InlineData("http://Example.com:8080/a/b", "http://example.com:8080", "example.com:8080")]
    public void Normalize_Should_Handle_Common_Forms(string input, string expectedUrl, string expectedAuthority)
    {
        var (url, authority) = WebsiteConfigService.Normalize(input);

        Assert.Equal(expectedUrl, url);
        Assert.Equal(expectedAuthority, authority);
    }

    [Theory]
    [InlineData("ftp://example.com")]      // 不支持的协议
    [InlineData("ws://example.com")]       // 仅 http/https
    [InlineData("file:///etc/passwd")]     // file 协议
    [InlineData("not a url at all!!")]     // 明显非法
    [InlineData("   ")]                    // 空白
    [InlineData("http://")]                // 只有 scheme
    public void Normalize_Should_Reject_Invalid_Input(string input)
    {
        Assert.Throws<ArgumentException>(() => WebsiteConfigService.Normalize(input));
    }

    // ─────────── NormalizeAuthority（请求路径里的 authority → 白名单 key） ───────────

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("Example.COM", "example.com")]
    [InlineData("example.com:443", "example.com")]     // 默认 https 端口省略
    [InlineData("example.com:80", "example.com")]      // 默认 http 端口省略
    [InlineData("example.com:8080", "example.com:8080")]
    [InlineData("EXAMPLE.com:80", "example.com")]
    public void NormalizeAuthority_Should_Fold_Default_Ports_And_Case(string input, string expected)
    {
        Assert.Equal(expected, WebsiteProxyTransformProvider.NormalizeAuthority(input));
    }

    [Fact]
    public void NormalizeAuthority_Should_Handle_Ipv6_Literal()
    {
        // IPv6 字面量：最后一个 ':' 才是端口分隔，不能把地址里的冒号当端口
        Assert.Equal("[::1]", WebsiteProxyTransformProvider.NormalizeAuthority("[::1]"));
        Assert.Equal("[::1]:8443", WebsiteProxyTransformProvider.NormalizeAuthority("[::1]:8443"));
    }

    /// <summary>
    /// 关键防绕过用例：用户把站点登记为 https://example.com，
    /// 那么 /http://example.com/... 也必须命中同一条白名单记录（否则白名单可被协议前缀绕过）。
    /// </summary>
    [Fact]
    public void NormalizeAuthority_Should_Be_Protocol_Agnostic_So_AllowList_Cannot_Be_Bypassed()
    {
        // 登记时用 https，访问时改成 http（或反过来）——authority 必须一致
        var (_, httpsAuthority) = WebsiteConfigService.Normalize("https://example.com");
        var (_, httpAuthority) = WebsiteConfigService.Normalize("http://example.com");

        Assert.Equal(httpsAuthority, httpAuthority);

        // 大写 + 显式默认端口同样不能绕过
        var (_, trickyAuthority) = WebsiteConfigService.Normalize("HTTP://Example.com:80");
        Assert.Equal(httpsAuthority, trickyAuthority);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeAuthority_Should_Return_Empty_For_Blank(string input, string expected)
    {
        Assert.Equal(expected, WebsiteProxyTransformProvider.NormalizeAuthority(input));
    }

    // ─────────── NormalizeAlias（短别名校验与规范化） ───────────

    [Theory]
    [InlineData("oa", "oa")]
    [InlineData("GitLab", "gitlab")]
    [InlineData("my-site-1", "my-site-1")]
    [InlineData("app_2", "app_2")]
    public void NormalizeAlias_Should_Handle_Valid_Aliases(string input, string expected)
    {
        Assert.Equal(expected, WebsiteConfigService.NormalizeAlias(input));
    }

    [Theory]
    [InlineData("proxy")]       // 系统保留字
    [InlineData("http")]
    [InlineData("https")]
    [InlineData("api")]
    [InlineData("-bad")]        // 非字母数字开头
    [InlineData("_bad")]
    [InlineData("has space")]   // 空格
    [InlineData("site/123")]    // 斜杠
    public void NormalizeAlias_Should_Reject_Invalid_Or_Reserved(string input)
    {
        Assert.Throws<ArgumentException>(() => WebsiteConfigService.NormalizeAlias(input));
    }

    [Fact]
    public void NormalizeAlias_Should_Return_Null_For_Null_Or_Whitespace()
    {
        Assert.Null(WebsiteConfigService.NormalizeAlias(null));
        Assert.Null(WebsiteConfigService.NormalizeAlias("   "));
    }

    // ─────────── NormalizeAllowedModes（代理模式多选校验） ───────────

    [Fact]
    public void NormalizeAllowedModes_Should_Default_To_Scheme_And_Prefix()
    {
        var modes = WebsiteConfigService.NormalizeAllowedModes(null, null);
        Assert.Equal("Scheme,Prefix", modes);
    }

    [Fact]
    public void NormalizeAllowedModes_Should_Require_Alias_When_Alias_Mode_Selected()
    {
        Assert.Throws<ArgumentException>(() => WebsiteConfigService.NormalizeAllowedModes("Alias", null));
        Assert.Throws<ArgumentException>(() => WebsiteConfigService.NormalizeAllowedModes("Prefix,Alias", "  "));

        var valid = WebsiteConfigService.NormalizeAllowedModes("Prefix,Alias", "oa");
        Assert.Contains("Prefix", valid);
        Assert.Contains("Alias", valid);
    }

    [Fact]
    public void WebsiteEntry_AllowsMode_Should_Check_Correctly()
    {
        var entry = new WebsiteEntry("example.com", true, true, true, "Prefix,Alias", "oa");
        Assert.True(entry.AllowsMode("Prefix"));
        Assert.True(entry.AllowsMode("prefix"));
        Assert.True(entry.AllowsMode("Alias"));
        Assert.False(entry.AllowsMode("Scheme"));
    }

    [Fact]
    public void WebsiteAllowList_Should_Support_Authority_And_Alias_Lookup()
    {
        var list = new WebsiteAllowList();
        var entity = new ProxyYARP.Data.Models.WebsiteEntity
        {
            Id = "1",
            GroupId = "default",
            Name = "OA",
            TargetUrl = "https://oa.local",
            HostAuthority = "oa.local",
            RewriteBody = true,
            RewriteCookies = true,
            AllowedModes = "Prefix,Alias",
            Alias = "oa",
            IsEnabled = true
        };

        list.Replace(new[] { entity });

        // 按 Authority 查询
        Assert.True(list.TryGetByAuthority("oa.local", out var entry1));
        Assert.NotNull(entry1);
        Assert.Equal("oa.local", entry1.Authority);

        // 按 Alias 查询
        Assert.True(list.TryGetByAlias("oa", out var entry2));
        Assert.NotNull(entry2);
        Assert.Equal("oa.local", entry2.Authority);

        // 统一 TryGet
        Assert.True(list.TryGet("oa.local", out _));
        Assert.True(list.TryGet("oa", out _));
        Assert.False(list.TryGet("notfound", out _));
    }
}
