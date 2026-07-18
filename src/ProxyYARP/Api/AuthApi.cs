using ProxyYARP.Auth;
using ProxyYARP.Data.Services;
using ProxyYARP.Serialization;

namespace ProxyYARP.Api;

/// <summary>认证 API：POST /api/auth/login</summary>
public static class AuthApi
{
    public static void MapAuthApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // POST /api/auth/login
        group.MapPost("/login", async (HttpContext ctx, LoginRequest req, ApiKeyService keyService) =>
        {
            if (string.IsNullOrWhiteSpace(req.ApiKey))
                return Results.BadRequest(new ErrorResponse { Error = "Key is required" });

            var entity = keyService.Validate(req.ApiKey);
            if (entity == null)
                return Results.Unauthorized();

            // 写 Cookie（7 天有效）
            ctx.Response.Cookies.Append("api_key", req.ApiKey, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = ctx.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });

            return Results.Ok(new AuthResponse
            {
                Token = req.ApiKey,
                Role = entity.Role,
                Name = entity.Name
            });
        });

        // POST /api/auth/logout
        group.MapPost("/logout", (HttpContext ctx) =>
        {
            ctx.Response.Cookies.Delete("api_key");
            return Results.Ok(new StatusResponse { Message = "Logged out" });
        });

        // GET /api/auth/me
        group.MapGet("/me", (HttpContext ctx, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var key = ctx.GetApiKey();
            if (key == null) return Results.Unauthorized();
            return Results.Ok(new AuthResponse
            {
                Token = "***",
                Role = key.Role,
                Name = key.Name,
                GroupId = ident.GroupId
            });
        });
    }
}

public sealed class LoginRequest
{
    public string ApiKey { get; set; } = "";
}

public sealed class AuthResponse
{
    public string Token { get; set; } = "";
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
    public string? GroupId { get; set; }
}
