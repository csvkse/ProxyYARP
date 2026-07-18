using ProxyYARP.Auth;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Serialization;

namespace ProxyYARP.Api;

public static class NodesApi
{
    public static void MapNodesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/nodes");

        // GET /api/groups
        app.MapGet("/api/groups", (HttpContext ctx, ProxyConfigGroupRepository repo) => 
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var groups = repo.GetAll().Select(g => g.Id).ToList();
            return Results.Ok(groups);
        });

        // GET /api/nodes
        group.MapGet("/", (HttpContext ctx, ProxyNodeRepository repo) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var nodes = repo.GetAll().Select(MapToDto).ToList();
            return Results.Ok(nodes);
        });

        // PUT /api/nodes/{id}
        group.MapPut("/{id}", (string id, HttpContext ctx, UpdateNodeRequest req, ProxyNodeRepository repo) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            
            var existing = repo.GetAll().FirstOrDefault(n => n.Id == id);
            if (existing == null) return Results.NotFound();

            repo.UpdateNameAndUrl(id, req.Name, req.ManagementUrl);
            return Results.Ok(new StatusResponse { Message = "Updated" });
        });

        // DELETE /api/nodes/{id}
        group.MapDelete("/{id}", (string id, HttpContext ctx, ProxyNodeRepository repo) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            repo.Delete(id);
            return Results.Ok(new StatusResponse { Message = "Deleted" });
        });
    }

    private static NodeDto MapToDto(ProxyNodeEntity entity)
    {
        // Consider offline if heartbeat is older than 30s
        bool isOnline = entity.LastHeartbeat.HasValue && (DateTime.UtcNow - entity.LastHeartbeat.Value).TotalSeconds < 30;

        return new NodeDto
        {
            Id = entity.Id,
            GroupId = entity.GroupId,
            Name = entity.Name,
            ManagementUrl = entity.ManagementUrl,
            IsManagementEnabled = entity.IsManagementEnabled,
            LastHeartbeat = entity.LastHeartbeat.HasValue ? entity.LastHeartbeat.Value.ToString("o") : "",
            IsOnline = isOnline
        };
    }
}

public class NodeDto
{
    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ManagementUrl { get; set; }
    public bool IsManagementEnabled { get; set; }
    public string LastHeartbeat { get; set; } = "";
    public bool IsOnline { get; set; }
}

public class UpdateNodeRequest
{
    public string Name { get; set; } = "";
    public string? ManagementUrl { get; set; }
}
