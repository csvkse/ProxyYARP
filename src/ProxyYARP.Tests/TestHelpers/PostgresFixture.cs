using Testcontainers.PostgreSql;

namespace ProxyYARP.Tests.TestHelpers;

/// <summary>
/// PostgreSQL 容器 Fixture（postgres:18-alpine）
/// Docker 不可用时 Available=false，测试用 Skip.IfNot 跳过
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Docker 是否可用（不可用则所有 pgsql 测试跳过）</summary>
    public bool Available { get; private set; }

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL 容器未启动");

    public async Task InitializeAsync()
    {
        Available = await IsDockerRunning();
        if (!Available) return;

        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    private static async Task<bool> IsDockerRunning()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}