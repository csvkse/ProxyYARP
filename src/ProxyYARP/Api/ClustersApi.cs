using ProxyYARP.Serialization;
using ProxyYARP.Auth;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Api;

public static class ClustersApi
{
    public static void MapClustersApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clusters");

        // GET /api/clusters
        group.MapGet("/", (string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var clusters = svc.GetAllClusters(targetGroupId).Select(MapClusterToDto).ToList();
            return Results.Ok(clusters);
        });

        // GET /api/clusters/{id}
        group.MapGet("/{id}", (string id, string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var cluster = svc.GetClusterById(id, targetGroupId);
            return cluster == null ? Results.NotFound() : Results.Ok(MapClusterToDto(cluster));
        });

        // POST /api/clusters
        group.MapPost("/", (string? groupId, HttpContext ctx, CreateClusterRequest req, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.ClusterId)) return Results.BadRequest(new ErrorResponse { Error = "ClusterId is required" });

            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            try
            {
                var entity = svc.CreateCluster(targetGroupId, req.ClusterId, req.LoadBalancing, req.HealthCheckEnabled);
                return Results.Created($"/api/clusters/{entity.Id}", MapClusterToDto(entity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // PUT /api/clusters/{id}
        group.MapPut("/{id}", (string id, string? groupId, HttpContext ctx, UpdateClusterRequest req, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            try
            {
                var ok = svc.UpdateCluster(id, targetGroupId, req.ClusterId ?? "", req.LoadBalancing ?? "RoundRobin", req.HealthCheckEnabled, req.IsEnabled);
                return ok ? Results.Ok(new StatusResponse { Message = "Updated" }) : Results.NotFound();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // DELETE /api/clusters/{id}
        group.MapDelete("/{id}", (string id, string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var ok = svc.DeleteCluster(id, targetGroupId);
            return ok ? Results.Ok(new StatusResponse { Message = "Deleted" }) : Results.NotFound();
        });

        // ---------------- Destinations ----------------

        // GET /api/clusters/destinations
        app.MapGet("/api/clusters/destinations", (string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var dests = svc.GetAllDestinations(targetGroupId).Select(MapDestToDto).ToList();
            return Results.Ok(dests);
        });

        // GET /api/clusters/{clusterId}/destinations
        group.MapGet("/{clusterId}/destinations", (string clusterId, string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var dests = svc.GetDestinationsByCluster(clusterId, targetGroupId).Select(MapDestToDto).ToList();
            return Results.Ok(dests);
        });

        // POST /api/clusters/{clusterId}/destinations
        group.MapPost("/{clusterId}/destinations", (string clusterId, string? groupId, HttpContext ctx, CreateDestinationRequest req, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.Address)) return Results.BadRequest(new ErrorResponse { Error = "Address is required" });

            var destId = string.IsNullOrWhiteSpace(req.DestId) ? Guid.NewGuid().ToString("N")[..8] : req.DestId;
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;

            try
            {
                var entity = svc.CreateDestination(targetGroupId, clusterId, destId, req.Address, req.Health, req.Metadata);
                return Results.Created($"/api/clusters/{clusterId}/destinations/{entity.Id}", MapDestToDto(entity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // PUT /api/clusters/destinations/{id}
        app.MapPut("/api/clusters/destinations/{id}", (string id, string? groupId, HttpContext ctx, UpdateDestinationRequest req, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            try
            {
                var destId = req.DestId ?? "";
                var ok = svc.UpdateDestination(id, targetGroupId, destId, req.Address ?? "", req.Health, req.Metadata, req.IsEnabled);
                return ok ? Results.Ok(new StatusResponse { Message = "Updated" }) : Results.NotFound();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // DELETE /api/clusters/destinations/{id}
        app.MapDelete("/api/clusters/destinations/{id}", (string id, string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var ok = svc.DeleteDestination(id, targetGroupId);
            return ok ? Results.Ok(new StatusResponse { Message = "Deleted" }) : Results.NotFound();
        });
    }

    private static ClusterDto MapClusterToDto(ProxyClusterEntity e) => new()
    {
        Id = e.Id,
        ClusterId = e.ClusterId,
        LoadBalancing = e.LoadBalancing,
        HealthCheckEnabled = e.HealthCheckEnabled,
        IsEnabled = e.IsEnabled,
        CreatedAt = e.CreatedAt.ToString("o"),
        UpdatedAt = e.UpdatedAt.ToString("o")
    };

    private static DestinationDto MapDestToDto(ProxyDestinationEntity e) => new()
    {
        Id = e.Id,
        ClusterId = e.ClusterId,
        DestId = e.DestId,
        Address = e.Address,
        Health = e.Health,
        Metadata = e.Metadata,
        IsEnabled = e.IsEnabled,
        CreatedAt = e.CreatedAt.ToString("o")
    };
}

public sealed class CreateClusterRequest
{
    public string ClusterId { get; set; } = "";
    public string LoadBalancing { get; set; } = "RoundRobin";
    public string? HealthCheckEnabled { get; set; }
}

public sealed class UpdateClusterRequest
{
    public string? ClusterId { get; set; }
    public string? LoadBalancing { get; set; }
    public string? HealthCheckEnabled { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class ClusterDto
{
    public string Id { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string LoadBalancing { get; set; } = "";
    public string? HealthCheckEnabled { get; set; }
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public sealed class CreateDestinationRequest
{
    public string? DestId { get; set; }
    public string Address { get; set; } = "";
    public string? Health { get; set; }
    public string? Metadata { get; set; }
}

public sealed class UpdateDestinationRequest
{
    public string? DestId { get; set; }
    public string? Address { get; set; }
    public string? Health { get; set; }
    public string? Metadata { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class DestinationDto
{
    public string Id { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string DestId { get; set; } = "";
    public string Address { get; set; } = "";
    public string? Health { get; set; }
    public string? Metadata { get; set; }
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
}
