using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProxyYARP.Tests.TestHelpers;
using Xunit;

namespace ProxyYARP.Tests.Integration;

/// <summary>
/// PostgreSQL 端到端冒烟测试（Testcontainers）
/// 验证：迁移建表、鉴权、L7 路由/集群 CRUD、L4 路由 CRUD、bool/DateTime 映射
/// Docker 不可用时自动跳过
/// </summary>
public class PostgresSmokeTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _pg;

    public PostgresSmokeTests(PostgresFixture fixture) => _pg = fixture;

    [SkippableFact]
    public async Task Pgsql_Auth_And_L7_Crud_Flow()
    {
        Skip.IfNot(_pg.Available, "Docker 不可用，跳过 pgsql 集成测试");

        using var factory = new PostgresWebFactory(_pg.ConnectionString);
        var client = factory.CreateAdminClient();

        // 鉴权：迁移 + 种子 AdminKey 应已生效
        var keysRes = await client.GetAsync("/api/keys");
        keysRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 创建集群
        var clusterRes = await client.PostAsJsonAsync("/api/clusters", new
        {
            clusterId = "pg-cluster",
            loadBalancing = "RoundRobin"
        });
        clusterRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 创建路由
        var routeRes = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId = "pg-route",
            clusterId = "pg-cluster",
            path = "/pg/{**rest}",
            order = 0
        });
        routeRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 读取验证（bool/DateTime 经 pgsql BOOLEAN/TIMESTAMPTZ 往返）
        var routes = await client.GetFromJsonAsync<List<RouteDto>>("/api/routes");
        routes.Should().Contain(r => r.RouteId == "pg-route" && r.IsEnabled);
        routes!.First(r => r.RouteId == "pg-route").CreatedAt.Should().NotBeNullOrWhiteSpace();

        // 禁用路由（bool 写回）
        var route = routes.First(r => r.RouteId == "pg-route");
        var updateRes = await client.PutAsJsonAsync($"/api/routes/{route.Id}", new
        {
            routeId = "pg-route",
            clusterId = "pg-cluster",
            path = "/pg/{**rest}",
            order = 0,
            isEnabled = false
        });
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<List<RouteDto>>("/api/routes");
        after!.First(r => r.RouteId == "pg-route").IsEnabled.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Pgsql_L4_Crud_Flow()
    {
        Skip.IfNot(_pg.Available, "Docker 不可用，跳过 pgsql 集成测试");

        using var factory = new PostgresWebFactory(_pg.ConnectionString);
        var client = factory.CreateAdminClient();

        var createRes = await client.PostAsJsonAsync("/api/tcp-routes", new
        {
            routeId = "pg-l4",
            listenPort = 15999,
            loadBalancingPolicy = "RoundRobin",
            destinations = new[]
            {
                new { targetHost = "127.0.0.1", targetPort = 9001, weight = 1 }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<List<TcpRouteDto>>("/api/tcp-routes");
        list.Should().Contain(r => r.RouteId == "pg-l4" && r.IsEnabled && r.ListenPort == 15999);
    }

    private sealed class RouteDto
    {
        public string Id { get; set; } = "";
        public string RouteId { get; set; } = "";
        public bool IsEnabled { get; set; }
        public string CreatedAt { get; set; } = "";
    }

    private sealed class TcpRouteDto
    {
        public string Id { get; set; } = "";
        public string RouteId { get; set; } = "";
        public int ListenPort { get; set; }
        public bool IsEnabled { get; set; }
    }
}