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
            CREATE TABLE IF NOT EXISTS "ProxyYARP_ApiKeys" (
                "Id"          TEXT PRIMARY KEY,
                "KeyValue"    TEXT NOT NULL UNIQUE,
                "Name"        TEXT NOT NULL,
                "Role"        TEXT NOT NULL DEFAULT 'ReadOnly',
                "IsEnabled"   INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"   TEXT NOT NULL,
                "LastUsedAt"  TEXT
            );
            CREATE INDEX IF NOT EXISTS "idx_apikeys_keyvalue" ON "ProxyYARP_ApiKeys"("KeyValue");

            CREATE TABLE IF NOT EXISTS "ProxyYARP_ConfigGroups" (
                "Id"            TEXT PRIMARY KEY,
                "Name"          TEXT NOT NULL,
                "ConfigVersion" INTEGER NOT NULL DEFAULT 1
            );

            INSERT OR IGNORE INTO "ProxyYARP_ConfigGroups" ("Id", "Name", "ConfigVersion")
            VALUES ('default', 'Default Group', 1);

            CREATE TABLE IF NOT EXISTS "ProxyYARP_Nodes" (
                "Id"                  TEXT PRIMARY KEY,
                "GroupId"             TEXT NOT NULL,
                "Name"                TEXT NOT NULL,
                "ManagementUrl"       TEXT,
                "IsManagementEnabled" INTEGER NOT NULL DEFAULT 1,
                "LastHeartbeat"       TEXT,
                FOREIGN KEY("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "ProxyYARP_Routes" (
                "Id"          TEXT PRIMARY KEY,
                "RouteId"     TEXT NOT NULL,
                "GroupId"     TEXT NOT NULL,
                "ClusterId"   TEXT NOT NULL,
                "Path"        TEXT NOT NULL,
                "Methods"     TEXT,
                "Hosts"       TEXT,
                "Order"       INTEGER DEFAULT 0,
                "IsEnabled"   INTEGER NOT NULL DEFAULT 1,
                "Metadata"    TEXT,
                "CreatedAt"   TEXT NOT NULL,
                "UpdatedAt"   TEXT NOT NULL,
                FOREIGN KEY("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE,
                UNIQUE("RouteId", "GroupId")
            );

            CREATE TABLE IF NOT EXISTS "ProxyYARP_Clusters" (
                "Id"                  TEXT PRIMARY KEY,
                "ClusterId"           TEXT NOT NULL,
                "GroupId"             TEXT NOT NULL,
                "LoadBalancing"       TEXT NOT NULL DEFAULT 'RoundRobin',
                "HealthCheckEnabled"  TEXT,
                "IsEnabled"           INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"           TEXT NOT NULL,
                "UpdatedAt"           TEXT NOT NULL,
                FOREIGN KEY("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE,
                UNIQUE("ClusterId", "GroupId")
            );

            CREATE TABLE IF NOT EXISTS "ProxyYARP_Destinations" (
                "Id"          TEXT PRIMARY KEY,
                "ClusterId"   TEXT NOT NULL,
                "GroupId"     TEXT NOT NULL,
                "DestId"      TEXT NOT NULL,
                "Address"     TEXT NOT NULL,
                "Health"      TEXT,
                "Metadata"    TEXT,
                "IsEnabled"   INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"   TEXT NOT NULL,
                FOREIGN KEY("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE,
                FOREIGN KEY("ClusterId", "GroupId") REFERENCES "ProxyYARP_Clusters"("ClusterId", "GroupId") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "idx_dest_clusterid_groupid" ON "ProxyYARP_Destinations"("ClusterId", "GroupId");

            CREATE TABLE IF NOT EXISTS "ProxyYARP_L4Routes" (
                "Id"                  TEXT PRIMARY KEY,
                "RouteId"             TEXT NOT NULL,
                "GroupId"             TEXT NOT NULL,
                "ListenPort"          INTEGER NOT NULL,
                "Protocol"            TEXT NOT NULL DEFAULT 'TCP',
                "LoadBalancingPolicy" TEXT NOT NULL DEFAULT 'RoundRobin',
                "IdleTimeoutSeconds"  INTEGER NOT NULL DEFAULT 60,
                "IsEnabled"           INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"           TEXT NOT NULL,
                "UpdatedAt"           TEXT NOT NULL,
                FOREIGN KEY("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE,
                UNIQUE("ListenPort", "GroupId"),
                UNIQUE("RouteId", "GroupId")
            );

            CREATE TABLE IF NOT EXISTS "ProxyYARP_L4Destinations" (
                "Id"          TEXT PRIMARY KEY,
                "RouteId"     TEXT NOT NULL,
                "GroupId"     TEXT NOT NULL,
                "TargetHost"  TEXT NOT NULL,
                "TargetPort"  INTEGER NOT NULL,
                "Weight"      INTEGER NOT NULL DEFAULT 1,
                "IsEnabled"   INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"   TEXT NOT NULL,
                "UpdatedAt"   TEXT NOT NULL,
                FOREIGN KEY("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE,
                FOREIGN KEY("RouteId", "GroupId") REFERENCES "ProxyYARP_L4Routes"("RouteId", "GroupId") ON DELETE CASCADE
            );
            """)
    ];
}
