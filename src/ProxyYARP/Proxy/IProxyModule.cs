using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ProxyYARP.Proxy;

/// <summary>
/// 代理模块统一接口，支持 L4/L7 协议扩展。
/// 每个协议（如 YARP、TCP）都需要实现此接口，并在启动时挂载。
/// </summary>
public interface IProxyModule
{
    /// <summary>
    /// 注册服务到 DI 容器。
    /// 可以注册单例配置、后台服务、或者第三方库集成服务。
    /// </summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>
    /// 配置 Kestrel 服务器选项。
    /// 主要用于 L4 代理（如 TCP/UDP）在启动时需要绑定特定端点，
    /// 或者 HTTP/3 的一些特殊绑定需求。
    /// </summary>
    void ConfigureKestrel(KestrelServerOptions options);

    /// <summary>
    /// 配置 ASP.NET Core 请求管道。
    /// 主要用于 L7 代理（如 YARP、中间件路由）拦截和处理请求。
    /// </summary>
    void ConfigurePipeline(WebApplication app);
}
