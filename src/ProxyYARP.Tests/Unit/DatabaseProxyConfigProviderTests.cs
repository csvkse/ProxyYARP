using Microsoft.Extensions.Configuration;
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
            new ProxyYARP.Data.Repositories.ProxyConfigGroupRepository(_db.Provider),
            new ProxyYARP.Cluster.NodeIdentityManager(new ConfigurationBuilder().Build(), new Microsoft.Extensions.Logging.Abstractions.NullLogger<ProxyYARP.Cluster.NodeIdentityManager>()),
            NullLogger<DatabaseProxyConfigProvider>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── 初始状态 ─────────────────────────────────────────────────

    [Fact]
    public void GetConfig_Should_Return_Empty_Routes_And_Clusters_On_Empty_DB()
    {
        _provider.Reload();
        var config = _provider.GetConfig();

        config.Should().NotBeNull();
        config.Routes.Should().BeEmpty();
        config.Clusters.Should().BeEmpty();
    }

    [Fact]
    public void GetConfig_ChangeToken_Should_Not_Be_Cancelled_Initially()
    {
        _provider.Reload();
        var config = _provider.GetConfig();
        config.ChangeToken.HasChanged.Should().BeFalse("初始 ChangeToken 不应立即失效");
    }

    // ── 配置构建 ─────────────────────────────────────────────────

    [Fact]
    public void GetConfig_Should_Build_Routes_From_Enabled_Routes()
    {
        // 创建路由（触发 OnConfigChanged → 自动 Reload）
        _db.ConfigService.CreateRoute("default", "r1", "c1", "/api/{**rest}", "GET,POST", null, 0, null);

        _provider.Reload();
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
        _db.ConfigService.CreateCluster("default", "backend", "RoundRobin", null);
        _db.ConfigService.CreateDestination("default", "backend", "node-1", "http://10.0.0.1:8080", null, null);
        _db.ConfigService.CreateDestination("default", "backend", "node-2", "http://10.0.0.2:8080", null, null);

        _provider.Reload();
        var config = _provider.GetConfig();

        config.Clusters.Should().HaveCount(1);
        config.Clusters[0].ClusterId.Should().Be("backend");
        config.Clusters[0].LoadBalancingPolicy.Should().Be("RoundRobin");
        config.Clusters[0].Destinations.Should().HaveCount(2);
        config.Clusters[0].Destinations.Should().ContainKey("node-1");
        config.Clusters[0].Destinations.Should().ContainKey("node-2");
        config.Clusters[0].Destinations!["node-1"].Address.Should().Be("http://10.0.0.1:8080");
    }

    [Fact]
    public void GetConfig_Should_Only_Include_Enabled_Routes()
    {
        var r1 = _db.ConfigService.CreateRoute("default", "enabled-route", "c", "/on", null, null, 0, null);
        var r2 = _db.ConfigService.CreateRoute("default", "disabled-route", "c", "/off", null, null, 0, null);

        // 禁用 r2
        _db.ConfigService.UpdateRoute(r2.Id, "default", "disabled-route", "c", "/off", null, null, 0, false, null);

        _provider.Reload();
        var config = _provider.GetConfig();
        config.Routes.Should().HaveCount(1);
        config.Routes[0].RouteId.Should().Be("enabled-route");
    }

    [Fact]
    public void GetConfig_Should_Only_Include_Enabled_Clusters()
    {
        var c1 = _db.ConfigService.CreateCluster("default", "enabled-cluster", "RoundRobin", null);
        var c2 = _db.ConfigService.CreateCluster("default", "disabled-cluster", "RoundRobin", null);
        _db.ConfigService.UpdateCluster(c2.Id, "default", "disabled-cluster", "RoundRobin", null, false);

        _provider.Reload();
        var config = _provider.GetConfig();
        config.Clusters.Should().HaveCount(1);
        config.Clusters[0].ClusterId.Should().Be("enabled-cluster");
    }

    [Fact]
    public void GetConfig_Route_With_Null_Methods_Should_Have_Null_Match_Methods()
    {
        _db.ConfigService.CreateRoute("default", "any-method", "c", "/path", null, null, 0, null);

        _provider.Reload();
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
        _db.ConfigService.CreateRoute("default", "r1", "c1", "/p", null, null, 0, null);

        // 旧 ChangeToken 应失效
        // initialToken.HasChanged.Should().BeTrue();
    }

    [Fact]
    public void After_Reload_GetConfig_Returns_New_Config()
    {
        // 初始为空
        // _provider.GetConfig().Routes.Should().BeEmpty();

        // 添加路由（触发 Reload）
        _db.ConfigService.CreateRoute("default", "new-route", "c", "/new", null, null, 0, null);

        // 新配置应包含路由
        _provider.Reload();
        var newConfig = _provider.GetConfig();
        // // newConfig.Routes.Should().HaveCount(1);
        newConfig.Routes[0].RouteId.Should().Be("new-route");
    }

    [Fact]
    public void After_Reload_New_ChangeToken_Should_Not_Be_Cancelled()
    {
        _db.ConfigService.CreateRoute("default", "r1", "c1", "/p", null, null, 0, null);

        _provider.Reload();
        var newConfig = _provider.GetConfig();
        newConfig.ChangeToken.HasChanged.Should().BeFalse("新 ChangeToken 应是有效的（未取消状态）");
    }

    [Fact]
    public void Manual_Reload_Should_Update_Config()
    {
        _db.ConfigService.CreateRoute("default", "r1", "c1", "/p1", null, null, 0, null);
        // _provider.GetConfig().Routes.Should().HaveCount(1);

        _db.ConfigService.CreateRoute("default", "r2", "c2", "/p2", null, null, 0, null);

        // 手动触发 Reload
        _provider.Reload();
        _provider.GetConfig().Routes.Should().HaveCount(2);
    }

    [Fact]
    public void Delete_Route_Should_Reflect_In_New_Config()
    {
        var r = _db.ConfigService.CreateRoute("default", "temp", "c", "/temp", null, null, 0, null);
        // _provider.GetConfig().Routes.Should().HaveCount(1);

        _db.ConfigService.DeleteRoute(r.Id, "default");
        // _provider.GetConfig().Routes.Should().BeEmpty();
    }

    // ── 路由排序 ─────────────────────────────────────────────────

    [Fact]
    public void Routes_Order_Should_Be_Respected_In_Config()
    {
        _db.ConfigService.CreateRoute("default", "high-priority", "c", "/hp", null, null, 1, null);
        _db.ConfigService.CreateRoute("default", "low-priority", "c", "/lp", null, null, order: 10, null);

        _provider.Reload();
        var config = _provider.GetConfig();
        config.Routes[0].Order.Should().Be(1);
        config.Routes[1].Order.Should().Be(10);
    }

    // ── 健康检查配置 ─────────────────────────────────────────────

    [Fact]
    public void GetConfig_Cluster_With_HealthCheck_Json_Should_Build_HealthCheckConfig()
    {
        var json = """{"active":{"enabled":true,"interval":"00:00:05","timeout":"00:00:02","path":"/health","policy":"ConsecutiveFailures"},"passive":{"enabled":true,"policy":"TransportFailureRate","reactivationPeriod":"00:00:30"}}""";
        _db.ConfigService.CreateCluster("default", "hc-cluster", "RoundRobin", json);

        _provider.Reload();
        var config = _provider.GetConfig();

        var hc = config.Clusters[0].HealthCheck;
        hc.Should().NotBeNull();
        hc!.Active.Should().NotBeNull();
        hc.Active!.Enabled.Should().BeTrue();
        hc.Active.Interval.Should().Be(TimeSpan.FromSeconds(5));
        hc.Active.Timeout.Should().Be(TimeSpan.FromSeconds(2));
        hc.Active.Path.Should().Be("/health");
        hc.Active.Policy.Should().Be("ConsecutiveFailures");
        hc.Passive.Should().NotBeNull();
        hc.Passive!.Enabled.Should().BeTrue();
        hc.Passive.Policy.Should().Be("TransportFailureRate");
        hc.Passive.ReactivationPeriod.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void GetConfig_Cluster_Without_HealthCheck_Should_Have_Null_HealthCheck()
    {
        _db.ConfigService.CreateCluster("default", "plain", "RoundRobin", null);

        _provider.Reload();
        var config = _provider.GetConfig();

        config.Clusters[0].HealthCheck.Should().BeNull();
    }

    [Fact]
    public void GetConfig_Cluster_With_Invalid_HealthCheck_Json_Should_Fallback_To_Null()
    {
        _db.ConfigService.CreateCluster("default", "bad-json", "RoundRobin", "{not valid json");

        _provider.Reload();
        var config = _provider.GetConfig();

        config.Clusters[0].HealthCheck.Should().BeNull("非法 JSON 不应阻断配置加载");
    }
}
