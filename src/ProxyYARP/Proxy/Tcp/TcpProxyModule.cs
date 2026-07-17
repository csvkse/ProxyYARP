using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ProxyYARP.Proxy.Tcp;

/// <summary>
/// TCP L4 代理模块封装
/// </summary>
public class TcpProxyModule : IProxyModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // 注册配置提供者
        services.AddSingleton<L4ProxyConfigProvider>();
        
        // 注册独立于 Kestrel 的 TCP 代理后台引擎
        services.AddHostedService<TcpProxyEngine>();
    }

    public void ConfigureKestrel(KestrelServerOptions options)
    {
        // 因为我们采用了 BackgroundService + 原生 Socket 自主管理端口，
        // 所以无需在此处绑定 Kestrel 端口。
    }

    public void ConfigurePipeline(WebApplication app)
    {
        // TCP 属于 4 层协议，在 BackgroundService 中独立处理，不进入 HTTP Pipeline。
    }
}