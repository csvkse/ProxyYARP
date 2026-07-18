using System.Data;
using ProxyYARP.Data.Db;

namespace ProxyYARP.Data.Repositories;

/// <summary>Repository 基类，通过 IDbProvider 获取数据库连接</summary>
public abstract class BaseRepository<T> where T : class, new()
{
    private readonly IDbProvider _provider;

    protected BaseRepository(IDbProvider provider) => _provider = provider;

    protected void WithConnection(Action<IDbConnection> action)
    {
        using var conn = _provider.CreateConnection();
        action(conn);
    }

    protected TResult WithConnection<TResult>(Func<IDbConnection, TResult> action)
    {
        using var conn = _provider.CreateConnection();
        return action(conn);
    }
}
