using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProxyYARP.Proxy.Tcp;
using Xunit;

namespace ProxyYARP.Tests.Unit;

public class TcpProxyModuleTests
{
    [Fact]
    public void RegisterServices_Should_Register_Required_Services()
    {
        // Arrange
        var services = new ServiceCollection();
        var module = new TcpProxyModule();

        // Act
        module.RegisterServices(services);

        // Assert
        Assert.Contains(services, s => s.ServiceType == typeof(L4ProxyConfigProvider) && s.Lifetime == ServiceLifetime.Singleton);
        
        // Hosted services are registered as IHostedService
        Assert.Contains(services, s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(TcpProxyEngine));
    }
}