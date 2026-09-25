namespace ProxyYARP.Auth;

/// <summary>
/// 管理端路径前缀的统一定义与归一化。
/// 三处消费方（Program.cs 挂载端点、ApiKeyMiddleware 鉴权、NodeHeartbeatService 生成节点链接）
/// 必须走同一套规则，否则会出现「界面能开但 API 401」或「节点面板链接 404」这类割裂问题。
/// </summary>
public static class ManagementPath
{
    /// <summary>默认管理端前缀。把管理界面挪到非根路径，避免与业务 catch-all 路由冲突。</summary>
    public const string Default = "/_proxy";

    /// <summary>
    /// 从配置解析出归一化后的管理端前缀。
    /// 规则：未配置 → 默认 /_proxy；显式空串 → 根路径（返回 ""）；其余补前导 '/' 并去掉尾部 '/'。
    /// </summary>
    public static string Resolve(IConfiguration? config)
    {
        var raw = config?["Management:PathBase"] ?? Default;

        // 显式清空：MANAGEMENT_PATH="" 让管理界面回到根路径（兼容旧行为）
        if (string.Equals(raw, string.Empty, StringComparison.Ordinal))
            return "";

        if (string.IsNullOrWhiteSpace(raw))
            return Default;

        var path = raw.Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        if (path.Length > 1 && path.EndsWith('/')) path = path.TrimEnd('/');

        return path;
    }
}
