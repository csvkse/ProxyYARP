using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Services;
using ProxyYARP.Tests.TestHelpers;
using Xunit;

namespace ProxyYARP.Tests.Integration;

/// <summary>
/// TCP 代理引擎端到端转发测试
/// 链路：测试客户端 -> TcpProxyEngine 监听端口 -> 本地 echo server
/// 使用动态空闲端口 + 轮询等待，避免固定端口和固定 sleep 导致的不稳定
/// </summary>
public class TcpProxyEngineTests : IClassFixture<ProxyYarpWebFactory>
{
    private readonly ProxyYarpWebFactory _factory;

    public TcpProxyEngineTests(ProxyYarpWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TcpProxyEngine_Should_Forward_Traffic_To_Backend()
    {
        // 确保宿主（含 TcpProxyEngine 后台服务）已启动
        _ = _factory.Services;

        var echoPort = GetFreePort();
        var proxyPort = GetFreePort();

        using var echoCts = new CancellationTokenSource();
        var echoTask = RunEchoServerAsync(echoPort, echoCts.Token);

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<L4ConfigService>();
        var route = svc.CreateRoute(
            $"it-tcp-{Guid.NewGuid():N}",
            proxyPort,
            "RoundRobin",
            new List<L4ProxyDestinationEntity>
            {
                new() { TargetHost = "127.0.0.1", TargetPort = echoPort, Weight = 1, IsEnabled = true }
            });

        try
        {
            using var client = await ConnectWithRetryAsync(proxyPort, TimeSpan.FromSeconds(10));

            var payload = Guid.NewGuid().ToByteArray();
            var stream = client.GetStream();
            await stream.WriteAsync(payload);

            var buffer = new byte[payload.Length];
            await ReadExactAsync(stream, buffer, TimeSpan.FromSeconds(5));

            buffer.Should().BeEquivalentTo(payload, "经过 TCP 代理转发后应收到 echo server 原样返回的数据");
        }
        finally
        {
            svc.DeleteRoute(route.Id);
            echoCts.Cancel();
            try { await echoTask; } catch { /* 取消即退出 */ }
        }
    }

    /// <summary>绑定端口 0 让系统分配空闲端口，立即释放后返回端口号</summary>
    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>简单 echo server：接受连接并把收到的数据原样写回</summary>
    private static async Task RunEchoServerAsync(int port, CancellationToken token)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        var buf = new byte[8192];
                        var stream = client.GetStream();
                        try
                        {
                            int read;
                            while ((read = await stream.ReadAsync(buf, token)) > 0)
                                await stream.WriteAsync(buf.AsMemory(0, read), token);
                        }
                        catch { /* 连接关闭或取消 */ }
                    }
                }, token);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    /// <summary>在超时时间内反复尝试连接（等待代理监听器就绪）</summary>
    private static async Task<TcpClient> ConnectWithRetryAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                return client;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cts.Token);
            if (read == 0) break;
            total += read;
        }
    }
}
