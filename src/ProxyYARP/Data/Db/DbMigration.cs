namespace ProxyYARP.Data.Db;

/// <summary>一次 schema 迁移（Version 单调递增，应用后记录到 __SchemaMigrations）</summary>
public sealed record DbMigration(int Version, string Name, string Sql);
