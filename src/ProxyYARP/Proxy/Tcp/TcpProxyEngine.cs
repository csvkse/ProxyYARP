using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProxyYARP.Proxy.Tcp;

/// <summary>
/// 高性能 TCP 代理引擎，独立于 Kestrel 的后台服务。
/// 支持动态监听端口，基于 System.Net.Sockets 和 ArrayPool 的零分配转发。
/// </summary>
public class TcpProxyEngine : BackgroundService
{
    private readonly L4ProxyConfigProvider _configProvider;
    private readonly ILogger<TcpProxyEngine> _logger;

    // 记录正在运行的监听器
    private readonly ConcurrentDictionary<int, ListenerContext> _activeListeners = new();

    // 发出全局停止信号
    private CancellationTokenSource? _globalCts;

    public TcpProxyEngine(L4ProxyConfigProvider configProvider, ILogger<TcpProxyEngine> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
        _configProvider.OnConfigChanged += ReloadConfig;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _globalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _logger.LogInformation("[TCP Proxy] Engine started.");

        ReloadConfig(); // 启动时应用当前配置
        // 阻塞直到宿主关闭
        try
        {
            await Task.Delay(Timeout.Infinite, _globalCts.Token);
        }
        catch (TaskCanceledException)
        {
            // Ignore
        }

        StopAllListeners();
        _logger.LogInformation("[TCP Proxy] Engine stopped.");
    }

    private void ReloadConfig()
    {
        if (_globalCts == null || _globalCts.IsCancellationRequested) return;

        var currentRoutes = _configProvider.GetRoutes();
        var newPortMap = currentRoutes.GroupBy(r => r.ListenPort).ToDictionary(g => g.Key, g => g.First());

        // 1. 停止不再需要的监听器，热更新存活监听器的目标配置
        foreach (var (port, ctx) in _activeListeners)
        {
            if (!newPortMap.TryGetValue(port, out var newRoute))
            {
                _logger.LogInformation("[TCP Proxy] Stopping listener on port {Port}", port);
                ctx.Cts.Cancel();
                if (ctx.ListenerSocket != null)
                {
                    try { ctx.ListenerSocket.Close(); } catch { }
                }
                _activeListeners.TryRemove(port, out _);
            }
            else
            {
                // 热更新 Route 对象（包含新的 Destinations 和 Policy），不重启监听器
                ctx.Route = newRoute;
            }
        }

        // 2. 启动新的监听器
        foreach (var (port, route) in newPortMap)
        {
            if (!_activeListeners.ContainsKey(port))
            {
                StartListener(route);
            }
        }
    }

    private void StartListener(L4ProxyRoute route)
    {
        var ctx = new ListenerContext
        {
            Route = route,
            Cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts!.Token)
        };

        try
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, route.ListenPort));
            listener.Listen(1000);
            ctx.ListenerSocket = listener;

            _activeListeners[route.ListenPort] = ctx;
            _logger.LogInformation("[TCP Proxy] Started listening on port {Port} with {Count} destinations",
                route.ListenPort, route.Destinations.Count);

            // 后台接受连接
            _ = AcceptLoopAsync(ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TCP Proxy] Failed to start listener on port {Port}", route.ListenPort);
        }
    }

    private async Task AcceptLoopAsync(ListenerContext ctx)
    {
        var token = ctx.Cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await ctx.ListenerSocket!.AcceptAsync(token);
                // 不等待，交给后台处理单条连接
                _ = HandleConnectionAsync(clientSocket, ctx, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    _logger.LogWarning(ex, "[TCP Proxy] Accept error on port {Port}", ctx.Route.ListenPort);
            }
        }
    }

    private async Task HandleConnectionAsync(Socket clientSocket, ListenerContext ctx, CancellationToken token)
    {
        Socket? targetSocket = null;
        try
        {
            clientSocket.NoDelay = true;

            // 获取最新的路由配置
            var route = ctx.Route;
            var policy = route.Policy ?? L4LoadBalancerPolicyFactory.GetPolicy(route.LoadBalancingPolicy);

            // 获取可用的节点列表（如果做被动健康检查，这里可以过滤掉刚失败的节点）
            var availableDests = route.Destinations.ToList();
            L4ProxyDestination? connectedDest = null;

            while (availableDests.Count > 0 && !token.IsCancellationRequested)
            {
                var dest = policy.PickDestination(availableDests, clientSocket.RemoteEndPoint);
                if (dest == null) break;

                targetSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

                try
                {
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(5)); // 连接超时短一点，快速 fallback

                    await targetSocket.ConnectAsync(dest.TargetHost, dest.TargetPort, connectCts.Token);
                    connectedDest = dest;
                    break; // 连接成功
                }
                catch (Exception)
                {
                    // 连接失败，关闭 targetSocket 并剔除该节点，尝试下一个
                    SafeClose(targetSocket);
                    targetSocket = null;
                    availableDests.Remove(dest);
                    _logger.LogWarning("[TCP Proxy] Port {Port}: Failed to connect to {Host}:{DestPort}, trying next...",
                        route.ListenPort, dest.TargetHost, dest.TargetPort);
                }
            }

            if (targetSocket == null || connectedDest == null)
            {
                _logger.LogWarning("[TCP Proxy] Port {Port}: No available destinations to connect to.", route.ListenPort);
                return;
            }

            L4ConnectionTracker.Increment(connectedDest.TargetHost, connectedDest.TargetPort);
            try
            {
                // 双向零分配转发
                var clientToTarget = PumpAsync(clientSocket, targetSocket, token);
                var targetToClient = PumpAsync(targetSocket, clientSocket, token);

                // 任何一端断开或出错，就结束整个连接
                await Task.WhenAny(clientToTarget, targetToClient);
            }
            finally
            {
                L4ConnectionTracker.Decrement(connectedDest.TargetHost, connectedDest.TargetPort);
            }
        }
        catch (Exception)
        {
            // LogDebug or Ignore
        }
        finally
        {
            SafeClose(clientSocket);
            SafeClose(targetSocket);
        }
    }

    /// <summary>
    /// 基于 ArrayPool 和原生 Memory 扩展的高性能双向 Pump
    /// </summary>
    private async Task PumpAsync(Socket input, Socket output, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920); // 80KB buffer
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await input.ReceiveAsync(buffer, SocketFlags.None, token);
                if (read == 0) break; // 正常关闭

                int sent = 0;
                while (sent < read && !token.IsCancellationRequested)
                {
                    sent += await output.SendAsync(buffer.AsMemory(sent, read - sent), SocketFlags.None, token);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void StopAllListeners()
    {
        foreach (var ctx in _activeListeners.Values)
        {
            ctx.Cts.Cancel();
            SafeClose(ctx.ListenerSocket);
        }
        _activeListeners.Clear();
    }

    private static void SafeClose(Socket? socket)
    {
        if (socket == null) return;
        try { socket.Shutdown(SocketShutdown.Both); } catch { }
        try { socket.Close(); } catch { }
    }

    private class ListenerContext
    {
        public L4ProxyRoute Route { get; set; } = null!;
        public Socket? ListenerSocket { get; set; }
        public CancellationTokenSource Cts { get; set; } = null!;
    }
}
