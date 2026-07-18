using System.Data;
using Npgsql;

namespace ProxyYARP.Data.Db;

/// <summary>PostgreSQL Provider（Npgsql 10，官方兼容 Native AOT/trimming，纯托管无原生依赖）</summary>
public sealed class PostgreSqlDbProvider : IDbProvider
{
    private readonly string _connectionString;

    public PostgreSqlDbProvider(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "pgsql Provider 需要连接字符串（DB_CONNECTION 或 --db-conn）", nameof(connectionString));
        _connectionString = connectionString;
    }

    public string Name => "pgsql";

    /// <summary>显示用信息，剔除密码等敏感项</summary>
    public string DisplayInfo
    {
        get
        {
            var b = new NpgsqlConnectionStringBuilder(_connectionString);
            return $"Host={b.Host};Port={b.Port};Database={b.Database};Username={b.Username}";
        }
    }

    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    public IReadOnlyList<DbMigration> Migrations { get; } =
    [
        new DbMigration(1, "InitialSchema", """
            CREATE TABLE IF NOT EXISTS "ApiKeys" (
                "Id"          TEXT PRIMARY KEY,
                "KeyValue"    TEXT NOT NULL UNIQUE,
                "Name"        TEXT NOT NULL,
                "Role"        TEXT NOT NULL DEFAULT 'ReadOnly',
                "IsEnabled"   BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"   TIMESTAMPTZ NOT NULL,
                "LastUsedAt"  TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS "idx_apikeys_keyvalue" ON "ApiKeys"("KeyValue");

            CREATE TABLE IF NOT EXISTS "ProxyRoutes" (
                "Id"          TEXT PRIMARY KEY,
                "RouteId"     TEXT NOT NULL UNIQUE,
                "ClusterId"   TEXT NOT NULL,
                "Path"        TEXT NOT NULL,
                "Methods"     TEXT,
                "Hosts"       TEXT,
                "Order"       INTEGER DEFAULT 0,
                "IsEnabled"   BOOLEAN NOT NULL DEFAULT TRUE,
                "Metadata"    TEXT,
                "CreatedAt"   TIMESTAMPTZ NOT NULL,
                "UpdatedAt"   TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "ProxyClusters" (
                "Id"                  TEXT PRIMARY KEY,
                "ClusterId"           TEXT NOT NULL UNIQUE,
                "LoadBalancing"       TEXT NOT NULL DEFAULT 'RoundRobin',
                "HealthCheckEnabled"  TEXT,
                "IsEnabled"           BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"           TIMESTAMPTZ NOT NULL,
                "UpdatedAt"           TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "ProxyDestinations" (
                "Id"          TEXT PRIMARY KEY,
                "ClusterId"   TEXT NOT NULL,
                "DestId"      TEXT NOT NULL,
                "Address"     TEXT NOT NULL,
                "Health"      TEXT,
                "Metadata"    TEXT,
                "IsEnabled"   BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"   TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "idx_dest_clusterid" ON "ProxyDestinations"("ClusterId");

            CREATE TABLE IF NOT EXISTS "ProxyL4Routes" (
                "Id"                  TEXT PRIMARY KEY,
                "RouteId"             TEXT NOT NULL,
                "ListenPort"          INTEGER NOT NULL UNIQUE,
                "Protocol"            TEXT NOT NULL DEFAULT 'TCP',
                "LoadBalancingPolicy" TEXT NOT NULL DEFAULT 'RoundRobin',
                "IdleTimeoutSeconds"  INTEGER NOT NULL DEFAULT 60,
                "IsEnabled"           BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"           TIMESTAMPTZ NOT NULL,
                "UpdatedAt"           TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "ProxyL4Destinations" (
                "Id"          TEXT PRIMARY KEY,
                "RouteId"     TEXT NOT NULL,
                "TargetHost"  TEXT NOT NULL,
                "TargetPort"  INTEGER NOT NULL,
                "Weight"      INTEGER NOT NULL DEFAULT 1,
                "IsEnabled"   BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"   TIMESTAMPTZ NOT NULL,
                "UpdatedAt"   TIMESTAMPTZ NOT NULL
            );
            """)
    ];
}
