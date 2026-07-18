namespace ProxyYARP.Data.Db;

/// <summary>按配置创建数据库 Provider</summary>
public static class DatabaseProviderFactory
{
    /// <param name="provider">sqlite | pgsql（null/空 = sqlite）</param>
    /// <param name="connectionString">连接字符串（sqlite 允许空 = 默认路径）</param>
    public static IDbProvider Create(string? provider, string? connectionString)
    {
        return (provider ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "sqlite" => new SqliteDbProvider(connectionString),
            var other => throw new ArgumentException(
                $"未知数据库 Provider: '{other}'（支持: sqlite, pgsql）", nameof(provider))
        };
    }
}
