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

        // 重新组装启动参数（去掉 --install 以及敏感/不应出现在 unit 文件中的 Key）
        var filteredArgs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--install" || a == "-i") continue;
            // 不将 admin key 写入 unit 文件，避免泄露与 shell 注入
            if (a == "-k" || a == "--Key")
            {
                i++; // skip value
                continue;
            }
            filteredArgs.Add(a);
        }
        var startArgs = string.Join(" ", filteredArgs.Select(EscapeSystemdExecArg));

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
Environment=ACCESS_KEY=%PLACEHOLDER_SET_VIA_SECURE_MECHANISM%

[Install]
WantedBy=multi-user.target
""";

        var unitPath = $"/etc/systemd/system/{ServiceName}.service";
        await File.WriteAllTextAsync(unitPath, unit, Encoding.UTF8);

        Console.WriteLine($"[INSTALL] Written: {unitPath}");
        Console.WriteLine("[WARNING] Admin key was NOT embedded in the unit file. Set it via a secure mechanism (e.g. systemd EnvironmentFile).");
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

    private static string EscapeSystemdExecArg(string arg)
    {
        // systemd ExecStart argument escaping inside double quotes: \" and \\\\
        var escaped = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return arg.Contains(' ') || arg.Contains('\"') || arg.Contains('\\') || arg.Contains('\t') || arg.Contains('\n')
            ? $"\"{escaped}\""
            : escaped;
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
