using ProxyYARP.Serialization;
using ProxyYARP.Auth;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Api;

public static class RoutesApi
{
    public static void MapRoutesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/routes");

        // GET /api/routes
        group.MapGet("/", (string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var routes = svc.GetAllRoutes(targetGroupId).Select(MapToDto).ToList();
            return Results.Ok(routes);
        });

        // GET /api/routes/{id}
        group.MapGet("/{id}", (string id, string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var route = svc.GetRouteById(id, targetGroupId);
            return route == null ? Results.NotFound() : Results.Ok(MapToDto(route));
        });

        // POST /api/routes
        group.MapPost("/", (string? groupId, HttpContext ctx, CreateRouteRequest req, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.RouteId)) return Results.BadRequest(new ErrorResponse { Error = "RouteId is required" });
            if (string.IsNullOrWhiteSpace(req.ClusterId)) return Results.BadRequest(new ErrorResponse { Error = "ClusterId is required" });
            if (string.IsNullOrWhiteSpace(req.Path)) return Results.BadRequest(new ErrorResponse { Error = "Path is required" });

            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            try
            {
                var entity = svc.CreateRoute(targetGroupId, req.RouteId, req.ClusterId, req.Path, req.Methods, req.Hosts, req.Order, req.Metadata);
                return Results.Created($"/api/routes/{entity.Id}", MapToDto(entity));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // PUT /api/routes/{id}
        group.MapPut("/{id}", (string id, string? groupId, HttpContext ctx, UpdateRouteRequest req, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            try
            {
                var ok = svc.UpdateRoute(id, targetGroupId, req.RouteId ?? "", req.ClusterId ?? "", req.Path ?? "", req.Methods, req.Hosts, req.Order ?? 0, req.IsEnabled, req.Metadata);
                return ok ? Results.Ok(new StatusResponse { Message = "Updated" }) : Results.NotFound();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // DELETE /api/routes/{id}
        group.MapDelete("/{id}", (string id, string? groupId, HttpContext ctx, ProxyConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var ok = svc.DeleteRoute(id, targetGroupId);
            return ok ? Results.Ok(new StatusResponse { Message = "Deleted" }) : Results.NotFound();
        });
    }

    private static RouteDto MapToDto(ProxyRouteEntity e) => new()
    {
        Id = e.Id,
        RouteId = e.RouteId,
        ClusterId = e.ClusterId,
        Path = e.Path,
        Methods = e.Methods,
        Hosts = e.Hosts,
        Order = e.Order,
        IsEnabled = e.IsEnabled,
        Metadata = e.Metadata,
        CreatedAt = e.CreatedAt.ToString("o"),
        UpdatedAt = e.UpdatedAt.ToString("o")
    };
}

public sealed class CreateRouteRequest
{
    public string RouteId { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Methods { get; set; }
    public string? Hosts { get; set; }
    public int Order { get; set; } = 0;
    public string? Metadata { get; set; }
}

public sealed class UpdateRouteRequest
{
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
    public string? Path { get; set; }
    public string? Methods { get; set; }
    public string? Hosts { get; set; }
    public int? Order { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Metadata { get; set; }
}

public sealed class RouteDto
{
    public string Id { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Methods { get; set; }
    public string? Hosts { get; set; }
    public int Order { get; set; }
    public bool IsEnabled { get; set; }
    public string? Metadata { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
