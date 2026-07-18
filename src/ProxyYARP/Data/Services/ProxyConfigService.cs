using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;
using System.Data;

namespace ProxyYARP.Data.Services;

/// <summary>代理配置业务服务层（路由 + 集群 + 目标节点）</summary>
public class ProxyConfigService
{
    private readonly IDbProvider _provider;
    private readonly RouteRepository _routeRepo;
    private readonly ClusterRepository _clusterRepo;
    private readonly DestinationRepository _destRepo;

    public ProxyConfigService(
        IDbProvider provider,
        RouteRepository routeRepo,
        ClusterRepository clusterRepo,
        DestinationRepository destRepo)
    {
        _provider = provider;
        _routeRepo = routeRepo;
        _clusterRepo = clusterRepo;
        _destRepo = destRepo;
    }

    private void ExecuteWithVersionBump(string groupId, Action<IDbConnection, IDbTransaction> action)
    {
        using var conn = _provider.CreateConnection();
        if (conn.State != ConnectionState.Open) conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            action(conn, tx);
            conn.Execute("""UPDATE "ProxyYARP_ConfigGroups" SET "ConfigVersion" = "ConfigVersion" + 1 WHERE "Id" = @Id""", new { Id = groupId }, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ───────── Routes ─────────

    public List<ProxyRouteEntity> GetAllRoutes(string groupId) => _routeRepo.GetAll(groupId);
    public List<ProxyRouteEntity> GetEnabledRoutes(string groupId) => _routeRepo.GetAllEnabled(groupId);
    public ProxyRouteEntity? GetRouteById(string id, string groupId) => _routeRepo.GetById(id, groupId);

    public ProxyRouteEntity CreateRoute(string groupId, string routeId, string clusterId, string path,
        string? methods, string? hosts, int order, string? metadata)
    {
        var now = DateTime.UtcNow;
        var entity = new ProxyRouteEntity
        {
            Id = Guid.NewGuid().ToString(),
            GroupId = groupId,
            RouteId = routeId,
            ClusterId = clusterId,
            Path = path,
            Methods = methods,
            Hosts = hosts,
            Order = order,
            IsEnabled = true,
            Metadata = metadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        ExecuteWithVersionBump(groupId, (conn, tx) => 
        {
            conn.Execute("""
                INSERT INTO "ProxyYARP_Routes" ("Id", "RouteId", "GroupId", "ClusterId", "Path", "Methods", "Hosts", "Order", "IsEnabled", "Metadata", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @RouteId, @GroupId, @ClusterId, @Path, @Methods, @Hosts, @Order, @IsEnabled, @Metadata, @CreatedAt, @UpdatedAt)
                """, entity, tx);
        });
        return entity;
    }

    public bool UpdateRoute(string id, string groupId, string routeId, string clusterId, string path,
        string? methods, string? hosts, int order, bool isEnabled, string? metadata)
    {
        var entity = _routeRepo.GetById(id, groupId);
        if (entity == null) return false;
        entity.RouteId = string.IsNullOrWhiteSpace(routeId) ? entity.RouteId : routeId;
        entity.ClusterId = string.IsNullOrWhiteSpace(clusterId) ? entity.ClusterId : clusterId;
        entity.Path = string.IsNullOrWhiteSpace(path) ? entity.Path : path;
        entity.Methods = methods;
        entity.Hosts = hosts;
        entity.Order = order;
        entity.IsEnabled = isEnabled;
        entity.Metadata = metadata;
        entity.UpdatedAt = DateTime.UtcNow;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""
                UPDATE "ProxyYARP_Routes"
                SET "RouteId" = @RouteId, "ClusterId" = @ClusterId, "Path" = @Path,
                    "Methods" = @Methods, "Hosts" = @Hosts, "Order" = @Order,
                    "IsEnabled" = @IsEnabled, "Metadata" = @Metadata, "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id AND "GroupId" = @GroupId
                """, entity, tx);
        });
        return true;
    }

    public bool DeleteRoute(string id, string groupId)
    {
        var entity = _routeRepo.GetById(id, groupId);
        if (entity == null) return false;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""DELETE FROM "ProxyYARP_Routes" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }, tx);
        });
        return true;
    }

    // ───────── Clusters ─────────

    public List<ProxyClusterEntity> GetAllClusters(string groupId) => _clusterRepo.GetAll(groupId);
    public List<ProxyClusterEntity> GetEnabledClusters(string groupId) => _clusterRepo.GetAllEnabled(groupId);
    public ProxyClusterEntity? GetClusterById(string id, string groupId) => _clusterRepo.GetById(id, groupId);

    public ProxyClusterEntity CreateCluster(string groupId, string clusterId, string loadBalancing, string? healthCheckEnabled)
    {
        var now = DateTime.UtcNow;
        var entity = new ProxyClusterEntity
        {
            Id = Guid.NewGuid().ToString(),
            GroupId = groupId,
            ClusterId = clusterId,
            LoadBalancing = loadBalancing,
            HealthCheckEnabled = healthCheckEnabled,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""
                INSERT INTO "ProxyYARP_Clusters" ("Id", "ClusterId", "GroupId", "LoadBalancing", "HealthCheckEnabled", "IsEnabled", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @ClusterId, @GroupId, @LoadBalancing, @HealthCheckEnabled, @IsEnabled, @CreatedAt, @UpdatedAt)
                """, entity, tx);
        });
        return entity;
    }

    public bool UpdateCluster(string id, string groupId, string clusterId, string loadBalancing, string? healthCheckEnabled, bool isEnabled)
    {
        var entity = _clusterRepo.GetById(id, groupId);
        if (entity == null) return false;
        entity.ClusterId = string.IsNullOrWhiteSpace(clusterId) ? entity.ClusterId : clusterId;
        entity.LoadBalancing = string.IsNullOrWhiteSpace(loadBalancing) ? entity.LoadBalancing : loadBalancing;
        entity.HealthCheckEnabled = healthCheckEnabled;
        entity.IsEnabled = isEnabled;
        entity.UpdatedAt = DateTime.UtcNow;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""
                UPDATE "ProxyYARP_Clusters"
                SET "ClusterId" = @ClusterId, "LoadBalancing" = @LoadBalancing,
                    "HealthCheckEnabled" = @HealthCheckEnabled, "IsEnabled" = @IsEnabled, "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id AND "GroupId" = @GroupId
                """, entity, tx);
        });
        return true;
    }

    public bool DeleteCluster(string id, string groupId)
    {
        var entity = _clusterRepo.GetById(id, groupId);
        if (entity == null) return false;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""DELETE FROM "ProxyYARP_Destinations" WHERE "ClusterId" = @ClusterId AND "GroupId" = @GroupId""", new { ClusterId = entity.ClusterId, GroupId = groupId }, tx);
            conn.Execute("""DELETE FROM "ProxyYARP_Clusters" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }, tx);
        });
        return true;
    }

    // ───────── Destinations ─────────

    public List<ProxyDestinationEntity> GetDestinationsByCluster(string clusterId, string groupId)
        => _destRepo.GetAllByClusterId(clusterId, groupId);

    public List<ProxyDestinationEntity> GetEnabledDestinationsByCluster(string clusterId, string groupId)
        => _destRepo.GetByClusterId(clusterId, groupId);

    public List<ProxyDestinationEntity> GetAllDestinations(string groupId)
        => _destRepo.GetAll(groupId);

    public ProxyDestinationEntity? GetDestinationById(string id, string groupId) => _destRepo.GetById(id, groupId);

    public ProxyDestinationEntity CreateDestination(string groupId, string clusterId, string destId,
        string address, string? health, string? metadata)
    {
        var entity = new ProxyDestinationEntity
        {
            Id = Guid.NewGuid().ToString(),
            GroupId = groupId,
            ClusterId = clusterId,
            DestId = destId,
            Address = address,
            Health = health,
            Metadata = metadata,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""
                INSERT INTO "ProxyYARP_Destinations" ("Id", "ClusterId", "GroupId", "DestId", "Address", "Health", "Metadata", "IsEnabled", "CreatedAt")
                VALUES (@Id, @ClusterId, @GroupId, @DestId, @Address, @Health, @Metadata, @IsEnabled, @CreatedAt)
                """, entity, tx);
        });
        return entity;
    }

    public bool UpdateDestination(string id, string groupId, string destId, string address,
        string? health, string? metadata, bool isEnabled)
    {
        var entity = _destRepo.GetById(id, groupId);
        if (entity == null) return false;
        entity.DestId = destId;
        entity.Address = address;
        entity.Health = health;
        entity.Metadata = metadata;
        entity.IsEnabled = isEnabled;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""
                UPDATE "ProxyYARP_Destinations"
                SET "DestId" = @DestId, "Address" = @Address, "Health" = @Health,
                    "Metadata" = @Metadata, "IsEnabled" = @IsEnabled
                WHERE "Id" = @Id AND "GroupId" = @GroupId
                """, entity, tx);
        });
        return true;
    }

    public bool DeleteDestination(string id, string groupId)
    {
        var entity = _destRepo.GetById(id, groupId);
        if (entity == null) return false;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""DELETE FROM "ProxyYARP_Destinations" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }, tx);
        });
        return true;
    }
}
