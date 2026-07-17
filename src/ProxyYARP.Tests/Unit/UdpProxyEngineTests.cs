using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
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
        var configService = new L4ConfigService(new L4RouteRepository(), new L4DestinationRepository());
        var configProviderLogger = new NullLogger<L4ProxyConfigProvider>();
        var configProvider = new L4ProxyConfigProvider(configService, configProviderLogger);
        
        var engineLogger = new NullLogger<UdpProxyEngine>();
        
        // Act
        var engine = new UdpProxyEngine(configProvider, engineLogger);

        // Assert
        Assert.NotNull(engine);
    }
}