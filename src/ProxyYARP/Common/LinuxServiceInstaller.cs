using System.Runtime.InteropServices;
using System.Text;

namespace ProxyYARP.Common;

/// <summary>Linux systemd 服务安装/卸载（参照 ServerLatency 实现）</summary>
public static class LinuxServiceInstaller
{
    private const string ServiceName = "proxyyarp";

    public static async Task HandleInstallAsync(string[] args)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.WriteLine("[ERROR] --install is only supported on Linux.");
            return;
        }

        var execPath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var workDir = Path.GetDirectoryName(execPath) ?? AppContext.BaseDirectory;

        // 重新组装启动参数（去掉 --install）
        var startArgs = string.Join(" ", args.Where(a => a != "--install" && a != "-i")
                                            .Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

        var unit = $"""
[Unit]
Description=ProxyYARP - YARP Reverse Proxy with Web Management
After=network.target

[Service]
Type=simple
WorkingDirectory={workDir}
ExecStart={execPath} {startArgs}
Restart=always
RestartSec=5
StandardOutput=journal
StandardError=journal
SyslogIdentifier={ServiceName}

[Install]
WantedBy=multi-user.target
""";

        var unitPath = $"/etc/systemd/system/{ServiceName}.service";
        await File.WriteAllTextAsync(unitPath, unit, Encoding.UTF8);

        Console.WriteLine($"[INSTALL] Written: {unitPath}");
        await RunShellAsync("systemctl daemon-reload");
        await RunShellAsync($"systemctl enable {ServiceName}");
        await RunShellAsync($"systemctl start {ServiceName}");
        Console.WriteLine($"[INSTALL] Service '{ServiceName}' started.");
        Console.WriteLine($"  Status : sudo systemctl status {ServiceName}");
        Console.WriteLine($"  Logs   : journalctl -u {ServiceName} -f");
        Console.WriteLine($"  Stop   : sudo systemctl stop {ServiceName}");
    }

    public static async Task HandleUninstallAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.WriteLine("[ERROR] --uninstall is only supported on Linux.");
            return;
        }

        await RunShellAsync($"systemctl stop {ServiceName}");
        await RunShellAsync($"systemctl disable {ServiceName}");

        var unitPath = $"/etc/systemd/system/{ServiceName}.service";
        if (File.Exists(unitPath)) File.Delete(unitPath);

        await RunShellAsync("systemctl daemon-reload");
        Console.WriteLine($"[UNINSTALL] Service '{ServiceName}' removed.");
    }

    private static async Task RunShellAsync(string cmd)
    {
        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{cmd}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        if (proc != null)
        {
            await proc.WaitForExitAsync();
            Console.WriteLine($"  $ {cmd} → exit {proc.ExitCode}");
        }
    }
}
