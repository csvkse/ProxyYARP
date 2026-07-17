using System.Data;
using ProxyYARP.Data.Db;

namespace ProxyYARP.Data.Repositories;

/// <summary>Repository 基类，提供统一的数据库连接获取</summary>
public class BaseRepository<T> where T : class, new()
{
    private readonly IDbConnection? _explicitConnection;

    public BaseRepository(IDbConnection? connection = null)
    {
        _explicitConnection = connection;
    }

    protected void WithConnection(Action<IDbConnection> action)
    {
        if (_explicitConnection != null)
        {
            action(_explicitConnection);
        }
        else
        {
            using var conn = DbContext.GetConnection();
            action(conn);
        }
    }

    protected TResult WithConnection<TResult>(Func<IDbConnection, TResult> action)
    {
        if (_explicitConnection != null)
        {
            return action(_explicitConnection);
        }
        else
        {
            using var conn = DbContext.GetConnection();
            return action(conn);
        }
    }
}
