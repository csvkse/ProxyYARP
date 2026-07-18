using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProxyYARP.Data.Db;
using ProxyYARP.Proxy.Tcp;

namespace ProxyYARP.Tests.TestHelpers;

/// <summary>PostgreSQL 版集成测试工厂：替换 IDbProvider 为容器连接</summary>
public class PostgresWebFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public PostgresWebFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbProvider>();
            services.AddSingleton<IDbProvider>(new PostgreSqlDbProvider(_connectionString));

            var udpServices = services
                .Where(sd => sd.ServiceType == typeof(IHostedService) &&
                             sd.ImplementationType?.Name == "UdpProxyEngine")
                .ToList();
            foreach (var sd in udpServices)
                services.Remove(sd);

            services.TryAddSingleton<L4ProxyConfigProvider>();
        });

        builder.UseSetting("ProxyConfig:AdminKey", ProxyYarpWebFactory.AdminKey);
        builder.UseSetting("ProxyConfig:Port", "0");
    }

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ProxyYarpWebFactory.AdminKey);
        return client;
    }
}