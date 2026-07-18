using FluentAssertions;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Tests.Unit;

/// <summary>SQLite 下 6 个 Repository 的 CRUD 往返测试（验证引号 SQL + bool/DateTime 映射）</summary>
public class SqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"proxyyarp_repo_{Guid.NewGuid():N}.db");
    private readonly SqliteDbProvider _provider;

    public SqliteRepositoryTests()
    {
        _provider = new SqliteDbProvider($"Data Source={_dbPath};Cache=Shared;");
        MigrationRunner.Migrate(_provider);
    }

    [Fact]
    public void ApiKeyRepository_Roundtrip()
    {
        var repo = new ApiKeyRepository(_provider);
        var entity = new ApiKeyEntity
        {
            Id = Guid.NewGuid().ToString(),
            KeyValue = "k-" + Guid.NewGuid().ToString("N"),
            Name = "t", Role = "Admin",
            IsEnabled = true, CreatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        var loaded = repo.GetByKeyValue(entity.KeyValue);
        loaded.Should().NotBeNull();
        loaded!.IsEnabled.Should().BeTrue();
        loaded.CreatedAt.Should().BeCloseTo(entity.CreatedAt, TimeSpan.FromSeconds(1));

        repo.UpdateLastUsed(entity.KeyValue);
        repo.GetById(entity.Id)!.LastUsedAt.Should().NotBeNull();

        loaded.IsEnabled = false;
        repo.Update(loaded);
        repo.GetByKeyValue(entity.KeyValue).Should().BeNull("禁用后 GetByKeyValue 应过滤");

        repo.Delete(entity.Id);
        repo.GetById(entity.Id).Should().BeNull();
    }

    [Fact]
    public void RouteRepository_Roundtrip()
    {
        var repo = new RouteRepository(_provider);
        var entity = new ProxyRouteEntity
        {
            Id = Guid.NewGuid().ToString(), GroupId = "default", RouteId = "r1", ClusterId = "c1",
            Path = "/a/{**rest}", Order = 5, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        repo.GetAllEnabled("default").Should().ContainSingle(r => r.RouteId == "r1");
        repo.GetById(entity.Id, "default")!.Order.Should().Be(5);

        entity.IsEnabled = false;
        entity.Path = "/b/{**rest}";
        repo.Update(entity);
        repo.GetAllEnabled("default").Should().BeEmpty();
        repo.GetById(entity.Id, "default")!.Path.Should().Be("/b/{**rest}");

        repo.Delete(entity.Id, "default");
        repo.GetAll("default").Should().BeEmpty();
    }

    [Fact]
    public void ClusterRepository_Roundtrip()
    {
        var repo = new ClusterRepository(_provider);
        var entity = new ProxyClusterEntity
        {
            Id = Guid.NewGuid().ToString(), GroupId = "default", ClusterId = "c1",
            LoadBalancing = "LeastRequests", IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        repo.GetAllEnabled("default").Should().ContainSingle(c => c.ClusterId == "c1");

        entity.IsEnabled = false;
        repo.Update(entity);
        repo.GetAllEnabled("default").Should().BeEmpty();
        repo.GetById(entity.Id, "default")!.IsEnabled.Should().BeFalse();

        repo.Delete(entity.Id, "default");
        repo.GetAll("default").Should().BeEmpty();
    }

    [Fact]
    public void DestinationRepository_Roundtrip()
    {
        var clusterRepo = new ClusterRepository(_provider);
        clusterRepo.Insert(new ProxyYARP.Data.Models.ProxyClusterEntity { Id = Guid.NewGuid().ToString(), GroupId = "default", ClusterId = "c1", LoadBalancing = "RoundRobin", IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        var repo = new DestinationRepository(_provider);
        var entity = new ProxyDestinationEntity
        {
            Id = Guid.NewGuid().ToString(), GroupId = "default", ClusterId = "c1", DestId = "d1",
            Address = "http://localhost:9000", IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        repo.GetByClusterId("c1", "default").Should().ContainSingle(d => d.DestId == "d1");

        entity.IsEnabled = false;
        repo.Update(entity);
        repo.GetByClusterId("c1", "default").Should().BeEmpty("禁用后应被过滤");
        repo.GetAllByClusterId("c1", "default").Should().HaveCount(1);

        repo.DeleteByClusterId("c1", "default");
        repo.GetAll("default").Should().BeEmpty();
    }

    [Fact]
    public void L4RouteRepository_Roundtrip()
    {
        var repo = new L4RouteRepository(_provider);
        var entity = new L4ProxyRouteEntity
        {
            Id = Guid.NewGuid().ToString(), GroupId = "default", RouteId = "l4r1", ListenPort = 15333,
            Protocol = "TCP", IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        repo.GetByListenPort(15333, "default").Should().NotBeNull();
        repo.GetAllEnabled("default").Should().ContainSingle(r => r.RouteId == "l4r1");

        entity.IsEnabled = false;
        entity.IdleTimeoutSeconds = 120;
        repo.Update(entity);
        repo.GetAllEnabled("default").Should().BeEmpty();
        repo.GetById(entity.Id, "default")!.IdleTimeoutSeconds.Should().Be(120);

        repo.Delete(entity.Id, "default");
        repo.GetAll("default").Should().BeEmpty();
    }

    [Fact]
    public void L4DestinationRepository_Roundtrip()
    {
        var routeRepo = new L4RouteRepository(_provider);
        routeRepo.Insert(new ProxyYARP.Data.Models.L4ProxyRouteEntity { Id = Guid.NewGuid().ToString(), GroupId = "default", RouteId = "l4r1", ListenPort = 9999, Protocol = "TCP", LoadBalancingPolicy = "RoundRobin", IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        var repo = new L4DestinationRepository(_provider);
        var entity = new L4ProxyDestinationEntity
        {
            Id = Guid.NewGuid().ToString(), GroupId = "default", RouteId = "l4r1",
            TargetHost = "127.0.0.1", TargetPort = 9001, Weight = 2,
            IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        var loaded = repo.GetByRouteId("l4r1", "default").Should().ContainSingle().Subject;
        loaded.Weight.Should().Be(2);
        loaded.IsEnabled.Should().BeTrue();

        repo.DeleteByRouteId("l4r1", "default");
        repo.GetAll("default").Should().BeEmpty();
    }

    public void Dispose()
    {
        Thread.Sleep(100);
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* 忽略清理错误 */ }
    }
}
