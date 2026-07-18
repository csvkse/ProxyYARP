using System.Data;

namespace ProxyYARP.Data.Db;

/// <summary>数据库 Provider 抽象：连接工厂 + schema 迁移脚本来源</summary>
public interface IDbProvider
{
    /// <summary>Provider 标识：sqlite | pgsql</summary>
    string Name { get; }

    /// <summary>Banner 显示用安全描述（不含密码）</summary>
    string DisplayInfo { get; }

    /// <summary>创建新连接（调用方负责释放）</summary>
    IDbConnection CreateConnection();

    /// <summary>按 Version 升序排列的 schema 迁移脚本</summary>
    IReadOnlyList<DbMigration> Migrations { get; }
}
