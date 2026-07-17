using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProxyYARP.Proxy.Yarp;
using ProxyYARP.Tests.TestHelpers;
using Yarp.ReverseProxy.Configuration;

namespace ProxyYARP.Tests.Unit;

/// <summary>
/// DatabaseProxyConfigProvider 单元测试
/// 验证：从 DB 构建 YARP 配置、热重载触发机制
/// </summary>
public class DatabaseProxyConfigProviderTests : IDisposable
{
    private readonly TestDatabase _db;
    private readonly DatabaseProxyConfigProvider _provider;

    public DatabaseProxyConfigProviderTests()
    {
        _db = new TestDatabase();
        _provider = new DatabaseProxyConfigProvider(
            _db.ConfigService,
            NullLogger<DatabaseProxyConfigProvider>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── 初始状态 ─────────────────────────────────────────────────

    [Fact]
    public void GetConfig_Should_Return_Empty_Routes_And_Clusters_On_Empty_DB()
    {
        var config = _provider.GetConfig();

        config.Should().NotBeNull();
        config.Routes.Should().BeEmpty();
        config.Clusters.Should().BeEmpty();
    }

    [Fact]
    public void GetConfig_ChangeToken_Should_Not_Be_Cancelled_Initially()
    {
        var config = _provider.GetConfig();
        config.ChangeToken.HasChanged.Should().BeFalse("初始 ChangeToken 不应立即失效");
    }

    // ── 配置构建 ─────────────────────────────────────────────────

    [Fact]
    public void GetConfig_Should_Build_Routes_From_Enabled_Routes()
    {
        // 创建路由（触发 OnConfigChanged → 自动 Reload）
        _db.ConfigService.CreateRoute("r1", "c1", "/api/{**rest}", "GET,POST", null, 0, null);

        var config = _provider.GetConfig();

        config.Routes.Should().HaveCount(1);
        config.Routes[0].RouteId.Should().Be("r1");
        config.Routes[0].ClusterId.Should().Be("c1");
        config.Routes[0].Match.Path.Should().Be("/api/{**rest}");
        config.Routes[0].Match.Methods.Should().BeEquivalentTo(new[] { "GET", "POST" });
    }

    [Fact]
    public void GetConfig_Should_Build_Clusters_With_Destinations()
    {
        _db.ConfigService.CreateCluster("backend", "RoundRobin", null);
        _db.ConfigService.CreateDestination("backend", "node-1", "http://10.0.0.1:8080", null, null);
        _db.ConfigService.CreateDestination("backend", "node-2", "http://10.0.0.2:8080", null, null);

        var config = _provider.GetConfig();

        config.Clusters.Should().HaveCount(1);
        config.Clusters[0].ClusterId.Should().Be("backend");
        config.Clusters[0].LoadBalancingPolicy.Should().Be("RoundRobin");
        config.Clusters[0].Destinations.Should().HaveCount(2);
        config.Clusters[0].Destinations.Should().ContainKey("node-1");
        config.Clusters[0].Destinations.Should().ContainKey("node-2");
        config.Clusters[0].Destinations["node-1"].Address.Should().Be("http://10.0.0.1:8080");
    }

    [Fact]
    public void GetConfig_Should_Only_Include_Enabled_Routes()
    {
        var r1 = _db.ConfigService.CreateRoute("enabled-route", "c", "/on", null, null, 0, null);
        var r2 = _db.ConfigService.CreateRoute("disabled-route", "c", "/off", null, null, 0, null);

        // 禁用 r2
        _db.ConfigService.UpdateRoute(r2.Id, "disabled-route", "c", "/off", null, null, 0, false, null);

        var config = _provider.GetConfig();
        config.Routes.Should().HaveCount(1);
        config.Routes[0].RouteId.Should().Be("enabled-route");
    }

    [Fact]
    public void GetConfig_Should_Only_Include_Enabled_Clusters()
    {
        var c1 = _db.ConfigService.CreateCluster("enabled-cluster", "RoundRobin", null);
        var c2 = _db.ConfigService.CreateCluster("disabled-cluster", "RoundRobin", null);
        _db.ConfigService.UpdateCluster(c2.Id, "disabled-cluster", "RoundRobin", null, false);

        var config = _provider.GetConfig();
        config.Clusters.Should().HaveCount(1);
        config.Clusters[0].ClusterId.Should().Be("enabled-cluster");
    }

    [Fact]
    public void GetConfig_Route_With_Null_Methods_Should_Have_Null_Match_Methods()
    {
        _db.ConfigService.CreateRoute("any-method", "c", "/path", null, null, 0, null);

        var config = _provider.GetConfig();
        config.Routes[0].Match.Methods.Should().BeNull("null Methods 表示接受全部 HTTP 方法");
    }

    // ── 热重载机制 ───────────────────────────────────────────────

    [Fact]
    public void OnConfigChanged_Should_Invalidate_ChangeToken()
    {
        var initialConfig = _provider.GetConfig();
        var initialToken = initialConfig.ChangeToken;
        initialToken.HasChanged.Should().BeFalse();

        // 触发配置变更
        _db.ConfigService.CreateRoute("r1", "c1", "/p", null, null, 0, null);

        // 旧 ChangeToken 应失效
        initialToken.HasChanged.Should().BeTrue("配置变更后旧 ChangeToken 必须失效");
    }

    [Fact]
    public void After_Reload_GetConfig_Returns_New_Config()
    {
        // 初始为空
        _provider.GetConfig().Routes.Should().BeEmpty();

        // 添加路由（触发 Reload）
        _db.ConfigService.CreateRoute("new-route", "c", "/new", null, null, 0, null);

        // 新配置应包含路由
        var newConfig = _provider.GetConfig();
        newConfig.Routes.Should().HaveCount(1);
        newConfig.Routes[0].RouteId.Should().Be("new-route");
    }

    [Fact]
    public void After_Reload_New_ChangeToken_Should_Not_Be_Cancelled()
    {
        _db.ConfigService.CreateRoute("r1", "c1", "/p", null, null, 0, null);

        var newConfig = _provider.GetConfig();
        newConfig.ChangeToken.HasChanged.Should().BeFalse("新 ChangeToken 应是有效的（未取消状态）");
    }

    [Fact]
    public void Manual_Reload_Should_Update_Config()
    {
        _db.ConfigService.CreateRoute("r1", "c1", "/p1", null, null, 0, null);
        _provider.GetConfig().Routes.Should().HaveCount(1);

        _db.ConfigService.CreateRoute("r2", "c2", "/p2", null, null, 0, null);

        // 手动触发 Reload
        _provider.Reload();
        _provider.GetConfig().Routes.Should().HaveCount(2);
    }

    [Fact]
    public void Delete_Route_Should_Reflect_In_New_Config()
    {
        var r = _db.ConfigService.CreateRoute("temp", "c", "/temp", null, null, 0, null);
        _provider.GetConfig().Routes.Should().HaveCount(1);

        _db.ConfigService.DeleteRoute(r.Id);
        _provider.GetConfig().Routes.Should().BeEmpty();
    }

    // ── 路由排序 ─────────────────────────────────────────────────

    [Fact]
    public void Routes_Order_Should_Be_Respected_In_Config()
    {
        _db.ConfigService.CreateRoute("high-priority", "c", "/hp", null, null, order: 1, null);
        _db.ConfigService.CreateRoute("low-priority", "c", "/lp", null, null, order: 10, null);

        var config = _provider.GetConfig();
        config.Routes[0].Order.Should().Be(1);
        config.Routes[1].Order.Should().Be(10);
    }
}
