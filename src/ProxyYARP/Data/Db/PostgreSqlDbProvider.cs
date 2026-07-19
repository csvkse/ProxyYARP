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
            CREATE TABLE IF NOT EXISTS "ProxyYARP_ApiKeys" (
                "Id"          TEXT PRIMARY KEY,
                "KeyValue"    TEXT NOT NULL UNIQUE,
                "Name"        TEXT NOT NULL,
                "Role"        TEXT NOT NULL DEFAULT 'ReadOnly',
                "IsEnabled"   BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"   TIMESTAMPTZ NOT NULL,
                "LastUsedAt"  TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS "idx_apikeys_keyvalue" ON "ProxyYARP_ApiKeys"("KeyValue");

            CREATE TABLE IF NOT EXISTS "ProxyYARP_ConfigGroups" (
                "Id"            TEXT PRIMARY KEY,
                "Name"          TEXT NOT NULL,
                "ConfigVersion" INTEGER NOT NULL DEFAULT 1
            );

            INSERT INTO "ProxyYARP_ConfigGroups" ("Id", "Name", "ConfigVersion")
            VALUES ('default', 'Default Group', 1)
            ON CONFLICT ("Id") DO NOTHING;

            CREATE TABLE IF NOT EXISTS "ProxyYARP_Nodes" (
                "Id"                  TEXT PRIMARY KEY,
                "GroupId"             TEXT NOT NULL,
                "Name"                TEXT NOT NULL,
                "ManagementUrl"       TEXT,
                "IsManagementEnabled" BOOLEAN NOT NULL DEFAULT TRUE,
                "LastHeartbeat"       TIMESTAMPTZ,
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
                "IsEnabled"   BOOLEAN NOT NULL DEFAULT TRUE,
                "Metadata"    TEXT,
                "CreatedAt"   TIMESTAMPTZ NOT NULL,
                "UpdatedAt"   TIMESTAMPTZ NOT NULL,
                FOREIGN KEY("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE,
                UNIQUE("RouteId", "GroupId")
            );

            CREATE TABLE IF NOT EXISTS "ProxyYARP_Clusters" (
                "Id"                  TEXT PRIMARY KEY,
                "ClusterId"           TEXT NOT NULL,
                "GroupId"             TEXT NOT NULL,
                "LoadBalancing"       TEXT NOT NULL DEFAULT 'RoundRobin',
                "HealthCheckEnabled"  TEXT,
                "IsEnabled"           BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"           TIMESTAMPTZ NOT NULL,
                "UpdatedAt"           TIMESTAMPTZ NOT NULL,
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
                "IsEnabled"   BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"   TIMESTAMPTZ NOT NULL,
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
                "IsEnabled"           BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"           TIMESTAMPTZ NOT NULL,
                "UpdatedAt"           TIMESTAMPTZ NOT NULL,
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
                "IsEnabled"   BOOLEAN NOT NULL DEFAULT TRUE,
                "CreatedAt"   TIMESTAMPTZ NOT NULL,
                "UpdatedAt"   TIMESTAMPTZ NOT NULL,
                FOREIGN KEY("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE,
                FOREIGN KEY("RouteId", "GroupId") REFERENCES "ProxyYARP_L4Routes"("RouteId", "GroupId") ON DELETE CASCADE
            );
            """),
        new DbMigration(2, "AddTargetGroupId", """
            ALTER TABLE "ProxyYARP_Nodes" ADD COLUMN IF NOT EXISTS "TargetGroupId" TEXT;
            """)
    ];
}
