using ProxyYARP.Serialization;
using ProxyYARP.Auth;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Api;

public static class WebsitesApi
{
    public static void MapWebsitesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/websites");

        // GET /api/websites
        group.MapGet("/", (string? groupId, WebsiteConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var websites = svc.GetAll(targetGroupId).Select(MapToDto).ToList();
            return Results.Ok(websites);
        });

        // GET /api/websites/{id}
        group.MapGet("/{id}", (string id, string? groupId, WebsiteConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var w = svc.GetById(id, targetGroupId);
            return w == null ? Results.NotFound() : Results.Ok(MapToDto(w));
        });

        // POST /api/websites
        group.MapPost("/", (string? groupId, HttpContext ctx, CreateWebsiteRequest req, WebsiteConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ErrorResponse { Error = "名称不能为空" });
            if (string.IsNullOrWhiteSpace(req.Url)) return Results.BadRequest(new ErrorResponse { Error = "网站地址不能为空" });

            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            try
            {
                var entity = svc.Create(targetGroupId, req.Name, req.Url, req.RewriteBody, req.RewriteCookies, req.AllowedModes, req.Alias);
                return Results.Created($"/api/websites/{entity.Id}", MapToDto(entity));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // PUT /api/websites/{id}
        group.MapPut("/{id}", (string id, string? groupId, HttpContext ctx, UpdateWebsiteRequest req, WebsiteConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.Url)) return Results.BadRequest(new ErrorResponse { Error = "网站地址不能为空" });

            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            try
            {
                var ok = svc.Update(id, targetGroupId, req.Name ?? "", req.Url, req.RewriteBody, req.RewriteCookies, req.IsEnabled, req.AllowedModes, req.Alias);
                return ok ? Results.Ok(new StatusResponse { Message = "Updated" }) : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // DELETE /api/websites/{id}
        group.MapDelete("/{id}", (string id, string? groupId, HttpContext ctx, WebsiteConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var ok = svc.Delete(id, targetGroupId);
            return ok ? Results.Ok(new StatusResponse { Message = "Deleted" }) : Results.NotFound();
        });

        // POST /api/websites/test-url —— 校验并预览目标网站的访问地址
        group.MapPost("/test-url", (HttpContext ctx, WebsiteTestUrlRequest req) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            try
            {
                var (target, authority) = ProxyYARP.Data.Services.WebsiteConfigService.Normalize(req.Url ?? "");
                var normAlias = ProxyYARP.Data.Services.WebsiteConfigService.NormalizeAlias(req.Alias);
                var normModes = ProxyYARP.Data.Services.WebsiteConfigService.NormalizeAllowedModes(req.AllowedModes, normAlias);
                var accessUrls = BuildAccessUrls(target, authority, normAlias, normModes);

                return Results.Ok(new WebsiteTestUrlResponse
                {
                    TargetUrl = target,
                    AccessUrlSuffix = $"/{target}/",
                    Authority = authority,
                    AccessUrls = accessUrls
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });
    }

    private static List<string> BuildAccessUrls(string targetUrl, string hostAuthority, string? alias, string allowedModes)
    {
        var modes = (allowedModes ?? "Scheme,Prefix").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var urls = new List<string>();
        if (modes.Contains("Prefix", StringComparer.OrdinalIgnoreCase))
            urls.Add($"/proxy/{hostAuthority}/");
        if (modes.Contains("Scheme", StringComparer.OrdinalIgnoreCase))
            urls.Add($"/{targetUrl}/");
        if (modes.Contains("Alias", StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(alias))
            urls.Add($"/s/{alias}/");
        return urls;
    }

    private static WebsiteDto MapToDto(WebsiteEntity e)
    {
        var accessUrls = BuildAccessUrls(e.TargetUrl, e.HostAuthority, e.Alias, e.AllowedModes);
        return new WebsiteDto
        {
            Id = e.Id,
            Name = e.Name,
            Url = e.TargetUrl,
            AccessUrl = $"/{e.TargetUrl}/",
            AccessUrls = accessUrls,
            AllowedModes = e.AllowedModes,
            Alias = e.Alias,
            RewriteBody = e.RewriteBody,
            RewriteCookies = e.RewriteCookies,
            IsEnabled = e.IsEnabled,
            CreatedAt = e.CreatedAt.ToString("o"),
            UpdatedAt = e.UpdatedAt.ToString("o")
        };
    }
}

public sealed class CreateWebsiteRequest
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool RewriteBody { get; set; } = true;
    public bool RewriteCookies { get; set; } = true;
    public string? AllowedModes { get; set; }
    public string? Alias { get; set; }
}

public sealed class UpdateWebsiteRequest
{
    public string? Name { get; set; }
    public string Url { get; set; } = "";
    public bool RewriteBody { get; set; } = true;
    public bool RewriteCookies { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public string? AllowedModes { get; set; }
    public string? Alias { get; set; }
}

public sealed class WebsiteDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string AccessUrl { get; set; } = "";
    public List<string> AccessUrls { get; set; } = [];
    public string AllowedModes { get; set; } = "Scheme,Prefix";
    public string? Alias { get; set; }
    public bool RewriteBody { get; set; }
    public bool RewriteCookies { get; set; }
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public sealed class WebsiteTestUrlRequest
{
    public string Url { get; set; } = "";
    public string? AllowedModes { get; set; }
    public string? Alias { get; set; }
}

public sealed class WebsiteTestUrlResponse
{
    public string TargetUrl { get; set; } = "";
    public string Authority { get; set; } = "";
    public string AccessUrlSuffix { get; set; } = "";
    public List<string> AccessUrls { get; set; } = [];
}
