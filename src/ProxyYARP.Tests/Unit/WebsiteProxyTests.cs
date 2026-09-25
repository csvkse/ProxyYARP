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
}
