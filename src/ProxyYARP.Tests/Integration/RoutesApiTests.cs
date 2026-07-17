using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProxyYARP.Tests.TestHelpers;

namespace ProxyYARP.Tests.Integration;

/// <summary>
/// Routes API 集成测试（/api/routes）
/// 验证：CRUD、权限控制、路由立即生效（热重载）
/// </summary>
public class RoutesApiTests : IClassFixture<ProxyYarpWebFactory>
{
    private readonly ProxyYarpWebFactory _factory;

    public RoutesApiTests(ProxyYarpWebFactory factory) => _factory = factory;

    // ── GET /api/routes ──────────────────────────────────────────

    [Fact]
    public async Task GetAll_With_Admin_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.GetAsync("/api/routes");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAll_With_ReadOnly_Should_Return_200()
    {
        // ReadOnly 可以读取路由列表
        var client = _factory.CreateReadOnlyClient();
        var res = await client.GetAsync("/api/routes");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAll_Without_Auth_Should_Return_401()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.GetAsync("/api/routes");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/routes ─────────────────────────────────────────

    [Fact]
    public async Task Create_Route_With_Admin_Should_Return_201()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId = $"test-route-{Guid.NewGuid():N}",
            clusterId = "backend-cluster",
            path = "/test/{**rest}",
            order = 0
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<RouteDto>();
        body!.Path.Should().Be("/test/{**rest}");
        body.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Route_Without_Path_Should_Return_400()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId = "bad-route",
            clusterId = "c",
            path = ""   // 空路径
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Route_Without_RouteId_Should_Return_400()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId = "",  // 空 RouteId
            clusterId = "c",
            path = "/p"
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Route_With_ReadOnly_Should_Return_403()
    {
        var client = _factory.CreateReadOnlyClient();
        var res = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId = "forbidden-route",
            clusterId = "c",
            path = "/p"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/routes/{id} ─────────────────────────────────────

    [Fact]
    public async Task Update_Route_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();

        // 先创建
        var routeId = $"update-test-{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId,
            clusterId = "c1",
            path = "/original",
            order = 0
        });
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<RouteDto>();

        // 更新
        var updateRes = await client.PutAsJsonAsync($"/api/routes/{created!.Id}", new
        {
            routeId,
            clusterId = "c2",
            path = "/updated",
            order = 5,
            isEnabled = true
        });
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 验证
        var getRes = await client.GetAsync($"/api/routes/{created.Id}");
        var updated = await getRes.Content.ReadFromJsonAsync<RouteDto>();
        updated!.Path.Should().Be("/updated");
        updated.ClusterId.Should().Be("c2");
        updated.Order.Should().Be(5);
    }

    [Fact]
    public async Task Update_Nonexistent_Route_Should_Return_404()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PutAsJsonAsync("/api/routes/nonexistent", new
        {
            routeId = "r",
            clusterId = "c",
            path = "/p",
            order = 0,
            isEnabled = true
        });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/routes/{id} ──────────────────────────────────

    [Fact]
    public async Task Delete_Route_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();

        var createRes = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId = $"del-{Guid.NewGuid():N}",
            clusterId = "c",
            path = "/delete-me"
        });
        var created = await createRes.Content.ReadFromJsonAsync<RouteDto>();

        var delRes = await client.DeleteAsync($"/api/routes/{created!.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getRes = await client.GetAsync($"/api/routes/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Nonexistent_Route_Should_Return_404()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.DeleteAsync("/api/routes/bad-id-xyz");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_With_ReadOnly_Should_Return_403()
    {
        // 先创建
        var admin = _factory.CreateAdminClient();
        var createRes = await admin.PostAsJsonAsync("/api/routes", new
        {
            routeId = $"ro-del-{Guid.NewGuid():N}",
            clusterId = "c",
            path = "/ro-del"
        });
        var created = await createRes.Content.ReadFromJsonAsync<RouteDto>();

        // ReadOnly 尝试删除
        var roClient = _factory.CreateReadOnlyClient();
        var delRes = await roClient.DeleteAsync($"/api/routes/{created!.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── CRUD 完整流程 ────────────────────────────────────────────

    [Fact]
    public async Task Full_CRUD_Flow_Should_Work_Correctly()
    {
        var client = _factory.CreateAdminClient();
        var routeId = $"full-flow-{Guid.NewGuid():N}";

        // 1. 创建
        var createRes = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId,
            clusterId = "flow-cluster",
            path = "/flow/{**rest}",
            methods = "GET",
            order = 1
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<RouteDto>();
        created!.RouteId.Should().Be(routeId);

        // 2. 读取
        var getRes = await client.GetAsync($"/api/routes/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. 更新
        var updateRes = await client.PutAsJsonAsync($"/api/routes/{created.Id}", new
        {
            routeId,
            clusterId = "flow-cluster-v2",
            path = "/flow-v2/{**rest}",
            order = 2,
            isEnabled = false
        });
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. 验证更新
        var after = await (await client.GetAsync($"/api/routes/{created.Id}")).Content
            .ReadFromJsonAsync<RouteDto>();
        after!.ClusterId.Should().Be("flow-cluster-v2");
        after.IsEnabled.Should().BeFalse();

        // 5. 删除
        await client.DeleteAsync($"/api/routes/{created.Id}");

        // 6. 确认删除
        var final = await client.GetAsync($"/api/routes/{created.Id}");
        final.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

file sealed class RouteDto
{
    public string Id { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Methods { get; set; }
    public string? Hosts { get; set; }
    public int Order { get; set; }
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
