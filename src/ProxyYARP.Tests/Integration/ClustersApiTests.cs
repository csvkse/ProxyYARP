using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProxyYARP.Tests.TestHelpers;

namespace ProxyYARP.Tests.Integration;

/// <summary>
/// Clusters API 集成测试（/api/clusters + /api/clusters/{id}/destinations）
/// 验证：集群 CRUD、目标节点 CRUD、级联删除、权限控制
/// </summary>
public class ClustersApiTests : IClassFixture<ProxyYarpWebFactory>
{
    private readonly ProxyYarpWebFactory _factory;

    public ClustersApiTests(ProxyYarpWebFactory factory) => _factory = factory;

    // ── GET /api/clusters ────────────────────────────────────────

    [Fact]
    public async Task GetAll_Clusters_With_Auth_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.GetAsync("/api/clusters");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await res.Content.ReadFromJsonAsync<List<ClusterDto>>();
        list.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAll_Clusters_Without_Auth_Should_Return_401()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.GetAsync("/api/clusters");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/clusters ───────────────────────────────────────

    [Fact]
    public async Task Create_Cluster_With_Admin_Should_Return_201()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/clusters", new
        {
            clusterId = $"cluster-{Guid.NewGuid():N}",
            loadBalancing = "RoundRobin"
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<ClusterDto>();
        body!.LoadBalancing.Should().Be("RoundRobin");
        body.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Cluster_Without_ClusterId_Should_Return_400()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/clusters", new
        {
            clusterId = "",
            loadBalancing = "RoundRobin"
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Cluster_With_ReadOnly_Should_Return_403()
    {
        var client = _factory.CreateReadOnlyClient();
        var res = await client.PostAsJsonAsync("/api/clusters", new
        {
            clusterId = "forbidden",
            loadBalancing = "Random"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/clusters/{id} ───────────────────────────────────

    [Fact]
    public async Task Update_Cluster_LoadBalancing_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();
        var clusterId = $"lb-{Guid.NewGuid():N}";

        var createRes = await client.PostAsJsonAsync("/api/clusters", new
        {
            clusterId,
            loadBalancing = "RoundRobin"
        });
        var created = await createRes.Content.ReadFromJsonAsync<ClusterDto>();

        var updateRes = await client.PutAsJsonAsync($"/api/clusters/{created!.Id}", new
        {
            clusterId,
            loadBalancing = "LeastRequests",
            isEnabled = true
        });
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await (await client.GetAsync($"/api/clusters/{created.Id}"))
            .Content.ReadFromJsonAsync<ClusterDto>();
        after!.LoadBalancing.Should().Be("LeastRequests");
    }

    // ── DELETE /api/clusters/{id} ─────────────────────────────────

    [Fact]
    public async Task Delete_Cluster_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();
        var clusterId = $"del-{Guid.NewGuid():N}";

        var createRes = await client.PostAsJsonAsync("/api/clusters", new { clusterId, loadBalancing = "Random" });
        var created = await createRes.Content.ReadFromJsonAsync<ClusterDto>();

        var delRes = await client.DeleteAsync($"/api/clusters/{created!.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getRes = await client.GetAsync($"/api/clusters/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/clusters/{clusterId}/destinations ───────────────

    [Fact]
    public async Task Create_Destination_Should_Return_201()
    {
        var client = _factory.CreateAdminClient();
        var clusterId = $"dest-test-{Guid.NewGuid():N}";

        // 先创建集群
        await client.PostAsJsonAsync("/api/clusters", new { clusterId, loadBalancing = "RoundRobin" });

        // 创建目标节点
        var res = await client.PostAsJsonAsync($"/api/clusters/{clusterId}/destinations", new
        {
            destId = "node-1",
            address = "http://10.0.0.1:8080",
            health = "http://10.0.0.1:8080/healthz"
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<DestDto>();
        body!.DestId.Should().Be("node-1");
        body.Address.Should().Be("http://10.0.0.1:8080");
        body.Health.Should().Be("http://10.0.0.1:8080/healthz");
        body.ClusterId.Should().Be(clusterId);
    }

    [Fact]
    public async Task Create_Destination_Without_Address_Should_Return_400()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/clusters/any-cluster/destinations", new
        {
            destId = "d",
            address = ""  // 空地址
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Destination_Without_DestId_Should_Auto_Generate()
    {
        var client = _factory.CreateAdminClient();
        var clusterId = $"autoid-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/clusters", new { clusterId, loadBalancing = "RoundRobin" });

        var res = await client.PostAsJsonAsync($"/api/clusters/{clusterId}/destinations", new
        {
            address = "http://auto:8080"
            // destId 省略
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<DestDto>();
        body!.DestId.Should().NotBeNullOrWhiteSpace("未提供 DestId 时应自动生成");
    }

    // ── GET /api/clusters/{clusterId}/destinations ────────────────

    [Fact]
    public async Task Get_Destinations_By_Cluster_Should_Return_Only_That_Cluster()
    {
        var client = _factory.CreateAdminClient();
        var cA = $"ca-{Guid.NewGuid():N}";
        var cB = $"cb-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/clusters", new { clusterId = cA, loadBalancing = "RoundRobin" });
        await client.PostAsJsonAsync("/api/clusters", new { clusterId = cB, loadBalancing = "RoundRobin" });

        await client.PostAsJsonAsync($"/api/clusters/{cA}/destinations", new { address = "http://a1:8080" });
        await client.PostAsJsonAsync($"/api/clusters/{cA}/destinations", new { address = "http://a2:8080" });
        await client.PostAsJsonAsync($"/api/clusters/{cB}/destinations", new { address = "http://b1:8080" });

        var res = await client.GetAsync($"/api/clusters/{cA}/destinations");
        var dests = await res.Content.ReadFromJsonAsync<List<DestDto>>();
        dests.Should().HaveCount(2);
        dests!.Should().AllSatisfy(d => d.ClusterId.Should().Be(cA));
    }

    // ── DELETE /api/clusters/destinations/{id} ───────────────────

    [Fact]
    public async Task Delete_Destination_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();
        var clusterId = $"del-dest-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/clusters", new { clusterId, loadBalancing = "RoundRobin" });
        var createRes = await client.PostAsJsonAsync($"/api/clusters/{clusterId}/destinations",
            new { address = "http://del:8080" });
        var created = await createRes.Content.ReadFromJsonAsync<DestDto>();

        var delRes = await client.DeleteAsync($"/api/clusters/destinations/{created!.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── 级联删除：删集群 → 目标节点也删除 ────────────────────────

    [Fact]
    public async Task Delete_Cluster_Should_Cascade_Delete_Destinations()
    {
        var client = _factory.CreateAdminClient();
        var clusterId = $"cascade-{Guid.NewGuid():N}";

        var createRes = await client.PostAsJsonAsync("/api/clusters", new { clusterId, loadBalancing = "RoundRobin" });
        var cluster = await createRes.Content.ReadFromJsonAsync<ClusterDto>();

        await client.PostAsJsonAsync($"/api/clusters/{clusterId}/destinations", new { address = "http://n1:8080" });
        await client.PostAsJsonAsync($"/api/clusters/{clusterId}/destinations", new { address = "http://n2:8080" });

        // 验证节点存在
        var beforeDel = await client.GetAsync("/api/clusters/destinations");
        var allBefore = await beforeDel.Content.ReadFromJsonAsync<List<DestDto>>();
        var clusterDests = allBefore!.Where(d => d.ClusterId == clusterId).ToList();
        clusterDests.Should().HaveCount(2);

        // 删除集群
        await client.DeleteAsync($"/api/clusters/{cluster!.Id}");

        // 验证节点也被删除
        var afterDel = await client.GetAsync("/api/clusters/destinations");
        var allAfter = await afterDel.Content.ReadFromJsonAsync<List<DestDto>>();
        allAfter!.Where(d => d.ClusterId == clusterId).Should().BeEmpty("级联删除后目标节点应全部移除");
    }

    // ── GET /api/clusters/destinations（全部）────────────────────

    [Fact]
    public async Task Get_All_Destinations_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.GetAsync("/api/clusters/destinations");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

file sealed class ClusterDto
{
    public string Id { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string LoadBalancing { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

file sealed class DestDto
{
    public string Id { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string DestId { get; set; } = "";
    public string Address { get; set; } = "";
    public string? Health { get; set; }
    public bool IsEnabled { get; set; }
}
