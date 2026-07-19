using System.Text.Json.Serialization;
using ProxyYARP.Api;

namespace ProxyYARP.Serialization;

/// <summary>
/// AOT 兼容的 JSON 序列化上下文
/// 所有通过 HTTP 传输的 DTO 类型必须在此注册
/// </summary>
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(ApiKeyDto))]
[JsonSerializable(typeof(List<ApiKeyDto>))]
[JsonSerializable(typeof(CreateKeyRequest))]
[JsonSerializable(typeof(UpdateKeyRequest))]
[JsonSerializable(typeof(RouteDto))]
[JsonSerializable(typeof(List<RouteDto>))]
[JsonSerializable(typeof(CreateRouteRequest))]
[JsonSerializable(typeof(UpdateRouteRequest))]
[JsonSerializable(typeof(ClusterDto))]
[JsonSerializable(typeof(List<ClusterDto>))]
[JsonSerializable(typeof(DestinationDto))]
[JsonSerializable(typeof(List<DestinationDto>))]
[JsonSerializable(typeof(CreateClusterRequest))]
[JsonSerializable(typeof(UpdateClusterRequest))]
[JsonSerializable(typeof(CreateDestinationRequest))]
[JsonSerializable(typeof(UpdateDestinationRequest))]
[JsonSerializable(typeof(TcpRouteApiResponseDto))]
[JsonSerializable(typeof(List<TcpRouteApiResponseDto>))]
[JsonSerializable(typeof(TcpDestinationDto))]
[JsonSerializable(typeof(List<TcpDestinationDto>))]
[JsonSerializable(typeof(CreateTcpRouteRequest))]
[JsonSerializable(typeof(UpdateTcpRouteRequest))]
[JsonSerializable(typeof(TestConnectionRequest))]
[JsonSerializable(typeof(ProxyYARP.Proxy.Yarp.HealthCheckConfigDto))]
[JsonSerializable(typeof(NodeDto))]
[JsonSerializable(typeof(List<NodeDto>))]
[JsonSerializable(typeof(UpdateNodeRequest))]
[JsonSerializable(typeof(UpdateNodeGroupRequest))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(VersionResponse))]
[JsonSerializable(typeof(GroupDetailDto))]
[JsonSerializable(typeof(List<GroupDetailDto>))]
[JsonSerializable(typeof(HealthResponse))]
// 通用响应包装类型
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<Dictionary<string, string>>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(object))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
internal partial class AppJsonContext : JsonSerializerContext { }

public sealed class StatusResponse
{
    public string Message { get; set; } = "";
    public bool Success { get; set; } = true;
}

public sealed class ErrorResponse
{
    public string Error { get; set; } = "";
}

public sealed class VersionResponse
{
    public string Version { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class HealthResponse
{
    public string Status { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsManagementEnabled { get; set; }
    public string ManagementUrl { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
