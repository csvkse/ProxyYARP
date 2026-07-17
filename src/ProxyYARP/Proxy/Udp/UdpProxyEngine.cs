using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProxyYARP.Proxy.Tcp;

namespace ProxyYARP.Proxy.Udp;

/// <summary>
/// UDP L4 代理引擎，提供高并发包转发和 NAT Session 跟踪。
/// </summary>
public class UdpProxyEngine : BackgroundService
{
    private readonly L4ProxyConfigProvider _configProvider;
    private readonly ILogger<UdpProxyEngine> _logger;
    private readonly ConcurrentDictionary<int, UdpListenerContext> _listeners = new();

    public UdpProxyEngine(L4ProxyConfigProvider configProvider, ILogger<UdpProxyEngine> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _configProvider.OnConfigChanged += ReloadConfig;
        ReloadConfig();
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (TaskCanceledException) { }
        finally
        {
            _configProvider.OnConfigChanged -= ReloadConfig;
            foreach (var ctx in _listeners.Values) ctx.Dispose();
            _listeners.Clear();
        }
    }

    private void ReloadConfig()
    {
        var allRoutes = _configProvider.GetRoutes()
            .Where(r => string.Equals(r.Protocol, "UDP", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var currentPorts = allRoutes.Select(r => r.ListenPort).ToHashSet();
        foreach (var port in _listeners.Keys)
        {
            if (!currentPorts.Contains(port) && _listeners.TryRemove(port, out var old))
            {
                _logger.LogInformation("Stopping UDP listener on port {Port}", port);
                old.Dispose();
            }
        }
        foreach (var route in allRoutes)
        {
            if (!_listeners.TryGetValue(route.ListenPort, out var ctx))
            {
                _logger.LogInformation("Starting UDP listener on port {Port}", route.ListenPort);
                ctx = new UdpListenerContext(route, _logger);
                _listeners[route.ListenPort] = ctx;
                ctx.Start();
            }
            else ctx.UpdateRoute(route);
        }
    }
}

public class UdpListenerContext : IDisposable
{
    private L4ProxyRoute _route;
    private readonly ILogger _logger;
    private readonly Socket _listenerSocket;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<EndPoint, UdpSession> _sessions = new();
    private readonly Timer _pruningTimer;

    public UdpListenerContext(L4ProxyRoute route, ILogger logger)
    {
        _route = route;
        _logger = logger;
        _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _listenerSocket.Bind(new IPEndPoint(IPAddress.Any, route.ListenPort));
        _pruningTimer = new Timer(PruneIdleSessions, null, 10000, 10000);
    }

    public void UpdateRoute(L4ProxyRoute newRoute) => _route = newRoute;

    public void Start() => Task.Run(ReceiveLoopAsync, _cts.Token);

    private async Task ReceiveLoopAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var result = await _listenerSocket.ReceiveFromAsync(
                    new ArraySegment<byte>(buffer), SocketFlags.None,
                    new IPEndPoint(IPAddress.Any, 0));

                if (result.ReceivedBytes <= 0) continue;

                var clientEp = result.RemoteEndPoint;
                if (!_sessions.TryGetValue(clientEp, out var session))
                {
                    var policy = _route.Policy ?? L4LoadBalancerPolicyFactory.GetPolicy(_route.LoadBalancingPolicy);
                    var dest = policy.PickDestination(_route.Destinations, clientEp);
                    if (dest == null)
                    {
                        _logger.LogWarning("No UDP destinations for port {Port}", _route.ListenPort);
                        continue;
                    }

                    IPAddress backendAddress;
                    try
                    {
                        var addresses = await Dns.GetHostAddressesAsync(dest.TargetHost, _cts.Token);
                        if (addresses.Length == 0)
                        {
                            _logger.LogWarning("Could not resolve UDP target host {Host} for port {Port}", dest.TargetHost, _route.ListenPort);
                            continue;
                        }
                        backendAddress = addresses[0];
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve UDP target host {Host} for port {Port}", dest.TargetHost, _route.ListenPort);
                        continue;
                    }

                    var newSession = new UdpSession(clientEp, backendAddress, dest.TargetPort, _listenerSocket, _route, _logger);
                    if (_sessions.TryAdd(clientEp, newSession))
                    {
                        newSession.StartBackendReceiveLoop();
                        session = newSession;
                    }
                    else
                    {
                        newSession.Dispose();
                        _sessions.TryGetValue(clientEp, out session);
                    }
                }

                if (session == null) continue;

                session.LastActiveAt = DateTime.UtcNow;
                try
                {
                    await session.BackendSocket.SendToAsync(
                        new ArraySegment<byte>(buffer, 0, result.ReceivedBytes),
                        SocketFlags.None, session.BackendEndPoint);
                }
                catch (Exception ex) { _logger.LogError(ex, "Error sending to backend"); }
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
        {
            _logger.LogError(ex, "UDP Listener Error on port {Port}", _route.ListenPort);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private void PruneIdleSessions(object? _)
    {
        var timeout = TimeSpan.FromSeconds(_route.IdleTimeoutSeconds > 0 ? _route.IdleTimeoutSeconds : 60);
        var now = DateTime.UtcNow;
        foreach (var kvp in _sessions)
        {
            if (now - kvp.Value.LastActiveAt > timeout && _sessions.TryRemove(kvp.Key, out var s))
            {
                _logger.LogDebug("Pruned idle UDP session {Client}", kvp.Key);
                s.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _pruningTimer.Dispose();
        foreach (var s in _sessions.Values) s.Dispose();
        _sessions.Clear();
        try { _listenerSocket.Dispose(); } catch { }
    }
}

public class UdpSession : IDisposable
{
    public EndPoint ClientEndPoint { get; }
    public Socket BackendSocket { get; }
    public EndPoint BackendEndPoint { get; }
    public DateTime LastActiveAt { get; set; }

    private readonly Socket _frontendSocket;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    public UdpSession(EndPoint clientEp, IPAddress backendAddress, int backendPort, Socket frontendSocket, L4ProxyRoute route, ILogger logger)
    {
        ClientEndPoint = clientEp;
        _frontendSocket = frontendSocket;
        _logger = logger;
        LastActiveAt = DateTime.UtcNow;
        BackendSocket = new Socket(backendAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        BackendSocket.Bind(new IPEndPoint(backendAddress.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
        BackendEndPoint = new IPEndPoint(backendAddress, backendPort);
        L4ConnectionTracker.Increment(backendAddress.ToString(), backendPort);
    }

    public void StartBackendReceiveLoop()
    {
        Task.Run(async () =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var result = await BackendSocket.ReceiveFromAsync(
                        new ArraySegment<byte>(buffer), SocketFlags.None,
                        new IPEndPoint(IPAddress.Any, 0));
                    if (result.ReceivedBytes <= 0) continue;
                    LastActiveAt = DateTime.UtcNow;
                    try
                    {
                        await _frontendSocket.SendToAsync(
                            new ArraySegment<byte>(buffer, 0, result.ReceivedBytes),
                            SocketFlags.None, ClientEndPoint);
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Error forwarding to client {Client}", ClientEndPoint); }
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
            { _logger.LogError(ex, "UDP Session Receive Error"); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }, _cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        var ep = BackendEndPoint as IPEndPoint;
        if (ep != null) L4ConnectionTracker.Decrement(ep.Address.ToString(), ep.Port);
        try { BackendSocket.Dispose(); } catch { }
    }
}