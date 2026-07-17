using ProxyYARP.Serialization;
using ProxyYARP.Auth;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Api;

/// <summary>API Key 管理 �?/api/keys（仅 Admin 可写�?/summary>
public static class KeysApi
{
    public static void MapKeysApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/keys");

        // GET /api/keys
        group.MapGet("/", (HttpContext ctx, ApiKeyService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var keys = svc.GetAll().Select(MapToDto).ToList();
            return Results.Ok(keys);
        });

        // GET /api/keys/{id}
        group.MapGet("/{id}", (string id, HttpContext ctx, ApiKeyService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var key = svc.GetById(id);
            return key == null ? Results.NotFound() : Results.Ok(MapToDto(key));
        });

        // POST /api/keys
        group.MapPost("/", (HttpContext ctx, CreateKeyRequest req, ApiKeyService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ErrorResponse { Error = "Name is required" });
            var role = req.Role == KeyRole.Admin ? KeyRole.Admin : KeyRole.ReadOnly;
            var entity = svc.Create(req.Name, role);
            return Results.Created($"/api/keys/{entity.Id}", MapToDto(entity));
        });

        // PUT /api/keys/{id}
        group.MapPut("/{id}", (string id, HttpContext ctx, UpdateKeyRequest req, ApiKeyService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var ok = svc.Update(id, req.Name ?? "", req.Role ?? KeyRole.ReadOnly, req.IsEnabled);
            return ok ? Results.Ok(new StatusResponse { Message = "Updated" }) : Results.NotFound();
        });

        // DELETE /api/keys/{id}
        group.MapDelete("/{id}", (string id, HttpContext ctx, ApiKeyService svc) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var ok = svc.Delete(id);
            return ok ? Results.Ok(new StatusResponse { Message = "Deleted" }) : Results.NotFound();
        });
    }

    private static ApiKeyDto MapToDto(ApiKeyEntity e) => new()
    {
        Id = e.Id,
        KeyValue = e.KeyValue,
        Name = e.Name,
        Role = e.Role,
        IsEnabled = e.IsEnabled == 1,
        CreatedAt = e.CreatedAt,
        LastUsedAt = e.LastUsedAt
    };
}

public sealed class CreateKeyRequest
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = KeyRole.ReadOnly;
}

public sealed class UpdateKeyRequest
{
    public string? Name { get; set; }
    public string? Role { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class ApiKeyDto
{
    public string Id { get; set; } = "";
    public string KeyValue { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? LastUsedAt { get; set; }
}
