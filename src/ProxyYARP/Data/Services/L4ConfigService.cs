using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;
using System.Data;

namespace ProxyYARP.Data.Services;

public class L4RouteDto
{
    public L4ProxyRouteEntity Route { get; set; } = null!;
    public List<L4ProxyDestinationEntity> Destinations { get; set; } = new();
}

/// <summary>四层配置业务服务</summary>
public class L4ConfigService
{
    private readonly IDbProvider _provider;
    private readonly L4RouteRepository _routeRepo;
    private readonly L4DestinationRepository _destRepo;

    public L4ConfigService(IDbProvider provider, L4RouteRepository routeRepo, L4DestinationRepository destRepo)
    {
        _provider = provider;
        _routeRepo = routeRepo;
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

    public List<L4RouteDto> GetAllRoutesWithDestinations(string groupId)
    {
        var routes = _routeRepo.GetAll(groupId);
        var allDests = _destRepo.GetAll(groupId).GroupBy(d => d.RouteId).ToDictionary(g => g.Key, g => g.ToList());
        
        return routes.Select(r => new L4RouteDto
        {
            Route = r,
            Destinations = allDests.GetValueOrDefault(r.Id) ?? new List<L4ProxyDestinationEntity>()
        }).ToList();
    }
    
    public List<L4RouteDto> GetEnabledRoutesWithDestinations(string groupId)
    {
        var routes = _routeRepo.GetAllEnabled(groupId);
        var allDests = _destRepo.GetAll(groupId).Where(d => d.IsEnabled).GroupBy(d => d.RouteId).ToDictionary(g => g.Key, g => g.ToList());
        
        return routes.Select(r => new L4RouteDto
        {
            Route = r,
            Destinations = allDests.GetValueOrDefault(r.Id) ?? new List<L4ProxyDestinationEntity>()
        }).ToList();
    }
    
    public L4RouteDto? GetRouteById(string id, string groupId)
    {
        var route = _routeRepo.GetById(id, groupId);
        if (route == null) return null;
        return new L4RouteDto
        {
            Route = route,
            Destinations = _destRepo.GetByRouteId(id, groupId)
        };
    }

    public L4ProxyRouteEntity CreateRoute(string groupId, string routeId, int listenPort, string protocol, string loadBalancingPolicy, List<L4ProxyDestinationEntity> destinations)
    {
        // 检查端口是否已被占用
        var existing = _routeRepo.GetByListenPort(listenPort, groupId);
        if (existing != null)
            throw new Exception($"Listen port {listenPort} is already in use by another TCP route.");

        var now = DateTime.UtcNow;
        var routeInternalId = Guid.NewGuid().ToString();
        var entity = new L4ProxyRouteEntity
        {
            Id = routeInternalId,
            GroupId = groupId,
            RouteId = routeId,
            ListenPort = listenPort,
            Protocol = string.IsNullOrWhiteSpace(protocol) ? "TCP" : protocol,
            LoadBalancingPolicy = loadBalancingPolicy,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""
                INSERT INTO "ProxyYARP_L4Routes"
                ("Id", "RouteId", "GroupId", "ListenPort", "Protocol", "LoadBalancingPolicy", "IdleTimeoutSeconds", "IsEnabled", "CreatedAt", "UpdatedAt")
                VALUES
                (@Id, @RouteId, @GroupId, @ListenPort, @Protocol, @LoadBalancingPolicy, @IdleTimeoutSeconds, @IsEnabled, @CreatedAt, @UpdatedAt)
                """, entity, tx);

            foreach (var dest in destinations)
            {
                dest.Id = Guid.NewGuid().ToString();
                dest.RouteId = routeId;
                dest.GroupId = groupId;
                dest.CreatedAt = now;
                dest.UpdatedAt = now;
                
                conn.Execute("""
                    INSERT INTO "ProxyYARP_L4Destinations"
                    ("Id", "RouteId", "GroupId", "TargetHost", "TargetPort", "Weight", "IsEnabled", "CreatedAt", "UpdatedAt")
                    VALUES
                    (@Id, @RouteId, @GroupId, @TargetHost, @TargetPort, @Weight, @IsEnabled, @CreatedAt, @UpdatedAt)
                    """, dest, tx);
            }
        });

        return entity;
    }

    public bool UpdateRoute(string id, string groupId, string routeId, int listenPort, string loadBalancingPolicy, bool isEnabled, List<L4ProxyDestinationEntity> destinations)
    {
        var entity = _routeRepo.GetById(id, groupId);
        if (entity == null) return false;

        // 检查端口是否已被其他记录占用
        var existing = _routeRepo.GetByListenPort(listenPort, groupId);
        if (existing != null && existing.Id != id)
            throw new Exception($"Listen port {listenPort} is already in use by another TCP route.");

        var now = DateTime.UtcNow;
        entity.RouteId = string.IsNullOrWhiteSpace(routeId) ? entity.RouteId : routeId;
        entity.ListenPort = listenPort == 0 ? entity.ListenPort : listenPort;
        entity.LoadBalancingPolicy = string.IsNullOrWhiteSpace(loadBalancingPolicy) ? entity.LoadBalancingPolicy : loadBalancingPolicy;
        entity.IsEnabled = isEnabled;
        entity.UpdatedAt = now;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""
                UPDATE "ProxyYARP_L4Routes" SET
                    "RouteId" = @RouteId,
                    "ListenPort" = @ListenPort,
                    "Protocol" = @Protocol,
                    "LoadBalancingPolicy" = @LoadBalancingPolicy,
                    "IdleTimeoutSeconds" = @IdleTimeoutSeconds,
                    "IsEnabled" = @IsEnabled,
                    "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id AND "GroupId" = @GroupId
                """, entity, tx);

            // 重建 Destinations
            conn.Execute("""DELETE FROM "ProxyYARP_L4Destinations" WHERE "RouteId" = @RouteId AND "GroupId" = @GroupId""", new { RouteId = id, GroupId = groupId }, tx);

            foreach (var dest in destinations)
            {
                dest.Id = Guid.NewGuid().ToString();
                dest.RouteId = id;
                dest.GroupId = groupId;
                dest.CreatedAt = now;
                dest.UpdatedAt = now;
                
                conn.Execute("""
                    INSERT INTO "ProxyYARP_L4Destinations"
                    ("Id", "RouteId", "GroupId", "TargetHost", "TargetPort", "Weight", "IsEnabled", "CreatedAt", "UpdatedAt")
                    VALUES
                    (@Id, @RouteId, @GroupId, @TargetHost, @TargetPort, @Weight, @IsEnabled, @CreatedAt, @UpdatedAt)
                    """, dest, tx);
            }
        });

        return true;
    }

    public bool DeleteRoute(string id, string groupId)
    {
        var entity = _routeRepo.GetById(id, groupId);
        if (entity == null) return false;

        ExecuteWithVersionBump(groupId, (conn, tx) =>
        {
            conn.Execute("""DELETE FROM "ProxyYARP_L4Routes" WHERE "Id" = @Id AND "GroupId" = @GroupId""", new { Id = id, GroupId = groupId }, tx);
            conn.Execute("""DELETE FROM "ProxyYARP_L4Destinations" WHERE "RouteId" = @RouteId AND "GroupId" = @GroupId""", new { RouteId = id, GroupId = groupId }, tx);
        });

        return true;
    }
}
