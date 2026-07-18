using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using ProxyYARP.Api;
using ProxyYARP.Auth;
using ProxyYARP.Common;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Data.Services;
using ProxyYARP.Proxy;
using ProxyYARP.Proxy.Tcp;
using ProxyYARP.Proxy.Udp;
using ProxyYARP.Proxy.Yarp;
using ProxyYARP.Serialization;
using Scalar.AspNetCore;

namespace ProxyYARP;

partial class Program
{
    static async Task Main(string[] args)
    {
        // 帮助信息
        var helpArgs = new[] { "-h", "--h", "-help", "--help", "/?", "/h", "/help" };
        if (args.Any(a => helpArgs.Contains(a.ToLowerInvariant())))
        {
            PrintHelp();
            return;
        }

        // Linux systemd 安装卸载
        if (args.Any(a => a == "--install" || a == "-i"))
        {
            await LinuxServiceInstaller.HandleInstallAsync(args);
            return;
        }
        if (args.Any(a => a == "--uninstall"))
        {
            await LinuxServiceInstaller.HandleUninstallAsync();
            return;
        }

        // 优先级：命令行参数 > 环境变量 > appsettings.json
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        // 环境变量映射（Docker 友好）
        var envMappings = new Dictionary<string, string>
        {
            { "PROXY_PORT",  "ProxyConfig:Port" },
            { "ACCESS_KEY",  "ProxyConfig:AdminKey" },
            { "DB_PATH",     "ProxyConfig:DbPath" }
        };

        var memConfig = new Dictionary<string, string?>();
        foreach (var (envVar, configKey) in envMappings)
        {
            var val = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(val)) memConfig[configKey] = val;
        }

        // 命令行参数映射
        var switchMappings = new Dictionary<string, string>
        {
            { "-p",    "ProxyConfig:Port" },
            { "--Port","ProxyConfig:Port" },
            { "-k",    "ProxyConfig:AdminKey" },
            { "--Key", "ProxyConfig:AdminKey" },
            { "-db",   "ProxyConfig:DbPath" },
            { "--Db",  "ProxyConfig:DbPath" }
        };

        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(memConfig)
            .AddCommandLine(args, switchMappings)
            .Build();

        // 读取关键配置
        var port = config.GetValue<int>("ProxyConfig:Port", 8080);
        var dbPath = config["ProxyConfig:DbPath"] ?? "";
        var adminKey = config["ProxyConfig:AdminKey"] ?? "";

        // 若未提供 Key，自动生成随机 Key
        bool isGeneratedKey = false;
        if (string.IsNullOrWhiteSpace(adminKey))
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            adminKey = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            isGeneratedKey = true;
        }

        // 校验端口（0 表示由 Kestrel 随机分配，测试环境使用）
        if (port < 0 || port > 65535)
        {
            Console.WriteLine($"[ERROR] Invalid port {port}. Port must be between 0 and 65535.");
            return;
        }

        // 配置 SQLite 路径
        DbContext.Configure(dbPath);

        // 打印 Banner（在种子数据之后，以便正确提示 Key 状态）
        var version = typeof(Program).Assembly.GetName().Version;
        var versionStr = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "unknown";

        // 构建 WebApplication
        var builder = WebApplication.CreateSlimBuilder(args);

        // 初始化代理模块 (L4/L7)
        var proxyModules = new IProxyModule[]
        {
            new YarpProxyModule(),
            new TcpProxyModule()
        };

        // Kestrel 配置
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(port);
            foreach (var module in proxyModules) module.ConfigureKestrel(options);
        });

        // AOT JSON 序列化
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
        });

        // OpenAPI 文档生成（AOT 兼容的内置生成器）
        builder.Services.AddOpenApi();

        // 注册 Repository 和 Service
        builder.Services.AddSingleton<ApiKeyRepository>();
        builder.Services.AddSingleton<RouteRepository>();
        builder.Services.AddSingleton<ClusterRepository>();
        builder.Services.AddSingleton<DestinationRepository>();
        builder.Services.AddSingleton<L4RouteRepository>();
        builder.Services.AddSingleton<L4DestinationRepository>();
        builder.Services.AddSingleton<DbInitService>();
        builder.Services.AddSingleton<ApiKeyService>();
        builder.Services.AddSingleton<ProxyConfigService>();
        builder.Services.AddSingleton<L4ConfigService>();

        // 注册代理模块 (L4/L7)
        foreach (var module in proxyModules)
        {
            Console.WriteLine($"Registering services for {module.GetType().Name}");
            module.RegisterServices(builder.Services);
        }

        // 注册 UDP 代理引擎
        builder.Services.AddHostedService<UdpProxyEngine>();

        // 构建应用
        var app = builder.Build();

        // 初始化数据库
        var dbInit = app.Services.GetRequiredService<DbInitService>();
        dbInit.InitTables();
        var adminKeySeeded = dbInit.SeedAdminKey(adminKey);
        dbInit.SeedDemoData();

        // 打印 Banner（此时已确定 Key 是否真正入库）
        Console.WriteLine("==========================================================");
        Console.WriteLine($" ProxyYARP - YARP Reverse Proxy Manager {versionStr}");
        Console.WriteLine("==========================================================");
        Console.WriteLine($"* Port        : {port}");
        Console.WriteLine($"* DB Path     : {DbContext.ConnectionString}");
        var displayKey = isGeneratedKey && adminKeySeeded
            ? adminKey
            : $"{adminKey[..Math.Min(4, adminKey.Length)]}***";
        var keyNotice = (isGeneratedKey, adminKeySeeded) switch
        {
            (true, true) => " (首次生成，请保存 Key)",
            (true, false) => " (数据库已存在管理员 Key，本次生成的随机 Key 未生效)",
            _ => " (已存在配置)"
        };
        Console.WriteLine($"* Admin Key   : {displayKey}{keyNotice}");
        Console.WriteLine($"* Environment : {env}");
        Console.WriteLine($"* Web UI      : http://localhost:{port}/");
        Console.WriteLine("==========================================================");
        Console.WriteLine();

        // 注册 YARP config provider（监听 ProxyConfigService 的变更）
        var provider = app.Services.GetRequiredService<DatabaseProxyConfigProvider>();

        // 嵌入式静态文件（wwwroot 内嵌到 DLL）
        var assembly = typeof(Program).Assembly;
        var embeddedProvider = new ManifestEmbeddedFileProvider(assembly, "wwwroot");
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = embeddedProvider,
            RequestPath = "",
            OnPrepareResponse = ctx =>
            {
                // HTML 文件，强制不缓存
                if (ctx.File.Name.EndsWith(".html"))
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
                }
                else
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=3600");
                }
            }
        });

        // ForwardedHeaders 支持（仅信任回环地址，防止客户端伪造）
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            KnownIPNetworks = { },
            KnownProxies = { }
        };
        forwardedHeadersOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Loopback, 8));
        forwardedHeadersOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.IPv6Loopback, 128));
        app.UseForwardedHeaders(forwardedHeadersOptions);

        // API Key 鉴权中间件
        app.UseMiddleware<ApiKeyMiddleware>();

        // 默认路由 -> 首页
        app.MapGet("/", () => Results.Redirect("/index.html"));

        // 版本信息接口
        app.MapGet("/api/version", () => Results.Ok(new VersionResponse { Version = versionStr, Name = "ProxyYARP" }));

        // 开发者文档（仅 Development 暴露，Production 不挂载）
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi(); // /openapi/v1.json
            app.MapScalarApiReference(); // /scalar/v1
            Console.WriteLine($"* API Docs    : http://localhost:{port}/scalar/v1");
        }

        // 注册 API 路由
        app.MapAuthApi();
        app.MapKeysApi();
        app.MapRoutesApi();
        app.MapClustersApi();
        app.MapTcpRoutesApi();

        // 启动代理管道 (L4/L7)
        foreach (var module in proxyModules)
        {
            module.ConfigurePipeline(app);
        }

        await app.RunAsync();
    }

    static void PrintHelp()
    {
        Console.WriteLine("ProxyYARP - YARP Reverse Proxy with Web Management (Native AOT)");
        Console.WriteLine("Usage: ./ProxyYARP [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --Port <port>       Listening port. Default: 8080");
        Console.WriteLine("  -k, --Key <key>         Initial admin API key (auto-generated if empty)");
        Console.WriteLine("  -db, --Db <path>        SQLite database file path. Default: ./proxy.db");
        Console.WriteLine();
        Console.WriteLine("Service Management (Linux only):");
        Console.WriteLine("  --install               Register as systemd service");
        Console.WriteLine("  --uninstall             Remove systemd service");
        Console.WriteLine();
        Console.WriteLine("Environment Variables:");
        Console.WriteLine("  PROXY_PORT              Listening port");
        Console.WriteLine("  ACCESS_KEY              Initial admin API key");
        Console.WriteLine("  DB_PATH                 SQLite database path");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ./ProxyYARP -p 8080 -k MyAdminKey");
        Console.WriteLine("  ./ProxyYARP -p 8080 -k MyAdminKey --install");
        Console.WriteLine("  PROXY_PORT=8080 ACCESS_KEY=secret ./ProxyYARP");
    }
}

/// <summary>YARP 自定义配置提供者扩展</summary>
public static class YarpConfigExtensions
{
    public static IReverseProxyBuilder LoadFromCustomConfig(
        this IReverseProxyBuilder builder,
        IServiceCollection services)
    {
        builder.Services.AddSingleton<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>(
            sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());
        return builder;
    }
}

// 暴露 Program 供测试使用（WebApplicationFactory<Program>）
public partial class Program { }