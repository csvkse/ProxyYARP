using Microsoft.Extensions.Configuration;
using ProxyYARP.Auth;
using Xunit;

namespace ProxyYARP.Tests.Unit;

/// <summary>
/// 管理端路径前缀的解析规则单元测试。
/// Program.cs（挂端点）、ApiKeyMiddleware（鉴权）、NodeHeartbeatService（节点链接）
/// 三处共用这一套规则，必须保持一致，否则会出现「界面能开但 API 401」这类割裂。
/// </summary>
public class ManagementPathTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs)
        {
            if (v is not null) dict[k] = v;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Unset_Should_Default_To_Prefix()
    {
        Assert.Equal("/_proxy", ManagementPath.Resolve(Config()));
        Assert.Equal("/_proxy", ManagementPath.Resolve(null));
    }

    [Fact]
    public void Explicit_Empty_Should_Mean_Root()
    {
        // 向后兼容的逃生口：MANAGEMENT_PATH="" 让管理界面回到根路径
        Assert.Equal("", ManagementPath.Resolve(Config(("Management:PathBase", ""))));
    }

    [Theory]
    [InlineData("proxyadmin", "/proxyadmin")]        // 自动补前导斜杠
    [InlineData("/proxyadmin", "/proxyadmin")]
    [InlineData("/proxyadmin/", "/proxyadmin")]      // 去掉尾部斜杠
    [InlineData("_proxy", "/_proxy")]
    [InlineData("/deep/nested/prefix", "/deep/nested/prefix")]
    public void Custom_Value_Should_Be_Normalized(string input, string expected)
    {
        Assert.Equal(expected, ManagementPath.Resolve(Config(("Management:PathBase", input))));
    }

    [Fact]
    public void Whitespace_Only_Should_Fall_Back_To_Default()
    {
        Assert.Equal(ManagementPath.Default, ManagementPath.Resolve(Config(("Management:PathBase", "   "))));
    }

    [Fact]
    public void Default_Should_Not_Be_Root()
    {
        // 默认值必须是非空前缀：根路径会和用户业务的 catch-all 路由抢匹配
        Assert.StartsWith("/", ManagementPath.Default);
        Assert.NotEqual("/", ManagementPath.Default);
    }
}
