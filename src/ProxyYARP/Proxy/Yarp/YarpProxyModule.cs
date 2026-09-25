using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ProxyYARP.Proxy.Yarp;

/// <summary>
/// YARP L7 HTTP 代理模块封装
/// </summary>
public class YarpProxyModule : IProxyModule
{
    public void RegisterServices(IServiceCollection services)
    {
        // 网站代理白名单快照（配置重载时整体替换）
        services.AddSingleton<WebsiteAllowList>();

        // 注册基于数据库的动态配置提供者
        services.AddSingleton<DatabaseProxyConfigProvider>();

        // 注册基于数据库的动态配置提供者
        services.AddSingleton<DatabaseProxyConfigProvider>();

        // 注册 YARP 并加载配置（主动健康检查服务由 AddReverseProxy 内部默认注册）
        var yarp = services.AddReverseProxy()
            .LoadFromCustomConfig(services);

        // 网站代理转换（读取 RouteConfig.Metadata 做路由级门控，不影响普通 L7 路由）
        yarp.AddTransforms<WebsiteProxyTransformProvider>();
    }

    public void ConfigureKestrel(KestrelServerOptions options)
    {
        // YARP 是 HTTP 级别代理，通过中间件拦截，无需特殊 Kestrel 端口绑定。
        // （监听默认 HTTP 端口即可）
    }

    public void ConfigurePipeline(WebApplication app)
    {
        // 注册 YARP 代理请求处理管道（UsePassiveHealthChecks 启用被动健康检查）
        app.MapReverseProxy(proxy => proxy.UsePassiveHealthChecks());
    }
}
