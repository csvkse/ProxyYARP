using System.Data;
using Microsoft.Data.Sqlite;

namespace ProxyYARP.Data.Db;

/// <summary>
/// SQLite 连接工厂，支持 AOT 编译
/// </summary>
public static class DbContext
{
    public static string ConnectionString { get; private set; } =
        $"Data Source={AppContext.BaseDirectory}/proxy.db;Cache=Shared;";

    public static void Configure(string? dbPath)
    {
        if (!string.IsNullOrWhiteSpace(dbPath))
        {
            // 支持相对路径和绝对路径
            var fullPath = Path.IsPathRooted(dbPath)
                ? dbPath
                : Path.Combine(AppContext.BaseDirectory, dbPath);
            ConnectionString = $"Data Source={fullPath};Cache=Shared;";
        }
    }

    public static IDbConnection GetConnection()
    {
        return new SqliteConnection(ConnectionString);
    }
}
