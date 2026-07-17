using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ProxyYARP.Proxy.Yarp;

/// <summary>
/// YARP L7 HTTP 代理模块封装
/// </summary>
public class YarpProxyModule : IProxyModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // 注册基于数据库的动态配置提供者
        services.AddSingleton<DatabaseProxyConfigProvider>();
        
        // 注册 YARP 并加载配置
        services.AddReverseProxy()
            .LoadFromCustomConfig(services);
    }

    public void ConfigureKestrel(KestrelServerOptions options)
    {
        // YARP 是 HTTP 级别代理，通过中间件拦截，无需特殊 Kestrel 端口绑定。
        // （监听默认 HTTP 端口即可）
    }

    public void ConfigurePipeline(WebApplication app)
    {
        // 注册 YARP 代理请求处理管道
        app.MapReverseProxy();
    }
}
