using ProxyYARP.Serialization;
using ProxyYARP.Auth;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ProxyYARP.Api;

public static class TcpRoutesApi
{
    public static void MapTcpRoutesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tcp-routes");

        // Helper: 检查本地端口是否已被占用
        bool IsPortInUse(int port)
        {
            var ipGlobalProperties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            var activeTcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            if (activeTcpListeners.Any(endpoint => endpoint.Port == port))
            {
                return true;
            }
            return false;
        }

        // GET /api/tcp-routes
        group.MapGet("/", (string? groupId, HttpContext ctx, L4ConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var routes = svc.GetAllRoutesWithDestinations(targetGroupId).Select(MapToDto).ToList();
            return Results.Ok(routes);
        });

        // GET /api/tcp-routes/{id}
        group.MapGet("/{id}", (string id, string? groupId, HttpContext ctx, L4ConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var route = svc.GetRouteById(id, targetGroupId);
            return route == null ? Results.NotFound() : Results.Ok(MapToDto(route));
        });

        // POST /api/tcp-routes
        group.MapPost("/", (string? groupId, HttpContext ctx, CreateTcpRouteRequest req, L4ConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.RouteId)) return Results.BadRequest(new ErrorResponse { Error = "RouteId is required" });
            if (req.ListenPort <= 0 || req.ListenPort > 65535) return Results.BadRequest(new ErrorResponse { Error = "Invalid ListenPort" });
            if (req.Destinations == null || req.Destinations.Count == 0) return Results.BadRequest(new ErrorResponse { Error = "At least one Destination is required" });

            if (IsPortInUse(req.ListenPort))
            {
                return Results.BadRequest(new ErrorResponse { Error = $"本地端口 {req.ListenPort} 已被其他程序占用，请更换端口" });
            }

            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;

            try
            {
                var destEntities = req.Destinations.Select(d => new L4ProxyDestinationEntity
                {
                    TargetHost = d.TargetHost,
                    TargetPort = d.TargetPort,
                    Weight = d.Weight,
                    IsEnabled = true
                }).ToList();

                var entity = svc.CreateRoute(targetGroupId, req.RouteId, req.ListenPort, "TCP", req.LoadBalancingPolicy ?? "RoundRobin", destEntities);
                
                // 返回完整的包含 Destinations 的 dto
                var createdRouteDto = svc.GetRouteById(entity.Id, targetGroupId);
                return Results.Created($"/api/tcp-routes/{entity.Id}", MapToDto(createdRouteDto!));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // PUT /api/tcp-routes/{id}
        group.MapPut("/{id}", (string id, string? groupId, HttpContext ctx, UpdateTcpRouteRequest req, L4ConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (req.Destinations == null || req.Destinations.Count == 0) return Results.BadRequest(new ErrorResponse { Error = "At least one Destination is required" });

            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;

            try
            {
                var existingRoute = svc.GetRouteById(id, targetGroupId);
                if (existingRoute == null) return Results.NotFound();

                if (req.ListenPort.HasValue && req.ListenPort.Value != existingRoute.Route.ListenPort)
                {
                    if (IsPortInUse(req.ListenPort.Value))
                    {
                        return Results.BadRequest(new ErrorResponse { Error = $"本地端口 {req.ListenPort.Value} 已被占用，请更换端口" });
                    }
                }

                var destEntities = req.Destinations.Select(d => new L4ProxyDestinationEntity
                {
                    TargetHost = d.TargetHost,
                    TargetPort = d.TargetPort,
                    Weight = d.Weight,
                    IsEnabled = true
                }).ToList();

                var ok = svc.UpdateRoute(id, targetGroupId, req.RouteId ?? "", req.ListenPort ?? 0, req.LoadBalancingPolicy ?? "RoundRobin", req.IsEnabled, destEntities);
                return ok ? Results.Ok(new StatusResponse { Message = "Updated" }) : Results.NotFound();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message });
            }
        });

        // DELETE /api/tcp-routes/{id}
        group.MapDelete("/{id}", (string id, string? groupId, HttpContext ctx, L4ConfigService svc, ProxyYARP.Cluster.NodeIdentityManager ident) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            var targetGroupId = string.IsNullOrWhiteSpace(groupId) ? ident.GroupId : groupId;
            var ok = svc.DeleteRoute(id, targetGroupId);
            return ok ? Results.Ok(new StatusResponse { Message = "Deleted" }) : Results.NotFound();
        });

        // POST /api/tcp-routes/test-connection
        group.MapPost("/test-connection", async (HttpContext ctx, TestConnectionRequest req) =>
        {
            if (!ctx.IsAdmin()) return Results.Json(new ErrorResponse { Error = "Forbidden" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.TargetHost)) return Results.BadRequest(new ErrorResponse { Error = "TargetHost is required" });
            if (req.TargetPort <= 0 || req.TargetPort > 65535) return Results.BadRequest(new ErrorResponse { Error = "Invalid TargetPort" });

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var probeSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                await probeSocket.ConnectAsync(req.TargetHost, req.TargetPort, cts.Token);
                return Results.Ok(new StatusResponse { Message = "Connection successful" });
            }
            catch (OperationCanceledException)
            {
                return Results.BadRequest(new ErrorResponse { Error = $"连接超时 (2s)，无法连接到 {req.TargetHost}:{req.TargetPort}" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = $"Test failed: {ex.Message}" });

            }
        });
    }

    private static TcpRouteApiResponseDto MapToDto(ProxyYARP.Data.Services.L4RouteDto d) => new()
    {
        Id = d.Route.Id,
        RouteId = d.Route.RouteId,
        ListenPort = d.Route.ListenPort,
        Protocol = d.Route.Protocol,
        LoadBalancingPolicy = d.Route.LoadBalancingPolicy,
        IsEnabled = d.Route.IsEnabled,
        CreatedAt = d.Route.CreatedAt.ToString("o"),
        UpdatedAt = d.Route.UpdatedAt.ToString("o"),
        Destinations = d.Destinations.Select(dest => new TcpDestinationDto
        {
            TargetHost = dest.TargetHost,
            TargetPort = dest.TargetPort,
            Weight = dest.Weight
        }).ToList()
    };
}

public sealed class TcpDestinationDto
{
    public string TargetHost { get; set; } = "";
    public int TargetPort { get; set; }
    public int Weight { get; set; } = 1;
}

public sealed class CreateTcpRouteRequest
{
    public string RouteId { get; set; } = "";
    public int ListenPort { get; set; }
    public string LoadBalancingPolicy { get; set; } = "RoundRobin";
    public List<TcpDestinationDto> Destinations { get; set; } = new();
}

public sealed class UpdateTcpRouteRequest
{
    public string? RouteId { get; set; }
    public int? ListenPort { get; set; }
    public string? LoadBalancingPolicy { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<TcpDestinationDto> Destinations { get; set; } = new();
}

public sealed class TcpRouteApiResponseDto
{
    public string Id { get; set; } = "";
    public string RouteId { get; set; } = "";
    public int ListenPort { get; set; }
    public string Protocol { get; set; } = "";
    public string LoadBalancingPolicy { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public List<TcpDestinationDto> Destinations { get; set; } = new();
}

public sealed class TestConnectionRequest
{
    public string TargetHost { get; set; } = "";
    public int TargetPort { get; set; }
}
