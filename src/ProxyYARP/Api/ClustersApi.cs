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
        group.MapGet("/", (HttpContext ctx, ProxyConfigService svc) =>
        {
            var clusters = svc.GetEnabledClusters().Select(MapClusterToDto).ToList();
            return Results.Ok(clusters);
        });

        // GET /api/clusters/{id}
        group.MapGet("/{id}", (string id, HttpContext ctx, ProxyConfigService svc) =>
        {
            var cluster = svc.GetClusterById(id);
            return cluster == null ? Results.NotFound() : Results.Ok(MapClusterToDto(cluster));
        });

        // POST /api/clusters
        group.MapPost("/", (HttpContext ctx, CreateClusterRequest req, ProxyConfigService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.ClusterId)) return Results.BadRequest(new ErrorResponse { Error = "ClusterId is required" });

            try
            {
                var entity = svc.CreateCluster(req.ClusterId, req.LoadBalancing, req.HealthCheckEnabled);
                return Results.Created($"/api/clusters/{entity.Id}", MapClusterToDto(entity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // PUT /api/clusters/{id}
        group.MapPut("/{id}", (string id, HttpContext ctx, UpdateClusterRequest req, ProxyConfigService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            try
            {
                var ok = svc.UpdateCluster(id, req.ClusterId ?? "", req.LoadBalancing ?? "RoundRobin", req.HealthCheckEnabled, req.IsEnabled);
                return ok ? Results.Ok(new StatusResponse { Message = "Updated" }) : Results.NotFound();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // DELETE /api/clusters/{id}
        group.MapDelete("/{id}", (string id, HttpContext ctx, ProxyConfigService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var ok = svc.DeleteCluster(id);
            return ok ? Results.Ok(new StatusResponse { Message = "Deleted" }) : Results.NotFound();
        });

        // ---------------- Destinations ----------------

        // GET /api/clusters/destinations
        app.MapGet("/api/clusters/destinations", (HttpContext ctx, ProxyConfigService svc) =>
        {
            var dests = svc.GetAllDestinations().Select(MapDestToDto).ToList();
            return Results.Ok(dests);
        });

        // GET /api/clusters/{clusterId}/destinations
        group.MapGet("/{clusterId}/destinations", (string clusterId, HttpContext ctx, ProxyConfigService svc) =>
        {
            var dests = svc.GetDestinationsByCluster(clusterId).Select(MapDestToDto).ToList();
            return Results.Ok(dests);
        });

        // POST /api/clusters/{clusterId}/destinations
        group.MapPost("/{clusterId}/destinations", (string clusterId, HttpContext ctx, CreateDestinationRequest req, ProxyConfigService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.Address)) return Results.BadRequest(new ErrorResponse { Error = "Address is required" });

            var destId = string.IsNullOrWhiteSpace(req.DestId) ? Guid.NewGuid().ToString("N")[..8] : req.DestId;

            try
            {
                var entity = svc.CreateDestination(clusterId, destId, req.Address, req.Health, req.Metadata);
                return Results.Created($"/api/clusters/{clusterId}/destinations/{entity.Id}", MapDestToDto(entity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // PUT /api/clusters/destinations/{id}
        app.MapPut("/api/clusters/destinations/{id}", (string id, HttpContext ctx, UpdateDestinationRequest req, ProxyConfigService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            try
            {
                var destId = req.DestId ?? "";
                var ok = svc.UpdateDestination(id, destId, req.Address ?? "", req.Health, req.Metadata, req.IsEnabled);
                return ok ? Results.Ok(new StatusResponse { Message = "Updated" }) : Results.NotFound();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // DELETE /api/clusters/destinations/{id}
        app.MapDelete("/api/clusters/destinations/{id}", (string id, HttpContext ctx, ProxyConfigService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var ok = svc.DeleteDestination(id);
            return ok ? Results.Ok(new StatusResponse { Message = "Deleted" }) : Results.NotFound();
        });
    }

    private static ClusterDto MapClusterToDto(ProxyClusterEntity e) => new()
    {
        Id = e.Id,
        ClusterId = e.ClusterId,
        LoadBalancing = e.LoadBalancing,
        HealthCheckEnabled = e.HealthCheckEnabled,
        IsEnabled = e.IsEnabled == 1,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };

    private static DestinationDto MapDestToDto(ProxyDestinationEntity e) => new()
    {
        Id = e.Id,
        ClusterId = e.ClusterId,
        DestId = e.DestId,
        Address = e.Address,
        Health = e.Health,
        Metadata = e.Metadata,
        IsEnabled = e.IsEnabled == 1,
        CreatedAt = e.CreatedAt
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
