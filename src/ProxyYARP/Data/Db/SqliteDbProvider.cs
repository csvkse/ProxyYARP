using System.Data;
using Microsoft.Data.Sqlite;

namespace ProxyYARP.Data.Db;

/// <summary>SQLite Provider（默认，零配置嵌入式）</summary>
public sealed class SqliteDbProvider : IDbProvider
{
    private readonly string _connectionString;

    public SqliteDbProvider(string? connectionString)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? $"Data Source={Path.Combine(AppContext.BaseDirectory, "proxy.db")};Cache=Shared;"
            : connectionString;
    }

    public string Name => "sqlite";

    public string DisplayInfo => _connectionString;

    public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public IReadOnlyList<DbMigration> Migrations { get; } =
    [
        new DbMigration(1, "InitialSchema", """
            CREATE TABLE IF NOT EXISTS "ApiKeys" (
                "Id"          TEXT PRIMARY KEY,
                "KeyValue"    TEXT NOT NULL UNIQUE,
                "Name"        TEXT NOT NULL,
                "Role"        TEXT NOT NULL DEFAULT 'ReadOnly',
                "IsEnabled"   INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"   TEXT NOT NULL,
                "LastUsedAt"  TEXT
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
                "IsEnabled"   INTEGER NOT NULL DEFAULT 1,
                "Metadata"    TEXT,
                "CreatedAt"   TEXT NOT NULL,
                "UpdatedAt"   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "ProxyClusters" (
                "Id"                  TEXT PRIMARY KEY,
                "ClusterId"           TEXT NOT NULL UNIQUE,
                "LoadBalancing"       TEXT NOT NULL DEFAULT 'RoundRobin',
                "HealthCheckEnabled"  TEXT,
                "IsEnabled"           INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"           TEXT NOT NULL,
                "UpdatedAt"           TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "ProxyDestinations" (
                "Id"          TEXT PRIMARY KEY,
                "ClusterId"   TEXT NOT NULL,
                "DestId"      TEXT NOT NULL,
                "Address"     TEXT NOT NULL,
                "Health"      TEXT,
                "Metadata"    TEXT,
                "IsEnabled"   INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "idx_dest_clusterid" ON "ProxyDestinations"("ClusterId");

            CREATE TABLE IF NOT EXISTS "ProxyL4Routes" (
                "Id"                  TEXT PRIMARY KEY,
                "RouteId"             TEXT NOT NULL,
                "ListenPort"          INTEGER NOT NULL UNIQUE,
                "Protocol"            TEXT NOT NULL DEFAULT 'TCP',
                "LoadBalancingPolicy" TEXT NOT NULL DEFAULT 'RoundRobin',
                "IdleTimeoutSeconds"  INTEGER NOT NULL DEFAULT 60,
                "IsEnabled"           INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"           TEXT NOT NULL,
                "UpdatedAt"           TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "ProxyL4Destinations" (
                "Id"          TEXT PRIMARY KEY,
                "RouteId"     TEXT NOT NULL,
                "TargetHost"  TEXT NOT NULL,
                "TargetPort"  INTEGER NOT NULL,
                "Weight"      INTEGER NOT NULL DEFAULT 1,
                "IsEnabled"   INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"   TEXT NOT NULL,
                "UpdatedAt"   TEXT NOT NULL
            );
            """)
    ];
}
