using System.Net;

namespace ProxyYARP.Proxy.Tcp;

public interface IL4LoadBalancerPolicy
{
    string Name { get; }
    L4ProxyDestination? PickDestination(IReadOnlyList<L4ProxyDestination> availableDestinations, EndPoint? clientEndPoint);
}

public class RoundRobinTcpPolicy : IL4LoadBalancerPolicy
{
    public string Name => "RoundRobin";
    private int _index = -1;

    public L4ProxyDestination? PickDestination(IReadOnlyList<L4ProxyDestination> availableDestinations, EndPoint? clientEndPoint)
    {
        if (availableDestinations.Count == 0) return null;
        
        var nextIndex = Interlocked.Increment(ref _index);
        // Ensure positive index
        if (nextIndex < 0)
        {
            nextIndex = 0;
            Interlocked.Exchange(ref _index, 0);
        }
        
        return availableDestinations[nextIndex % availableDestinations.Count];
    }
}

public class RandomTcpPolicy : IL4LoadBalancerPolicy
{
    public string Name => "Random";

    public L4ProxyDestination? PickDestination(IReadOnlyList<L4ProxyDestination> availableDestinations, EndPoint? clientEndPoint)
    {
        if (availableDestinations.Count == 0) return null;
        var index = Random.Shared.Next(availableDestinations.Count);
        return availableDestinations[index];
    }
}

public class IPHashTcpPolicy : IL4LoadBalancerPolicy
{
    public string Name => "IPHash";

    public L4ProxyDestination? PickDestination(IReadOnlyList<L4ProxyDestination> availableDestinations, EndPoint? clientEndPoint)
    {
        if (availableDestinations.Count == 0) return null;
        if (clientEndPoint is IPEndPoint ipEndPoint)
        {
            // Use IP address hash to select destination (ignoring port)
            var hash = ipEndPoint.Address.GetHashCode();
            var index = (int)((uint)hash % (uint)availableDestinations.Count);
            return availableDestinations[index];
        }
        
        // Fallback to random if not IPEndPoint
        return availableDestinations[Random.Shared.Next(availableDestinations.Count)];
    }
}

public class WeightedRoundRobinTcpPolicy : IL4LoadBalancerPolicy
{
    public string Name => "WeightedRoundRobin";
    private int _index = -1;

    public L4ProxyDestination? PickDestination(IReadOnlyList<L4ProxyDestination> availableDestinations, EndPoint? clientEndPoint)
    {
        if (availableDestinations.Count == 0) return null;

        int totalWeight = 0;
        foreach (var dest in availableDestinations)
        {
            totalWeight += dest.Weight > 0 ? dest.Weight : 1;
        }

        var nextIndex = Interlocked.Increment(ref _index);
        if (nextIndex < 0)
        {
            nextIndex = 0;
            Interlocked.Exchange(ref _index, 0);
        }

        int targetWeight = nextIndex % totalWeight;
        int currentWeightSum = 0;

        foreach (var dest in availableDestinations)
        {
            currentWeightSum += dest.Weight > 0 ? dest.Weight : 1;
            if (targetWeight < currentWeightSum)
            {
                return dest;
            }
        }

        return availableDestinations[0]; // fallback
    }
}

public static class L4ConnectionTracker
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _activeConnections = new();

    public static void Increment(string targetHost, int targetPort)
    {
        var key = $"{targetHost}:{targetPort}";
        _activeConnections.AddOrUpdate(key, 1, (_, count) => count + 1);
    }

    public static void Decrement(string targetHost, int targetPort)
    {
        var key = $"{targetHost}:{targetPort}";
        _activeConnections.AddOrUpdate(key, 0, (_, count) => count > 0 ? count - 1 : 0);
    }

    public static long GetActiveConnections(string targetHost, int targetPort)
    {
        var key = $"{targetHost}:{targetPort}";
        return _activeConnections.TryGetValue(key, out var count) ? count : 0;
    }
}

public class LeastRequestsTcpPolicy : IL4LoadBalancerPolicy
{
    public string Name => "LeastRequests";

    public L4ProxyDestination? PickDestination(IReadOnlyList<L4ProxyDestination> availableDestinations, EndPoint? clientEndPoint)
    {
        if (availableDestinations.Count == 0) return null;
        
        L4ProxyDestination? bestDest = null;
        long minConns = long.MaxValue;

        foreach (var dest in availableDestinations)
        {
            var conns = L4ConnectionTracker.GetActiveConnections(dest.TargetHost, dest.TargetPort);
            if (conns < minConns)
            {
                minConns = conns;
                bestDest = dest;
            }
        }

        return bestDest ?? availableDestinations[0];
    }
}

public class PowerOfTwoChoicesTcpPolicy : IL4LoadBalancerPolicy
{
    public string Name => "PowerOfTwoChoices";

    public L4ProxyDestination? PickDestination(IReadOnlyList<L4ProxyDestination> availableDestinations, EndPoint? clientEndPoint)
    {
        if (availableDestinations.Count == 0) return null;
        if (availableDestinations.Count == 1) return availableDestinations[0];

        // Pick two random indices
        int idx1 = Random.Shared.Next(availableDestinations.Count);
        int idx2 = Random.Shared.Next(availableDestinations.Count);
        
        // Ensure they are different if possible
        if (idx1 == idx2)
        {
            idx2 = (idx2 + 1) % availableDestinations.Count;
        }

        var dest1 = availableDestinations[idx1];
        var dest2 = availableDestinations[idx2];

        var conns1 = L4ConnectionTracker.GetActiveConnections(dest1.TargetHost, dest1.TargetPort);
        var conns2 = L4ConnectionTracker.GetActiveConnections(dest2.TargetHost, dest2.TargetPort);

        return conns1 <= conns2 ? dest1 : dest2;
    }
}

public class FirstAlphabeticalTcpPolicy : IL4LoadBalancerPolicy
{
    public string Name => "FirstAlphabetical";

    public L4ProxyDestination? PickDestination(IReadOnlyList<L4ProxyDestination> availableDestinations, EndPoint? clientEndPoint)
    {
        if (availableDestinations.Count == 0) return null;
        
        return availableDestinations
            .OrderBy(d => $"{d.TargetHost}:{d.TargetPort}")
            .First();
    }
}

public static class L4LoadBalancerPolicyFactory
{
    private static readonly Dictionary<string, IL4LoadBalancerPolicy> _policies = new(StringComparer.OrdinalIgnoreCase)
    {
        { "RoundRobin", new RoundRobinTcpPolicy() },
        { "Random", new RandomTcpPolicy() },
        { "IPHash", new IPHashTcpPolicy() },
        { "WeightedRoundRobin", new WeightedRoundRobinTcpPolicy() },
        { "LeastRequests", new LeastRequestsTcpPolicy() },
        { "PowerOfTwoChoices", new PowerOfTwoChoicesTcpPolicy() },
        { "FirstAlphabetical", new FirstAlphabeticalTcpPolicy() }
    };

    public static IL4LoadBalancerPolicy GetPolicy(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return _policies["RoundRobin"];
            
        if (_policies.TryGetValue(name, out var policy))
            return policy;
            
        return _policies["RoundRobin"]; // 默认回退到轮询
    }
}
