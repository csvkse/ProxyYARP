using Microsoft.Extensions.Configuration;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Services;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Proxy.Tcp;
using ProxyYARP.Proxy.Udp;
using Xunit;

namespace ProxyYARP.Tests.Unit;

public class UdpProxyEngineTests
{
    [Fact]
    public void UdpProxyEngine_Should_Instantiate_Without_Errors()
    {
        // Arrange
        var dbPath = Path.Combine(Path.GetTempPath(), $"proxyyarp_udp_{Guid.NewGuid():N}.db");
        var provider = new SqliteDbProvider($"Data Source={dbPath};Cache=Shared;");
        MigrationRunner.Migrate(provider);
        var configService = new L4ConfigService(provider, new L4RouteRepository(provider), new L4DestinationRepository(provider));
        var configProviderLogger = new NullLogger<L4ProxyConfigProvider>();
        var configProvider = new L4ProxyConfigProvider(configService, new ProxyConfigGroupRepository(provider), new ProxyYARP.Cluster.NodeIdentityManager(new ConfigurationBuilder().Build(), new NullLogger<ProxyYARP.Cluster.NodeIdentityManager>()), configProviderLogger);

        var engineLogger = new NullLogger<UdpProxyEngine>();

        try
        {
            // Act
            var engine = new UdpProxyEngine(configProvider, engineLogger);

            // Assert
            Assert.NotNull(engine);
        }
        finally
        {
            Thread.Sleep(50);
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* 忽略 */ }
        }
    }
}