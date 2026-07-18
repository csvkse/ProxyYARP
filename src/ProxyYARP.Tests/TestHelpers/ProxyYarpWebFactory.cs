using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Data.Services;
using ProxyYARP.Proxy.Tcp;

namespace ProxyYARP.Tests.TestHelpers;

/// <summary>
/// 用于集成测试的 WebApplicationFactory
/// 自动注入隔离的临时 SQLite 数据库，并预置管理员 Key
/// </summary>
public class ProxyYarpWebFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string AdminKey = "test-admin-key-12345";
    public const string ReadOnlyKey = "test-readonly-key-99";

    private readonly string _dbPath;
    private bool _disposed;

    public ProxyYarpWebFactory()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"proxyyarp_integration_{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // 用测试专用 SQLite 替换默认 IDbProvider
            services.RemoveAll<IDbProvider>();
            services.AddSingleton<IDbProvider>(
                new SqliteDbProvider($"Data Source={_dbPath};Cache=Shared;"));

            // 移除 UDP 代理引擎（测试环境不需要 UDP 转发）
            var udpServices = services
                .Where(sd => sd.ServiceType == typeof(IHostedService) &&
                             sd.ImplementationType?.Name == "UdpProxyEngine")
                .ToList();
            foreach (var sd in udpServices)
                services.Remove(sd);

            // 确保 L4ProxyConfigProvider 已注册
            services.TryAddSingleton<L4ProxyConfigProvider>();
        });

        // 通过环境变量注入配置（比 IConfiguration 注入更早生效）
        builder.UseSetting("ProxyConfig:AdminKey", AdminKey);
        builder.UseSetting("ProxyConfig:Port", "0"); // 随机端口
    }

    /// <summary>获取预配置了 AdminKey 的 HttpClient</summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AdminKey);
        return client;
    }

    /// <summary>获取预配置了 ReadOnly Key 的 HttpClient</summary>
    public HttpClient CreateReadOnlyClient()
    {
        // 先确保 ReadOnly Key 存在
        EnsureReadOnlyKey();
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ReadOnlyKey);
        return client;
    }

    /// <summary>获取未认证的 HttpClient</summary>
    public HttpClient CreateAnonymousClient() => CreateClient();

    private void EnsureReadOnlyKey()
    {
        using var scope = Services.CreateScope();
        var keyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var existing = keyService.Validate(ReadOnlyKey);
        if (existing == null)
        {
            var keyRepo = scope.ServiceProvider.GetRequiredService<ApiKeyRepository>();
            keyRepo.Insert(new ProxyYARP.Data.Models.ApiKeyEntity
            {
                Id = Guid.NewGuid().ToString(),
                KeyValue = ReadOnlyKey,
                Name = "Test ReadOnly",
                Role = "ReadOnly",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }
    }
}