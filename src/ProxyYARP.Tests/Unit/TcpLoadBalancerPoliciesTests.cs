using System.Net;
using ProxyYARP.Data.Models;
using ProxyYARP.Proxy.Tcp;
using Xunit;

namespace ProxyYARP.Tests.Unit;

public class TcpLoadBalancerPoliciesTests
{
    [Fact]
    public void RoundRobinTcpPolicy_Should_Pick_Destinations_In_Order()
    {
        // Arrange
        var policy = new RoundRobinTcpPolicy();
        var destinations = new List<L4ProxyDestination>
        {
            new L4ProxyDestination { TargetHost = "host1", TargetPort = 80 },
            new L4ProxyDestination { TargetHost = "host2", TargetPort = 80 },
            new L4ProxyDestination { TargetHost = "host3", TargetPort = 80 }
        };
        var dummyEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

        // Act
        var picked1 = policy.PickDestination(destinations, dummyEndpoint);
        var picked2 = policy.PickDestination(destinations, dummyEndpoint);
        var picked3 = policy.PickDestination(destinations, dummyEndpoint);
        var picked4 = policy.PickDestination(destinations, dummyEndpoint);

        // Assert
        Assert.NotNull(picked1);
        Assert.NotNull(picked2);
        Assert.NotNull(picked3);
        Assert.NotNull(picked4);

        Assert.Equal("host1", picked1.TargetHost);
        Assert.Equal("host2", picked2.TargetHost);
        Assert.Equal("host3", picked3.TargetHost);
        Assert.Equal("host1", picked4.TargetHost); // loop back
    }

    [Fact]
    public void RoundRobinTcpPolicy_Should_Return_Null_If_Destinations_Empty()
    {
        // Arrange
        var policy = new RoundRobinTcpPolicy();
        var destinations = new List<L4ProxyDestination>();
        var dummyEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

        // Act
        var picked = policy.PickDestination(destinations, dummyEndpoint);

        // Assert
        Assert.Null(picked);
    }

    [Fact]
    public void RandomTcpPolicy_Should_Pick_A_Destination()
    {
        // Arrange
        var policy = new RandomTcpPolicy();
        var destinations = new List<L4ProxyDestination>
        {
            new L4ProxyDestination { TargetHost = "host1", TargetPort = 80 },
            new L4ProxyDestination { TargetHost = "host2", TargetPort = 80 }
        };
        var dummyEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

        // Act
        var picked = policy.PickDestination(destinations, dummyEndpoint);

        // Assert
        Assert.NotNull(picked);
        Assert.Contains(picked.TargetHost, new[] { "host1", "host2" });
    }

    [Fact]
    public void IPHashTcpPolicy_Should_Pick_Same_Destination_For_Same_IP()
    {
        // Arrange
        var policy = new IPHashTcpPolicy();
        var destinations = new List<L4ProxyDestination>
        {
            new L4ProxyDestination { TargetHost = "host1" },
            new L4ProxyDestination { TargetHost = "host2" },
            new L4ProxyDestination { TargetHost = "host3" }
        };
        var client1 = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 1234);
        var client2 = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 5678); // different port, same IP
        var client3 = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1234);

        // Act
        var picked1 = policy.PickDestination(destinations, client1);
        var picked2 = policy.PickDestination(destinations, client2);
        var picked3 = policy.PickDestination(destinations, client3);

        // Assert
        Assert.NotNull(picked1);
        Assert.Equal(picked1.TargetHost, picked2?.TargetHost); // Same IP -> same dest
    }

    [Fact]
    public void WeightedRoundRobinTcpPolicy_Should_Respect_Weights()
    {
        // Arrange
        var policy = new WeightedRoundRobinTcpPolicy();
        var destinations = new List<L4ProxyDestination>
        {
            new L4ProxyDestination { TargetHost = "host1", Weight = 3 },
            new L4ProxyDestination { TargetHost = "host2", Weight = 1 }
        };
        var dummyEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

        // Act
        var results = new List<string?>();
        for (int i = 0; i < 4; i++)
        {
            results.Add(policy.PickDestination(destinations, dummyEndpoint)?.TargetHost);
        }

        // Assert
        Assert.Equal(4, results.Count);
        Assert.Equal(3, results.Count(x => x == "host1"));
        Assert.Equal(1, results.Count(x => x == "host2"));
    }
    [Fact]
    public void FirstAlphabeticalTcpPolicy_Should_Pick_Alphabetically_First()
    {
        var policy = new FirstAlphabeticalTcpPolicy();
        var destinations = new List<L4ProxyDestination>
        {
            new L4ProxyDestination { TargetHost = "zebra" },
            new L4ProxyDestination { TargetHost = "alpha" },
            new L4ProxyDestination { TargetHost = "beta" }
        };

        var picked = policy.PickDestination(destinations, null);

        Assert.NotNull(picked);
        Assert.Equal("alpha", picked.TargetHost);
    }

    [Fact]
    public void LeastRequestsTcpPolicy_Should_Pick_Destination_With_Least_Connections()
    {
        var policy = new LeastRequestsTcpPolicy();
        var destinations = new List<L4ProxyDestination>
        {
            new L4ProxyDestination { TargetHost = "host1", TargetPort = 1111 },
            new L4ProxyDestination { TargetHost = "host2", TargetPort = 2222 }
        };

        // Simulate active connections
        L4ConnectionTracker.Increment("host1", 1111);
        L4ConnectionTracker.Increment("host1", 1111);
        L4ConnectionTracker.Increment("host2", 2222);

        var picked = policy.PickDestination(destinations, null);

        Assert.NotNull(picked);
        Assert.Equal("host2", picked.TargetHost); // host2 has 1 connection, host1 has 2

        // Cleanup
        L4ConnectionTracker.Decrement("host1", 1111);
        L4ConnectionTracker.Decrement("host1", 1111);
        L4ConnectionTracker.Decrement("host2", 2222);
    }

    [Fact]
    public void PowerOfTwoChoicesTcpPolicy_Should_Pick_Destination_With_Fewer_Connections_Between_Two_Choices()
    {
        var policy = new PowerOfTwoChoicesTcpPolicy();
        var destinations = new List<L4ProxyDestination>
        {
            new L4ProxyDestination { TargetHost = "host1", TargetPort = 1111 },
            new L4ProxyDestination { TargetHost = "host2", TargetPort = 2222 }
        };

        // Simulate active connections
        L4ConnectionTracker.Increment("host1", 1111);
        L4ConnectionTracker.Increment("host1", 1111);
        // host2 has 0 connections

        var picked = policy.PickDestination(destinations, null);

        Assert.NotNull(picked);
        Assert.Equal("host2", picked.TargetHost); // Always picks host2 between host1 and host2 because host2 has 0 conns

        // Cleanup
        L4ConnectionTracker.Decrement("host1", 1111);
        L4ConnectionTracker.Decrement("host1", 1111);
    }
}
