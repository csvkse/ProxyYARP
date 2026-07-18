using FluentAssertions;
using ProxyYARP.Tests.TestHelpers;

namespace ProxyYARP.Tests.Unit;

/// <summary>
/// ProxyConfigService 鍗曞厓娴嬭瘯
/// 楠岃瘉锛氳矾鐢?闆嗙兢/鐩爣鑺傜偣 CRUD 鍙?OnConfigChanged 浜嬩欢瑙﹀彂
/// </summary>
public class ProxyConfigServiceTests : IDisposable
{
    private readonly TestDatabase _db;

    public ProxyConfigServiceTests() => _db = new TestDatabase();
    public void Dispose() => _db.Dispose();

    // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ Routes 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void CreateRoute_Should_Persist_To_Database()
    {
        var route = _db.ConfigService.CreateRoute(
            groupId: "default",
            routeId: "api-route",
            clusterId: "backend",
            path: "/api/{**rest}",
            methods: "GET,POST",
            hosts: null,
            order: 0,
            metadata: null);

        route.Should().NotBeNull();
        route.Id.Should().NotBeNullOrWhiteSpace();
        route.RouteId.Should().Be("api-route");
        route.IsEnabled.Should().BeTrue();

        var all = _db.ConfigService.GetAllRoutes("default");
        all.Should().HaveCount(1);
        all[0].RouteId.Should().Be("api-route");
    }

    [Fact]
    public void CreateRoute_Should_Bump_ConfigVersion()
    {
        var v1 = _db.GroupRepo.GetVersion("default");
        _db.ConfigService.CreateRoute("default", "r1", "c1", "/path", null, null, 0, null);
        var v2 = _db.GroupRepo.GetVersion("default");
        v2.Should().BeGreaterThan(v1);
    }

    [Fact]
    public void UpdateRoute_Should_Modify_Existing_Route()
    {
        var route = _db.ConfigService.CreateRoute("default", "old-id", "cluster1", "/old", null, null, 0, null);

        var ok = _db.ConfigService.UpdateRoute(route.Id, "default", "new-id", "cluster2", "/new", "GET", "example.com", 5, true, null);

        ok.Should().BeTrue();
        var updated = _db.ConfigService.GetRouteById(route.Id, "default");
        updated!.RouteId.Should().Be("new-id");
        updated.ClusterId.Should().Be("cluster2");
        updated.Path.Should().Be("/new");
        updated.Methods.Should().Be("GET");
        updated.Hosts.Should().Be("example.com");
        updated.Order.Should().Be(5);
    }

    [Fact]
    public void UpdateRoute_Should_Return_False_For_Nonexistent_Id()
    {
        var ok = _db.ConfigService.UpdateRoute("bad-id", "default", "r", "c", "/", null, null, 0, true, null);
        ok.Should().BeFalse();
    }

    [Fact]
    public void UpdateRoute_Should_Bump_ConfigVersion()
    {
        var route = _db.ConfigService.CreateRoute("default", "r1", "c1", "/p", null, null, 0, null);
        var v1 = _db.GroupRepo.GetVersion("default");
        _db.ConfigService.UpdateRoute(route.Id, "default", "r1", "c1", "/p2", null, null, 0, true, null);
        var v2 = _db.GroupRepo.GetVersion("default");
        v2.Should().BeGreaterThan(v1);
    }

    [Fact]
    public void DeleteRoute_Should_Remove_From_Database()
    {
        var route = _db.ConfigService.CreateRoute("default", "del-route", "c", "/del", null, null, 0, null);
        _db.ConfigService.GetAllRoutes("default").Should().HaveCount(1);

        var ok = _db.ConfigService.DeleteRoute(route.Id, "default");

        ok.Should().BeTrue();
        _db.ConfigService.GetAllRoutes("default").Should().BeEmpty();
    }

    [Fact]
    public void DeleteRoute_Should_Return_False_For_Nonexistent_Id()
    {
        _db.ConfigService.DeleteRoute("bad-id", "default").Should().BeFalse();
    }

    [Fact]
    public void DeleteRoute_Should_Bump_ConfigVersion()
    {
        var route = _db.ConfigService.CreateRoute("default", "r", "c", "/p", null, null, 0, null);
        var v1 = _db.GroupRepo.GetVersion("default");
        _db.ConfigService.DeleteRoute(route.Id, "default");
        var v2 = _db.GroupRepo.GetVersion("default");
        v2.Should().BeGreaterThan(v1);
    }

    [Fact]
    public void GetEnabledRoutes_Should_Only_Return_Enabled()
    {
        var r1 = _db.ConfigService.CreateRoute("default", "r1", "c", "/p1", null, null, 0, null);
        var r2 = _db.ConfigService.CreateRoute("default", "r2", "c", "/p2", null, null, 0, null);
        // 绂佺敤 r2
        _db.ConfigService.UpdateRoute(r2.Id, "default", "r2", "c", "/p2", null, null, 0, false, null);

        var enabled = _db.ConfigService.GetEnabledRoutes("default");
        enabled.Should().HaveCount(1);
        enabled[0].RouteId.Should().Be("r1");
    }

    // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ Clusters 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void CreateCluster_Should_Persist_To_Database()
    {
        var cluster = _db.ConfigService.CreateCluster("default", "my-cluster", "RoundRobin", null);

        cluster.Should().NotBeNull();
        cluster.ClusterId.Should().Be("my-cluster");
        cluster.LoadBalancing.Should().Be("RoundRobin");
        cluster.IsEnabled.Should().BeTrue();

        _db.ConfigService.GetAllClusters("default").Should().HaveCount(1);
    }

    [Fact]
    public void CreateCluster_Should_Bump_ConfigVersion()
    {
        var v1 = _db.GroupRepo.GetVersion("default");
        _db.ConfigService.CreateCluster("default", "c", "RoundRobin", null);
        var v2 = _db.GroupRepo.GetVersion("default");
        v2.Should().BeGreaterThan(v1);
    }

    [Fact]
    public void UpdateCluster_Should_Change_LoadBalancing()
    {
        var cluster = _db.ConfigService.CreateCluster("default", "lb-test", "RoundRobin", null);

        _db.ConfigService.UpdateCluster(cluster.Id, "default", "lb-test", "LeastRequests", null, true);

        var updated = _db.ConfigService.GetClusterById(cluster.Id, "default");
        updated!.LoadBalancing.Should().Be("LeastRequests");
    }

    [Fact]
    public void DeleteCluster_Should_Also_Delete_Destinations()
    {
        var cluster = _db.ConfigService.CreateCluster("default", "to-delete", "RoundRobin", null);
        _db.ConfigService.CreateDestination("default", "to-delete", "d1", "http://backend1:8080", null, null);
        _db.ConfigService.CreateDestination("default", "to-delete", "d2", "http://backend2:8080", null, null);

        _db.ConfigService.DeleteCluster(cluster.Id, "default");

        _db.ConfigService.GetAllClusters("default").Should().BeEmpty();
        _db.ConfigService.GetAllDestinations("default").Should().BeEmpty();
    }

    // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ Destinations 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void CreateDestination_Should_Persist_And_Link_To_Cluster()
    {
        _db.ConfigService.CreateCluster("default", "cluster-a", "RoundRobin", null);
        var dest = _db.ConfigService.CreateDestination("default", "cluster-a", "node-1", "http://10.0.0.1:8080", null, null);

        dest.ClusterId.Should().Be("cluster-a");
        dest.DestId.Should().Be("node-1");
        dest.Address.Should().Be("http://10.0.0.1:8080");
        dest.IsEnabled.Should().BeTrue();

        _db.ConfigService.GetDestinationsByCluster("cluster-a", "default").Should().HaveCount(1);
    }

    [Fact]
    public void CreateDestination_Should_Bump_ConfigVersion()
    {
        _db.ConfigService.CreateCluster("default", "c", "RoundRobin", null);
        var v1 = _db.GroupRepo.GetVersion("default");
        _db.ConfigService.CreateDestination("default", "c", "d", "http://x", null, null);
        var v2 = _db.GroupRepo.GetVersion("default");
        v2.Should().BeGreaterThan(v1);
    }

    [Fact]
    public void GetDestinationsByCluster_Should_Only_Return_Matching_Cluster()
    {
        _db.ConfigService.CreateCluster("default", "cluster-a", "RoundRobin", null);
        _db.ConfigService.CreateDestination("default", "cluster-a", "d1", "http://a1", null, null);
        _db.ConfigService.CreateDestination("default", "cluster-a", "d2", "http://a2", null, null);
        _db.ConfigService.CreateCluster("default", "cluster-b", "RoundRobin", null);
        _db.ConfigService.CreateDestination("default", "cluster-b", "d3", "http://b1", null, null);

        var dests = _db.ConfigService.GetDestinationsByCluster("cluster-a", "default");
        dests.Should().HaveCount(2);
        dests.Should().AllSatisfy(d => d.ClusterId.Should().Be("cluster-a"));
    }

    [Fact]
    public void UpdateDestination_Should_Change_Address()
    {
        _db.ConfigService.CreateCluster("default", "c", "RoundRobin", null);
        var dest = _db.ConfigService.CreateDestination("default", "c", "d", "http://old:8080", null, null);

        _db.ConfigService.UpdateDestination(dest.Id, "default", "d", "http://new:9090", "/health", null, true);

        var updated = _db.ConfigService.GetDestinationById(dest.Id, "default");
        updated!.Address.Should().Be("http://new:9090");
        updated.Health.Should().Be("/health");
    }

    [Fact]
    public void DeleteDestination_Should_Remove_From_Database()
    {
        _db.ConfigService.CreateCluster("default", "c", "RoundRobin", null);
        var dest = _db.ConfigService.CreateDestination("default", "c", "d", "http://x", null, null);
        _db.ConfigService.GetAllDestinations("default").Should().HaveCount(1);

        _db.ConfigService.DeleteDestination(dest.Id, "default");

        _db.ConfigService.GetAllDestinations("default").Should().BeEmpty();
    }

    // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ OnConfigChanged 澶氭搷浣滆鏁?鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    [Fact]
    public void Multiple_Operations_Should_Bump_ConfigVersion_Multiple_Times()
    {
        var v0 = _db.GroupRepo.GetVersion("default");
        _db.ConfigService.CreateRoute("default", "r1", "c1", "/p1", null, null, 0, null);
        var v1 = _db.GroupRepo.GetVersion("default");
        _db.ConfigService.CreateCluster("default", "cluster1", "RoundRobin", null);
        var v2 = _db.GroupRepo.GetVersion("default");
        _db.ConfigService.CreateDestination("default", "cluster1", "d1", "http://x", null, null);
        var v3 = _db.GroupRepo.GetVersion("default");

        v1.Should().BeGreaterThan(v0);
        v2.Should().BeGreaterThan(v1);
        v3.Should().BeGreaterThan(v2);
    }
}
