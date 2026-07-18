# 多数据库支持（SQLite + PostgreSQL）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ProxyYARP 在 Native AOT 下支持 SQLite 与 PostgreSQL 双数据库，通过 `DB_TYPE`/`DB_CONNECTION` 环境变量（及命令行/Docker）切换，附完整单元与集成测试。

**Architecture:** 保持 Dapper + Dapper.AOT（严格模式）不变，新增 `IDbProvider` 抽象（连接工厂 + 每 Provider 迁移脚本），`MigrationRunner` 版本化建表；实体改原生 `bool`/`DateTime`；SQL 全标识符双引号（PascalCase，pgsql 大小写敏感兼容）；Repository 改 DI 注入，废弃静态 `DbContext`。

**Tech Stack:** .NET 10 / PublishAot / Dapper.AOT 1.0.52 / Npgsql 10.0.3 / Microsoft.Data.Sqlite 10.0.10 / xunit + FluentAssertions / Testcontainers.PostgreSql

## Global Constraints

- `PublishAot=true`、`DapperAotStrict=true`：禁止任何反射回退；新增 Dapper 调用必须能被 Dapper.AOT 编译期拦截。
- 所有 SQL 标识符（表名、列名、索引名）必须双引号 + PascalCase：`"ProxyRoutes"`、`"IsEnabled"`、`"Order"`。
- 固定启用过滤统一写 `WHERE "IsEnabled" = TRUE`（SQLite ≥3.23 与 pgsql 均支持 TRUE 字面量）。
- 实体 `CreatedAt`/`UpdatedAt` 写入一律 `DateTime.UtcNow`（Npgsql timestamptz 只接受 UTC）。
- 配置键：`Database:Provider`（`sqlite`|`pgsql`，默认 sqlite）、`Database:ConnectionString`。
- 环境变量：`DB_TYPE`、`DB_CONNECTION`；命令行：`--db-type`、`--db-conn`。
- 废弃并删除：`ProxyConfig:DbPath`、`DB_PATH`、`-db`/`--Db`（含 appsettings、Program、Dockerfile、README）。
- C# 规范：file-scoped namespace、nullable enable、PascalCase 成员/camelCase 局部、中文注释风格与现有一致。
- 每个 Task 结束必须 `dotnet build` 零警告 + 相关测试全绿才提交。

---

### Task 0: 提交工作区现有改动

工作区有未提交的依赖版本升级（Dapper 2.1.79 / Sqlite 10.0.10 / Scalar 2.16.15 / FileProviders 10.0.10），先独立提交，避免混入本特性。

**Files:**
- Modify: `src/ProxyYARP/ProxyYARP.csproj`（已有改动，不新编）

- [ ] **Step 1: 验证当前状态可构建可测试**

Run: `dotnet test src/ProxyYARP.Tests -c Debug`
Expected: 全部 PASS（基线）

- [ ] **Step 2: 提交**

```bash
git add src/ProxyYARP/ProxyYARP.csproj
git commit -m "chore: 升级依赖 Dapper 2.1.79 / Sqlite 10.0.10 / Scalar 2.16.15"
```

---

### Task 1: IDbProvider 抽象 + SqliteDbProvider + 工厂 + DI 注册

**Files:**
- Create: `src/ProxyYARP/Data/Db/IDbProvider.cs`
- Create: `src/ProxyYARP/Data/Db/DbMigration.cs`
- Create: `src/ProxyYARP/Data/Db/SqliteDbProvider.cs`
- Create: `src/ProxyYARP/Data/Db/DatabaseProviderFactory.cs`
- Modify: `src/ProxyYARP/Program.cs`（新增配置键 + DI 注册，旧 DbPath 暂保留）
- Test: `src/ProxyYARP.Tests/Unit/DatabaseProviderFactoryTests.cs`

**Interfaces:**
- Produces（后续所有 Task 依赖）:
  - `interface IDbProvider { string Name { get; } string DisplayInfo { get; } IDbConnection CreateConnection(); IReadOnlyList<DbMigration> Migrations { get; } }`
  - `record DbMigration(int Version, string Name, string Sql)`
  - `SqliteDbProvider(string? connectionString)`
  - `DatabaseProviderFactory.Create(string? provider, string? connectionString) : IDbProvider`

- [ ] **Step 1: 写失败测试**

Create `src/ProxyYARP.Tests/Unit/DatabaseProviderFactoryTests.cs`:

```csharp
using FluentAssertions;
using ProxyYARP.Data.Db;

namespace ProxyYARP.Tests.Unit;

/// <summary>DatabaseProviderFactory 单元测试</summary>
public class DatabaseProviderFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sqlite")]
    [InlineData("SQLite")]
    [InlineData(" SQLITE ")]
    public void Create_Should_Return_Sqlite_By_Default(string? provider)
    {
        var p = DatabaseProviderFactory.Create(provider, null);
        p.Name.Should().Be("sqlite");
    }

    [Fact]
    public void Create_Sqlite_Empty_ConnectionString_Should_Use_Default_Path()
    {
        var p = DatabaseProviderFactory.Create("sqlite", null);
        p.DisplayInfo.Should().Contain("proxy.db");
    }

    [Fact]
    public void Create_Sqlite_Should_Pass_Through_ConnectionString()
    {
        var p = DatabaseProviderFactory.Create("sqlite", "Data Source=/tmp/x.db;Cache=Shared;");
        p.DisplayInfo.Should().Be("Data Source=/tmp/x.db;Cache=Shared;");
    }

    [Fact]
    public void Create_Unknown_Provider_Should_Throw()
    {
        var act = () => DatabaseProviderFactory.Create("oracle", null);
        act.Should().Throw<ArgumentException>().WithMessage("*oracle*");
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败**

Run: `dotnet test src/ProxyYARP.Tests --filter "FullyQualifiedName~DatabaseProviderFactoryTests"`
Expected: 编译错误 `DatabaseProviderFactory 不存在`

- [ ] **Step 3: 实现抽象 + Sqlite Provider + 工厂**

Create `src/ProxyYARP/Data/Db/IDbProvider.cs`:

```csharp
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
```

Create `src/ProxyYARP/Data/Db/DbMigration.cs`:

```csharp
namespace ProxyYARP.Data.Db;

/// <summary>一次 schema 迁移（Version 单调递增，应用后记录到 __SchemaMigrations）</summary>
public sealed record DbMigration(int Version, string Name, string Sql);
```

Create `src/ProxyYARP/Data/Db/SqliteDbProvider.cs`（DDL 从现有 6 个 Repository 的 CreateTable 收编，标识符全部加双引号）:

```csharp
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
```

Create `src/ProxyYARP/Data/Db/DatabaseProviderFactory.cs`（pgsql 分支 Task 2 再加，本 Task 先抛错）:

```csharp
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
```

- [ ] **Step 4: Program.cs 接线（新配置键 + DI，不动旧 DbPath 逻辑）**

`Program.cs` envMappings 字典改为（删除 DB_PATH 留到 Task 5，本轮只加两行）:

```csharp
        var envMappings = new Dictionary<string, string>
        {
            { "PROXY_PORT",    "ProxyConfig:Port" },
            { "ACCESS_KEY",    "ProxyConfig:AdminKey" },
            { "DB_PATH",       "ProxyConfig:DbPath" },
            { "DB_TYPE",       "Database:Provider" },
            { "DB_CONNECTION", "Database:ConnectionString" }
        };
```

switchMappings 改为:

```csharp
        var switchMappings = new Dictionary<string, string>
        {
            { "-p",        "ProxyConfig:Port" },
            { "--Port",    "ProxyConfig:Port" },
            { "-k",        "ProxyConfig:AdminKey" },
            { "--Key",     "ProxyConfig:AdminKey" },
            { "-db",       "ProxyConfig:DbPath" },
            { "--Db",      "ProxyConfig:DbPath" },
            { "--db-type", "Database:Provider" },
            { "--db-conn", "Database:ConnectionString" }
        };
```

`// 配置 SQLite 路径` 一行之后插入:

```csharp
        // 数据库 Provider（sqlite 默认；pgsql 用 --db-type pgsql --db-conn "Host=..."）
        var dbProvider = DatabaseProviderFactory.Create(
            config["Database:Provider"], config["Database:ConnectionString"]);
```

服务注册区（`builder.Services.AddSingleton<ApiKeyRepository>();` 之前）插入:

```csharp
        builder.Services.AddSingleton<IDbProvider>(dbProvider);
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test src/ProxyYARP.Tests --filter "FullyQualifiedName~DatabaseProviderFactoryTests"`
Expected: 4 个测试 PASS

- [ ] **Step 6: 全量回归 + 提交**

Run: `dotnet test src/ProxyYARP.Tests`
Expected: 全部 PASS

```bash
git add src/ProxyYARP/Data/Db/IDbProvider.cs src/ProxyYARP/Data/Db/DbMigration.cs src/ProxyYARP/Data/Db/SqliteDbProvider.cs src/ProxyYARP/Data/Db/DatabaseProviderFactory.cs src/ProxyYARP/Program.cs src/ProxyYARP.Tests/Unit/DatabaseProviderFactoryTests.cs
git commit -m "feat: IDbProvider 抽象 + SqliteDbProvider + 工厂 + DI 注册"
```

---

### Task 2: Npgsql + PostgreSqlDbProvider

**Files:**
- Modify: `src/ProxyYARP/ProxyYARP.csproj`（加 Npgsql）
- Create: `src/ProxyYARP/Data/Db/PostgreSqlDbProvider.cs`
- Modify: `src/ProxyYARP/Data/Db/DatabaseProviderFactory.cs`（加 pgsql 分支）
- Test: `src/ProxyYARP.Tests/Unit/PostgreSqlDbProviderTests.cs`

**Interfaces:**
- Consumes: `IDbProvider`、`DbMigration`、`DatabaseProviderFactory.Create`（Task 1）
- Produces: `PostgreSqlDbProvider(string? connectionString)`；工厂支持 `"pgsql"|"postgres"|"postgresql"`

- [ ] **Step 1: 写失败测试**

Create `src/ProxyYARP.Tests/Unit/PostgreSqlDbProviderTests.cs`:

```csharp
using FluentAssertions;
using ProxyYARP.Data.Db;

namespace ProxyYARP.Tests.Unit;

/// <summary>PostgreSqlDbProvider 单元测试（不连真实数据库）</summary>
public class PostgreSqlDbProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_Empty_ConnectionString_Should_Throw(string? connStr)
    {
        var act = () => new PostgreSqlDbProvider(connStr);
        act.Should().Throw<ArgumentException>().WithMessage("*连接字符串*");
    }

    [Fact]
    public void DisplayInfo_Should_Not_Leak_Password()
    {
        var p = new PostgreSqlDbProvider(
            "Host=pg.example.com;Port=5433;Database=proxy;Username=admin;Password=s3cret");
        p.DisplayInfo.Should().Contain("pg.example.com").And.Contain("5433");
        p.DisplayInfo.Should().NotContain("s3cret");
    }

    [Theory]
    [InlineData("pgsql")]
    [InlineData("postgres")]
    [InlineData("PostgreSQL")]
    public void Factory_Should_Create_Pgsql_Provider(string name)
    {
        var p = DatabaseProviderFactory.Create(name, "Host=x;Database=d;Username=u;Password=p");
        p.Name.Should().Be("pgsql");
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败**

Run: `dotnet test src/ProxyYARP.Tests --filter "FullyQualifiedName~PostgreSqlDbProviderTests"`
Expected: 编译错误 `PostgreSqlDbProvider 不存在`

- [ ] **Step 3: 加 Npgsql 包**

Run: `dotnet add src/ProxyYARP package Npgsql --version 10.0.3`
Expected: csproj 新增 `<PackageReference Include="Npgsql" Version="10.0.3" />`

- [ ] **Step 4: 实现 PostgreSqlDbProvider**

Create `src/ProxyYARP/Data/Db/PostgreSqlDbProvider.cs`:

```csharp
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
```

`DatabaseProviderFactory.Create` switch 改为:

```csharp
        return (provider ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "sqlite" => new SqliteDbProvider(connectionString),
            "pgsql" or "postgres" or "postgresql" => new PostgreSqlDbProvider(connectionString),
            var other => throw new ArgumentException(
                $"未知数据库 Provider: '{other}'（支持: sqlite, pgsql）", nameof(provider))
        };
```

- [ ] **Step 5: 跑测试确认通过 + 全量回归**

Run: `dotnet test src/ProxyYARP.Tests`
Expected: 全部 PASS

- [ ] **Step 6: 提交**

```bash
git add src/ProxyYARP/ProxyYARP.csproj src/ProxyYARP/Data/Db/PostgreSqlDbProvider.cs src/ProxyYARP/Data/Db/DatabaseProviderFactory.cs src/ProxyYARP.Tests/Unit/PostgreSqlDbProviderTests.cs
git commit -m "feat: Npgsql 10 + PostgreSqlDbProvider（含 pgsql 初始 schema）"
```

---

### Task 3: MigrationRunner 版本化迁移

**Files:**
- Create: `src/ProxyYARP/Data/Db/MigrationRunner.cs`
- Test: `src/ProxyYARP.Tests/Unit/MigrationRunnerTests.cs`

**Interfaces:**
- Consumes: `IDbProvider`、`DbMigration`（Task 1）
- Produces: `MigrationRunner.Migrate(IDbProvider provider) : void`（幂等；Program.cs 与测试均调用）

- [ ] **Step 1: 写失败测试**

Create `src/ProxyYARP.Tests/Unit/MigrationRunnerTests.cs`:

```csharp
using Dapper;
using FluentAssertions;
using ProxyYARP.Data.Db;

namespace ProxyYARP.Tests.Unit;

/// <summary>MigrationRunner 单元测试（SQLite 临时库）</summary>
public class MigrationRunnerTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"proxyyarp_mig_{Guid.NewGuid():N}.db");

    private SqliteDbProvider NewProvider() => new($"Data Source={_dbPath};");

    [Fact]
    public void Migrate_On_Empty_Db_Should_Create_All_Tables()
    {
        var provider = NewProvider();
        MigrationRunner.Migrate(provider);

        using var conn = provider.CreateConnection();
        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").AsList();
        tables.Should().Contain([
            "ApiKeys", "ProxyRoutes", "ProxyClusters",
            "ProxyDestinations", "ProxyL4Routes", "ProxyL4Destinations",
            "__SchemaMigrations"
        ]);
    }

    [Fact]
    public void Migrate_Twice_Should_Be_Idempotent()
    {
        var provider = NewProvider();
        MigrationRunner.Migrate(provider);
        var act = () => MigrationRunner.Migrate(provider);
        act.Should().NotThrow();

        using var conn = provider.CreateConnection();
        conn.ExecuteScalar<int>("""SELECT COUNT(*) FROM "__SchemaMigrations" """).Should().Be(1);
    }

    [Fact]
    public void Migrate_Should_Record_Migration_Name()
    {
        var provider = NewProvider();
        MigrationRunner.Migrate(provider);

        using var conn = provider.CreateConnection();
        conn.QueryFirst<string>("""SELECT "Name" FROM "__SchemaMigrations" WHERE "Version" = 1""")
            .Should().Be("InitialSchema");
    }

    public void Dispose()
    {
        Thread.Sleep(100);
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* 忽略清理错误 */ }
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败**

Run: `dotnet test src/ProxyYARP.Tests --filter "FullyQualifiedName~MigrationRunnerTests"`
Expected: 编译错误 `MigrationRunner 不存在`

- [ ] **Step 3: 实现 MigrationRunner**

Create `src/ProxyYARP/Data/Db/MigrationRunner.cs`:

```csharp
using Dapper;

namespace ProxyYARP.Data.Db;

/// <summary>版本化 schema 迁移执行器（AOT 安全，零第三方依赖，幂等）</summary>
public static class MigrationRunner
{
    /// <summary>执行所有未应用的迁移</summary>
    public static void Migrate(IDbProvider provider)
    {
        using var conn = provider.CreateConnection();
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS "__SchemaMigrations" (
                "Version"   INTEGER PRIMARY KEY,
                "Name"      TEXT NOT NULL,
                "AppliedAt" TEXT NOT NULL
            );
            """);

        var applied = conn.Query<int>("""SELECT "Version" FROM "__SchemaMigrations" """).ToHashSet();

        foreach (var migration in provider.Migrations.OrderBy(m => m.Version))
        {
            if (applied.Contains(migration.Version)) continue;

            conn.Execute(migration.Sql);
            conn.Execute("""
                INSERT INTO "__SchemaMigrations" ("Version", "Name", "AppliedAt")
                VALUES (@Version, @Name, @AppliedAt)
                """,
                new { migration.Version, migration.Name, AppliedAt = DateTime.UtcNow.ToString("o") });

            Console.WriteLine($"[DB] Applied migration {migration.Version}: {migration.Name} ({provider.Name})");
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过 + 全量回归**

Run: `dotnet test src/ProxyYARP.Tests`
Expected: 全部 PASS

- [ ] **Step 5: 提交**

```bash
git add src/ProxyYARP/Data/Db/MigrationRunner.cs src/ProxyYARP.Tests/Unit/MigrationRunnerTests.cs
git commit -m "feat: MigrationRunner 版本化 schema 迁移（幂等）"
```

---

### Task 4: 实体原生类型化（bool/DateTime）+ 全部消费方修复

**Files:**
- Modify: `src/ProxyYARP/Data/Models/ApiKeyEntity.cs`
- Modify: `src/ProxyYARP/Data/Models/ProxyRouteEntity.cs`
- Modify: `src/ProxyYARP/Data/Models/ProxyClusterEntity.cs`
- Modify: `src/ProxyYARP/Data/Models/ProxyDestinationEntity.cs`
- Modify: `src/ProxyYARP/Data/Models/L4ProxyRouteEntity.cs`
- Modify: `src/ProxyYARP/Data/Models/L4ProxyDestinationEntity.cs`
- Modify: `src/ProxyYARP/Data/Services/ApiKeyService.cs`
- Modify: `src/ProxyYARP/Data/Services/ProxyConfigService.cs`
- Modify: `src/ProxyYARP/Data/Services/L4ConfigService.cs`
- Modify: `src/ProxyYARP/Data/Services/DbInitService.cs`
- Modify: `src/ProxyYARP/Api/KeysApi.cs`、`RoutesApi.cs`、`ClustersApi.cs`、`L4RoutesApi.cs`
- Modify: `src/ProxyYARP.Tests/TestHelpers/ProxyYarpWebFactory.cs`（EnsureReadOnlyKey）
- Modify: `src/ProxyYARP.Tests/Unit/ApiKeyServiceTests.cs`、`ProxyConfigServiceTests.cs`（断言 `.Be(1)` → `.BeTrue()`）

**Interfaces:**
- Produces（Task 5 起全部 SQL 依赖此类型）:
  - 实体 `bool IsEnabled { get; set; } = true;`
  - `DateTime CreatedAt/UpdatedAt`、`DateTime? LastUsedAt`
  - API DTO 契约不变（`bool IsEnabled`、`string CreatedAt`），仅映射方式变

- [ ] **Step 1: 改 6 个实体**

`ApiKeyEntity.cs` 第 14-16 行改为:

```csharp
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
```

`ProxyRouteEntity.cs` 第 15、17-18 行改为:

```csharp
    public bool IsEnabled { get; set; } = true;  // true=启用, false=禁用
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
```

`ProxyClusterEntity.cs` 第 12-14 行、`ProxyDestinationEntity.cs` 第 14-15 行、`L4ProxyRouteEntity.cs` 第 12-14 行、`L4ProxyDestinationEntity.cs` 第 11-13 行同模式：`int IsEnabled` → `bool IsEnabled { get; set; } = true;`，`string CreatedAt/UpdatedAt` → `DateTime`。

- [ ] **Step 2: 改 Service 层（编译驱动，逐文件修）**

统一替换规则（4 个 Service 文件全部适用）：
- `IsEnabled = 1` → `IsEnabled = true`
- `entity.IsEnabled = isEnabled ? 1 : 0;` → `entity.IsEnabled = isEnabled;`
- `DateTime.UtcNow.ToString("o")` → `DateTime.UtcNow`
- `L4ConfigService.cs:41` `.Where(d => d.IsEnabled == 1)` → `.Where(d => d.IsEnabled)`

注意 `DbInitService.cs:54` `IsEnabled = 1`、`:69` `var now = DateTime.UtcNow.ToString("o");` 同样处理。

- [ ] **Step 3: 改 API 层 DTO 映射**

`KeysApi.cs:70-72` 改为:

```csharp
        IsEnabled = e.IsEnabled,
        CreatedAt = e.CreatedAt.ToString("o"),
        LastUsedAt = e.LastUsedAt?.ToString("o")
```

`RoutesApi.cs:80-83` 改为:

```csharp
        IsEnabled = e.IsEnabled,
        Order = e.Order,
        CreatedAt = e.CreatedAt.ToString("o"),
        UpdatedAt = e.UpdatedAt.ToString("o")
```

`ClustersApi.cs:134-136` 与 `:147-148` 同模式（`e.IsEnabled` 直传、`e.CreatedAt.ToString("o")`、`e.UpdatedAt.ToString("o")`）。

`L4RoutesApi.cs:154-156` 同模式；`:63` 与 `:102` 的 `IsEnabled = 1` → `IsEnabled = true`。

- [ ] **Step 4: 改测试侧**

`ProxyYarpWebFactory.cs:92-93` 改为:

```csharp
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
```

`ApiKeyServiceTests.cs:30` 与 `:131`：`entity.IsEnabled.Should().Be(1);` → `entity.IsEnabled.Should().BeTrue();`（`:131` 为 `updated!.IsEnabled`）。
`ProxyConfigServiceTests.cs:34`：`route.IsEnabled.Should().Be(1);` → `route.IsEnabled.Should().BeTrue();`

- [ ] **Step 5: 构建 + 修残余编译错误**

Run: `dotnet build`
Expected: 0 error。若有遗漏（如 `TcpProxyModuleTests`/`UdpProxyEngineTests` 构造实体处），按 Step 2 同规则修。

- [ ] **Step 6: 全量测试（验证 Dapper.AOT bool↔INTEGER、DateTime↔TEXT 双向映射）**

Run: `dotnet test src/ProxyYARP.Tests`
Expected: 全部 PASS。
**若失败于 bool/DateTime 映射**（Dapper.AOT 对 sqlite INTEGER→bool 或 TEXT→DateTime 不支持）：停止并上报，回退方案为 SELECT 显式列 + 方言分支，不得静默绕过。

- [ ] **Step 7: 提交**

```bash
git add -A
git commit -m "refactor: 实体原生类型化 bool/DateTime，DTO 边界统一 ToString(\"o\")"
```

---

### Task 5: Repository 改造 + Program 接线完成 + 删除 DbContext + 测试设施改造

**Files:**
- Modify: `src/ProxyYARP/Data/Repositories/BaseRepository.cs`
- Modify: 6 个 Repository（注入 `IDbProvider`、SQL 全引号、删 CreateTable、`= TRUE`）
- Modify: `src/ProxyYARP/Data/Services/DbInitService.cs`（删 InitTables）
- Modify: `src/ProxyYARP/Program.cs`（迁移走 DI、Banner、删 DbPath 全链路）
- Modify: `src/ProxyYARP/appsettings.json`、`appsettings.Development.json`
- Delete: `src/ProxyYARP/Data/Db/DbContext.cs`
- Modify: `src/ProxyYARP.Tests/TestHelpers/TestDatabase.cs`、`ProxyYarpWebFactory.cs`
- Test: `src/ProxyYARP.Tests/Unit/SqliteRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbProvider`（DI 已注册，Task 1）、`MigrationRunner.Migrate`（Task 3）
- Produces: `BaseRepository<T>(IDbProvider)`；所有 Repository 构造签名 `XRepository(IDbProvider provider)`

- [ ] **Step 1: 写失败测试（sqlite 全 Repository CRUD 往返）**

Create `src/ProxyYARP.Tests/Unit/SqliteRepositoryTests.cs`:

```csharp
using FluentAssertions;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Tests.Unit;

/// <summary>SQLite 下 6 个 Repository 的 CRUD 往返测试（验证引号 SQL + bool/DateTime 映射）</summary>
public class SqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"proxyyarp_repo_{Guid.NewGuid():N}.db");
    private readonly SqliteDbProvider _provider;

    public SqliteRepositoryTests()
    {
        _provider = new SqliteDbProvider($"Data Source={_dbPath};Cache=Shared;");
        MigrationRunner.Migrate(_provider);
    }

    [Fact]
    public void ApiKeyRepository_Roundtrip()
    {
        var repo = new ApiKeyRepository(_provider);
        var entity = new ApiKeyEntity
        {
            Id = Guid.NewGuid().ToString(),
            KeyValue = "k-" + Guid.NewGuid().ToString("N"),
            Name = "t", Role = "Admin",
            IsEnabled = true, CreatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        var loaded = repo.GetByKeyValue(entity.KeyValue);
        loaded.Should().NotBeNull();
        loaded!.IsEnabled.Should().BeTrue();
        loaded.CreatedAt.Should().BeCloseTo(entity.CreatedAt, TimeSpan.FromSeconds(1));

        repo.UpdateLastUsed(entity.KeyValue);
        repo.GetById(entity.Id)!.LastUsedAt.Should().NotBeNull();

        loaded.IsEnabled = false;
        repo.Update(loaded);
        repo.GetByKeyValue(entity.KeyValue).Should().BeNull("禁用后 GetByKeyValue 应过滤");

        repo.Delete(entity.Id);
        repo.GetById(entity.Id).Should().BeNull();
    }

    [Fact]
    public void RouteRepository_Roundtrip()
    {
        var repo = new RouteRepository(_provider);
        var entity = new ProxyRouteEntity
        {
            Id = Guid.NewGuid().ToString(), RouteId = "r1", ClusterId = "c1",
            Path = "/a/{**rest}", Order = 5, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        repo.GetAllEnabled().Should().ContainSingle(r => r.RouteId == "r1");
        repo.GetById(entity.Id)!.Order.Should().Be(5);

        entity.IsEnabled = false;
        entity.Path = "/b/{**rest}";
        repo.Update(entity);
        repo.GetAllEnabled().Should().BeEmpty();
        repo.GetById(entity.Id)!.Path.Should().Be("/b/{**rest}");

        repo.Delete(entity.Id);
        repo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void ClusterRepository_Roundtrip()
    {
        var repo = new ClusterRepository(_provider);
        var entity = new ProxyClusterEntity
        {
            Id = Guid.NewGuid().ToString(), ClusterId = "c1",
            LoadBalancing = "LeastRequests", IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        repo.GetAllEnabled().Should().ContainSingle(c => c.ClusterId == "c1");

        entity.IsEnabled = false;
        repo.Update(entity);
        repo.GetAllEnabled().Should().BeEmpty();
        repo.GetById(entity.Id)!.IsEnabled.Should().BeFalse();

        repo.Delete(entity.Id);
        repo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void DestinationRepository_Roundtrip()
    {
        var repo = new DestinationRepository(_provider);
        var entity = new ProxyDestinationEntity
        {
            Id = Guid.NewGuid().ToString(), ClusterId = "c1", DestId = "d1",
            Address = "http://localhost:9000", IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        repo.GetByClusterId("c1").Should().ContainSingle(d => d.DestId == "d1");

        entity.IsEnabled = false;
        repo.Update(entity);
        repo.GetByClusterId("c1").Should().BeEmpty("禁用后应被过滤");
        repo.GetAllByClusterId("c1").Should().HaveCount(1);

        repo.DeleteByClusterId("c1");
        repo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void L4RouteRepository_Roundtrip()
    {
        var repo = new L4RouteRepository(_provider);
        var entity = new L4ProxyRouteEntity
        {
            Id = Guid.NewGuid().ToString(), RouteId = "l4r1", ListenPort = 15333,
            Protocol = "TCP", IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        repo.GetByListenPort(15333).Should().NotBeNull();
        repo.GetAllEnabled().Should().ContainSingle(r => r.RouteId == "l4r1");

        entity.IsEnabled = false;
        entity.IdleTimeoutSeconds = 120;
        repo.Update(entity);
        repo.GetAllEnabled().Should().BeEmpty();
        repo.GetById(entity.Id)!.IdleTimeoutSeconds.Should().Be(120);

        repo.Delete(entity.Id);
        repo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void L4DestinationRepository_Roundtrip()
    {
        var repo = new L4DestinationRepository(_provider);
        var entity = new L4ProxyDestinationEntity
        {
            Id = Guid.NewGuid().ToString(), RouteId = "l4r1",
            TargetHost = "127.0.0.1", TargetPort = 9001, Weight = 2,
            IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        repo.Insert(entity);

        var loaded = repo.GetByRouteId("l4r1").Should().ContainSingle().Subject;
        loaded.Weight.Should().Be(2);
        loaded.IsEnabled.Should().BeTrue();

        repo.DeleteByRouteId("l4r1");
        repo.GetAll().Should().BeEmpty();
    }

    public void Dispose()
    {
        Thread.Sleep(100);
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* 忽略清理错误 */ }
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败**

Run: `dotnet test src/ProxyYARP.Tests --filter "FullyQualifiedName~SqliteRepositoryTests"`
Expected: 编译错误（Repository 构造签名不匹配）

- [ ] **Step 3: 改 BaseRepository**

`BaseRepository.cs` 全文替换为:

```csharp
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
```

- [ ] **Step 4: 改 6 个 Repository**

统一改造规则（每个文件适用）：
1. 构造函数改为 `public XRepository(IDbProvider provider) : base(provider) { }`（加 `using ProxyYARP.Data.Db;`）
2. 删除整个 `CreateTable()` 方法
3. 所有 SQL 的表名/列名/索引名加双引号
4. `WHERE IsEnabled = 1` → `WHERE "IsEnabled" = TRUE`

`RouteRepository.cs` 改后全文（其余 5 个按同规则改，下方给出每处 SQL 新旧对照）:

```csharp
using Dapper;
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Models;

namespace ProxyYARP.Data.Repositories;

[DapperAot]
public class RouteRepository : BaseRepository<ProxyRouteEntity>
{
    public RouteRepository(IDbProvider provider) : base(provider) { }

    public List<ProxyRouteEntity> GetAllEnabled()
    {
        return WithConnection(c => c.Query<ProxyRouteEntity>(
            """SELECT * FROM "ProxyRoutes" WHERE "IsEnabled" = TRUE ORDER BY "Order" ASC""")
            .AsList());
    }

    public List<ProxyRouteEntity> GetAll()
    {
        return WithConnection(c => c.Query<ProxyRouteEntity>(
            """SELECT * FROM "ProxyRoutes" ORDER BY "CreatedAt" DESC""")
            .AsList());
    }

    public ProxyRouteEntity? GetById(string id)
    {
        return WithConnection(c => c.QueryFirstOrDefault<ProxyRouteEntity>(
            """SELECT * FROM "ProxyRoutes" WHERE "Id" = @Id""", new { Id = id }));
    }

    public void Insert(ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            INSERT INTO "ProxyRoutes" ("Id", "RouteId", "ClusterId", "Path", "Methods", "Hosts", "Order", "IsEnabled", "Metadata", "CreatedAt", "UpdatedAt")
            VALUES (@Id, @RouteId, @ClusterId, @Path, @Methods, @Hosts, @Order, @IsEnabled, @Metadata, @CreatedAt, @UpdatedAt)
            """,
            entity));
    }

    public void Update(ProxyRouteEntity entity)
    {
        WithConnection(c => c.Execute("""
            UPDATE "ProxyRoutes"
            SET "RouteId" = @RouteId, "ClusterId" = @ClusterId, "Path" = @Path,
                "Methods" = @Methods, "Hosts" = @Hosts, "Order" = @Order,
                "IsEnabled" = @IsEnabled, "Metadata" = @Metadata, "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id
            """,
            entity));
    }

    public void Delete(string id)
    {
        WithConnection(c => c.Execute("""DELETE FROM "ProxyRoutes" WHERE "Id" = @Id""", new { Id = id }));
    }
}
```

`ApiKeyRepository.cs` 逐方法 SQL 对照（构造函数/CreateTable 按规则 1、2 处理）:

```csharp
// GetByKeyValue
"""SELECT * FROM "ApiKeys" WHERE "KeyValue" = @KeyValue AND "IsEnabled" = TRUE"""
// GetAll
"""SELECT * FROM "ApiKeys" ORDER BY "CreatedAt" DESC"""
// GetById
"""SELECT * FROM "ApiKeys" WHERE "Id" = @Id"""
// Insert
"""
INSERT INTO "ApiKeys" ("Id", "KeyValue", "Name", "Role", "IsEnabled", "CreatedAt")
VALUES (@Id, @KeyValue, @Name, @Role, @IsEnabled, @CreatedAt)
"""
// Update
"""
UPDATE "ApiKeys"
SET "Name" = @Name, "Role" = @Role, "IsEnabled" = @IsEnabled
WHERE "Id" = @Id
"""
// UpdateLastUsed（Now 参数改 DateTime）
"""
UPDATE "ApiKeys" SET "LastUsedAt" = @Now WHERE "KeyValue" = @KeyValue
"""
//   参数: new { Now = DateTime.UtcNow, KeyValue = keyValue }
// Delete
"""DELETE FROM "ApiKeys" WHERE "Id" = @Id"""
// Exists
"""SELECT COUNT(*) FROM "ApiKeys" """
```

`ClusterRepository.cs`:

```csharp
// GetAllEnabled
"""SELECT * FROM "ProxyClusters" WHERE "IsEnabled" = TRUE"""
// GetAll
"""SELECT * FROM "ProxyClusters" ORDER BY "CreatedAt" DESC"""
// GetById
"""SELECT * FROM "ProxyClusters" WHERE "Id" = @Id"""
// Insert
"""
INSERT INTO "ProxyClusters" ("Id", "ClusterId", "LoadBalancing", "HealthCheckEnabled", "IsEnabled", "CreatedAt", "UpdatedAt")
VALUES (@Id, @ClusterId, @LoadBalancing, @HealthCheckEnabled, @IsEnabled, @CreatedAt, @UpdatedAt)
"""
// Update
"""
UPDATE "ProxyClusters"
SET "ClusterId" = @ClusterId, "LoadBalancing" = @LoadBalancing,
    "HealthCheckEnabled" = @HealthCheckEnabled, "IsEnabled" = @IsEnabled, "UpdatedAt" = @UpdatedAt
WHERE "Id" = @Id
"""
// Delete
"""DELETE FROM "ProxyClusters" WHERE "Id" = @Id"""
```

`DestinationRepository.cs`:

```csharp
// GetByClusterId
"""SELECT * FROM "ProxyDestinations" WHERE "ClusterId" = @ClusterId AND "IsEnabled" = TRUE"""
// GetAllByClusterId
"""SELECT * FROM "ProxyDestinations" WHERE "ClusterId" = @ClusterId ORDER BY "CreatedAt" """.TrimEnd()
// GetAll
"""SELECT * FROM "ProxyDestinations" ORDER BY "ClusterId", "CreatedAt" """
// GetById
"""SELECT * FROM "ProxyDestinations" WHERE "Id" = @Id"""
// Insert
"""
INSERT INTO "ProxyDestinations" ("Id", "ClusterId", "DestId", "Address", "Health", "Metadata", "IsEnabled", "CreatedAt")
VALUES (@Id, @ClusterId, @DestId, @Address, @Health, @Metadata, @IsEnabled, @CreatedAt)
"""
// Update
"""
UPDATE "ProxyDestinations"
SET "DestId" = @DestId, "Address" = @Address, "Health" = @Health,
    "Metadata" = @Metadata, "IsEnabled" = @IsEnabled
WHERE "Id" = @Id
"""
// Delete / DeleteByClusterId
"""DELETE FROM "ProxyDestinations" WHERE "Id" = @Id"""
"""DELETE FROM "ProxyDestinations" WHERE "ClusterId" = @ClusterId"""
```

`L4RouteRepository.cs`:

```csharp
// GetAll
"""SELECT * FROM "ProxyL4Routes" ORDER BY "ListenPort" ASC"""
// GetAllEnabled
"""SELECT * FROM "ProxyL4Routes" WHERE "IsEnabled" = TRUE ORDER BY "ListenPort" ASC"""
// GetById / GetByListenPort
"""SELECT * FROM "ProxyL4Routes" WHERE "Id" = @Id"""
"""SELECT * FROM "ProxyL4Routes" WHERE "ListenPort" = @ListenPort"""
// Insert
"""
INSERT INTO "ProxyL4Routes"
("Id", "RouteId", "ListenPort", "Protocol", "LoadBalancingPolicy", "IdleTimeoutSeconds", "IsEnabled", "CreatedAt", "UpdatedAt")
VALUES
(@Id, @RouteId, @ListenPort, @Protocol, @LoadBalancingPolicy, @IdleTimeoutSeconds, @IsEnabled, @CreatedAt, @UpdatedAt)
"""
// Update
"""
UPDATE "ProxyL4Routes" SET
    "RouteId" = @RouteId,
    "ListenPort" = @ListenPort,
    "Protocol" = @Protocol,
    "LoadBalancingPolicy" = @LoadBalancingPolicy,
    "IdleTimeoutSeconds" = @IdleTimeoutSeconds,
    "IsEnabled" = @IsEnabled,
    "UpdatedAt" = @UpdatedAt
WHERE "Id" = @Id
"""
// Delete
"""DELETE FROM "ProxyL4Routes" WHERE "Id" = @Id"""
```

`L4DestinationRepository.cs`:

```csharp
// GetByRouteId
"""SELECT * FROM "ProxyL4Destinations" WHERE "RouteId" = @RouteId AND "IsEnabled" = TRUE"""
// GetAll
"""SELECT * FROM "ProxyL4Destinations" """
// Insert
"""
INSERT INTO "ProxyL4Destinations"
("Id", "RouteId", "TargetHost", "TargetPort", "Weight", "IsEnabled", "CreatedAt", "UpdatedAt")
VALUES
(@Id, @RouteId, @TargetHost, @TargetPort, @Weight, @IsEnabled, @CreatedAt, @UpdatedAt)
"""
// DeleteByRouteId
"""DELETE FROM "ProxyL4Destinations" WHERE "RouteId" = @RouteId"""
```

- [ ] **Step 5: DbInitService 删 InitTables**

删除 `InitTables()` 方法（第 32-41 行）及其 XML 注释。构造函数与种子方法保留。

- [ ] **Step 6: Program.cs 完成接线**

删除:
- `var dbPath = config["ProxyConfig:DbPath"] ?? "";`（第 92 行）
- `// 配置 SQLite 路径` + `DbContext.Configure(dbPath);`（第 111-112 行）
- envMappings 中 `{ "DB_PATH", "ProxyConfig:DbPath" }`
- switchMappings 中 `-db` / `--Db` 两行

替换 `dbInit.InitTables();`（第 171 行）为:

```csharp
        // 执行 schema 迁移（从 DI 取 Provider，测试环境可替换实现）
        MigrationRunner.Migrate(app.Services.GetRequiredService<IDbProvider>());
```

Banner `* DB Path` 行（第 180 行）替换为:

```csharp
        Console.WriteLine($"* Database    : {dbProvider.Name} | {dbProvider.DisplayInfo}");
```

`PrintHelp()` 中:
- `-db, --Db <path>        SQLite database file path. Default: ./proxy.db` 行替换为:

```
  --db-type <type>        Database provider: sqlite (default) | pgsql
  --db-conn <connstr>     Database connection string
```

- 环境变量段 `DB_PATH                 SQLite database path` 行替换为:

```
  DB_TYPE                 Database provider: sqlite (default) | pgsql
  DB_CONNECTION           Database connection string
```

- [ ] **Step 7: appsettings + 删除 DbContext.cs**

`appsettings.json` 改为:

```json
{
  "ProxyConfig": {
    "Port": 8080,
    "AdminKey": ""
  },
  "Database": {
    "Provider": "sqlite",
    "ConnectionString": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Yarp": "Warning"
    }
  }
}
```

检查 `appsettings.Development.json`，若含 `DbPath` 键则同步删除并补 `Database` 节。

Run: `git rm src/ProxyYARP/Data/Db/DbContext.cs`

- [ ] **Step 8: 改测试设施**

`TestDatabase.cs` 全文替换为:

```csharp
using ProxyYARP.Data.Db;
using ProxyYARP.Data.Repositories;
using ProxyYARP.Data.Services;

namespace ProxyYARP.Tests.TestHelpers;

/// <summary>
/// 为每个测试提供独立的临时 SQLite 数据库（测试结束后自动清理）
/// Repository 通过注入的 SqliteDbProvider 创建连接
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public string DbPath { get; }
    public SqliteDbProvider Provider { get; }

    // 仓储实例
    public ApiKeyRepository      KeyRepo     { get; }
    public RouteRepository       RouteRepo   { get; }
    public ClusterRepository     ClusterRepo { get; }
    public DestinationRepository DestRepo    { get; }

    // 服务实例
    public ApiKeyService      KeyService    { get; }
    public ProxyConfigService ConfigService { get; }
    public DbInitService      InitService   { get; }

    public TestDatabase()
    {
        // 每个测试使用独立的临时文件
        DbPath = Path.Combine(Path.GetTempPath(), $"proxyyarp_test_{Guid.NewGuid():N}.db");
        Provider = new SqliteDbProvider($"Data Source={DbPath};Cache=Shared;");

        // 执行迁移建表
        MigrationRunner.Migrate(Provider);

        KeyRepo     = new ApiKeyRepository(Provider);
        RouteRepo   = new RouteRepository(Provider);
        ClusterRepo = new ClusterRepository(Provider);
        DestRepo    = new DestinationRepository(Provider);
        var l4RouteRepo = new L4RouteRepository(Provider);
        var l4DestRepo  = new L4DestinationRepository(Provider);

        KeyService    = new ApiKeyService(KeyRepo);
        ConfigService = new ProxyConfigService(RouteRepo, ClusterRepo, DestRepo);
        InitService   = new DbInitService(KeyRepo, RouteRepo, ClusterRepo, DestRepo, l4RouteRepo, l4DestRepo);
    }

    /// <summary>获取一个新打开的 SQLite 连接（测试用于原生 SQL 验证）</summary>
    public Microsoft.Data.Sqlite.SqliteConnection GetConnection()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath};");
        conn.Open();
        return conn;
    }

    public void Dispose()
    {
        // 短暂等待，给 SQLite 时间关闭文件句柄
        Thread.Sleep(100);
        try { if (File.Exists(DbPath)) File.Delete(DbPath); } catch { /* 忽略清理错误 */ }
    }
}
```

`ProxyYarpWebFactory.cs` `ConfigureWebHost` 改为（删除 DbContext.Configure 与 DbPath UseSetting，改 IDbProvider 替换）:

```csharp
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // 用测试专用 SQLite 替换默认 IDbProvider
            services.RemoveAll<IDbProvider>();
            services.AddSingleton<IDbProvider>(
                new SqliteDbProvider($"Data Source={_dbPath};Cache=Shared;"));

            // 移除 UDP 代理引擎（测试环境不需要 UDP 转发）
            var udpServices = services
                .Where(sd => sd.ServiceType == typeof(IHostedService) &&
                             sd.ImplementationType?.Name == "UdpProxyEngine")
                .ToList();
            foreach (var sd in udpServices)
                services.Remove(sd);

            // 确保 L4ProxyConfigProvider 已注册
            services.TryAddSingleton<L4ProxyConfigProvider>();
        });

        // 通过环境变量注入配置（比 IConfiguration 注入更早生效）
        builder.UseSetting("ProxyConfig:AdminKey", AdminKey);
        builder.UseSetting("ProxyConfig:Port", "0"); // 随机端口
    }
```

文件头 using 中移除不再使用的 `ProxyYARP.Data.Db` 保留（需要 IDbProvider/SqliteDbProvider），确认 `Microsoft.Extensions.DependencyInjection.Extensions` 存在（RemoveAll 需要，已存在）。

- [ ] **Step 9: 构建 + 全量测试**

Run: `dotnet build; dotnet test src/ProxyYARP.Tests`
Expected: 0 error，全部 PASS

- [ ] **Step 10: 提交**

```bash
git add -A
git commit -m "refactor: Repository 注入 IDbProvider，SQL 全引号，删除静态 DbContext 与 DbPath 配置"
```

---

### Task 6: PostgreSQL 集成测试（Testcontainers）

**Files:**
- Modify: `src/ProxyYARP.Tests/ProxyYARP.Tests.csproj`（加 Testcontainers.PostgreSql、Xunit.SkippableFact）
- Create: `src/ProxyYARP.Tests/TestHelpers/PostgresFixture.cs`
- Create: `src/ProxyYARP.Tests/TestHelpers/PostgresWebFactory.cs`
- Test: `src/ProxyYARP.Tests/Integration/PostgresSmokeTests.cs`

**Interfaces:**
- Consumes: `PostgreSqlDbProvider`（Task 2）、`Program` 的 DI 迁移（Task 5）
- Produces: `PostgresFixture.Available : bool`、`PostgresFixture.ConnectionString : string`、`PostgresWebFactory(string connectionString).CreateAdminClient()`

- [ ] **Step 1: 加包**

Run:

```bash
dotnet add src/ProxyYARP.Tests package Testcontainers.PostgreSql
dotnet add src/ProxyYARP.Tests package Xunit.SkippableFact
```

Expected: 两个包加入测试 csproj（取最新稳定版）

- [ ] **Step 2: 写 Fixture 与工厂**

Create `src/ProxyYARP.Tests/TestHelpers/PostgresFixture.cs`:

```csharp
using Testcontainers.PostgreSql;

namespace ProxyYARP.Tests.TestHelpers;

/// <summary>
/// PostgreSQL 容器 Fixture（postgres:18-alpine）
/// Docker 不可用时 Available=false，测试用 Skip.IfNot 跳过
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Docker 是否可用（不可用则所有 pgsql 测试跳过）</summary>
    public bool Available { get; private set; }

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL 容器未启动");

    public async Task InitializeAsync()
    {
        Available = await IsDockerRunning();
        if (!Available) return;

        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    private static async Task<bool> IsDockerRunning()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
```

Create `src/ProxyYARP.Tests/TestHelpers/PostgresWebFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProxyYARP.Data.Db;
using ProxyYARP.Proxy.Tcp;

namespace ProxyYARP.Tests.TestHelpers;

/// <summary>PostgreSQL 版集成测试工厂：替换 IDbProvider 为容器连接</summary>
public class PostgresWebFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public PostgresWebFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbProvider>();
            services.AddSingleton<IDbProvider>(new PostgreSqlDbProvider(_connectionString));

            var udpServices = services
                .Where(sd => sd.ServiceType == typeof(IHostedService) &&
                             sd.ImplementationType?.Name == "UdpProxyEngine")
                .ToList();
            foreach (var sd in udpServices)
                services.Remove(sd);

            services.TryAddSingleton<L4ProxyConfigProvider>();
        });

        builder.UseSetting("ProxyConfig:AdminKey", ProxyYarpWebFactory.AdminKey);
        builder.UseSetting("ProxyConfig:Port", "0");
    }

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ProxyYarpWebFactory.AdminKey);
        return client;
    }
}
```

- [ ] **Step 3: 写冒烟测试**

Create `src/ProxyYARP.Tests/Integration/PostgresSmokeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProxyYARP.Tests.TestHelpers;

namespace ProxyYARP.Tests.Integration;

/// <summary>
/// PostgreSQL 端到端冒烟测试（Testcontainers）
/// 验证：迁移建表、鉴权、L7 路由/集群 CRUD、L4 路由 CRUD、bool/DateTime 映射
/// Docker 不可用时自动跳过
/// </summary>
public class PostgresSmokeTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _pg;

    public PostgresSmokeTests(PostgresFixture fixture) => _pg = fixture;

    [SkippableFact]
    public async Task Pgsql_Auth_And_L7_Crud_Flow()
    {
        Skip.IfNot(_pg.Available, "Docker 不可用，跳过 pgsql 集成测试");

        using var factory = new PostgresWebFactory(_pg.ConnectionString);
        var client = factory.CreateAdminClient();

        // 鉴权：迁移 + 种子 AdminKey 应已生效
        var keysRes = await client.GetAsync("/api/keys");
        keysRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 创建集群
        var clusterRes = await client.PostAsJsonAsync("/api/clusters", new
        {
            clusterId = "pg-cluster",
            loadBalancing = "RoundRobin"
        });
        clusterRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 创建路由
        var routeRes = await client.PostAsJsonAsync("/api/routes", new
        {
            routeId = "pg-route",
            clusterId = "pg-cluster",
            path = "/pg/{**rest}",
            order = 0
        });
        routeRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 读取验证（bool/DateTime 经 pgsql BOOLEAN/TIMESTAMPTZ 往返）
        var routes = await client.GetFromJsonAsync<List<RouteDto>>("/api/routes");
        routes.Should().Contain(r => r.RouteId == "pg-route" && r.IsEnabled);
        routes!.First(r => r.RouteId == "pg-route").CreatedAt.Should().NotBeNullOrWhiteSpace();

        // 禁用路由（bool 写回）
        var route = routes.First(r => r.RouteId == "pg-route");
        var updateRes = await client.PutAsJsonAsync($"/api/routes/{route.Id}", new
        {
            routeId = "pg-route",
            clusterId = "pg-cluster",
            path = "/pg/{**rest}",
            order = 0,
            isEnabled = false
        });
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<List<RouteDto>>("/api/routes");
        after!.First(r => r.RouteId == "pg-route").IsEnabled.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Pgsql_L4_Crud_Flow()
    {
        Skip.IfNot(_pg.Available, "Docker 不可用，跳过 pgsql 集成测试");

        using var factory = new PostgresWebFactory(_pg.ConnectionString);
        var client = factory.CreateAdminClient();

        var createRes = await client.PostAsJsonAsync("/api/tcp-routes", new
        {
            routeId = "pg-l4",
            listenPort = 15999,
            loadBalancingPolicy = "RoundRobin",
            destinations = new[]
            {
                new { targetHost = "127.0.0.1", targetPort = 9001, weight = 1 }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<List<L4RouteDto>>("/api/tcp-routes");
        list.Should().Contain(r => r.RouteId == "pg-l4" && r.IsEnabled && r.ListenPort == 15999);
    }

    private sealed class RouteDto
    {
        public string Id { get; set; } = "";
        public string RouteId { get; set; } = "";
        public bool IsEnabled { get; set; }
        public string CreatedAt { get; set; } = "";
    }

    private sealed class L4RouteDto
    {
        public string Id { get; set; } = "";
        public string RouteId { get; set; } = "";
        public int ListenPort { get; set; }
        public bool IsEnabled { get; set; }
    }
}
```

L4 路由 API 已确认为 `/api/tcp-routes`（`L4RoutesApi.cs:15` MapGroup），响应 DTO 含 `RouteId`/`ListenPort`/`IsEnabled`。

- [ ] **Step 4: 跑 pgsql 测试**

Run: `dotnet test src/ProxyYARP.Tests --filter "FullyQualifiedName~PostgresSmokeTests"`
Expected: Docker 可用 → 2 PASS；不可用 → 2 SKIPPED（均为绿）

- [ ] **Step 5: 全量回归 + 提交**

Run: `dotnet test src/ProxyYARP.Tests`
Expected: 全部 PASS/SKIP，无 FAIL

```bash
git add src/ProxyYARP.Tests/
git commit -m "test: Testcontainers pgsql 集成冒烟测试（Docker 不可用自动跳过）"
```

---

### Task 7: Docker + compose + README + 版本 + AOT 发布验证

**Files:**
- Modify: `src/ProxyYARP/Dockerfile`
- Create: `docker-compose.yml`（仓库根）
- Modify: `README.md`（环境变量表 + pgsql 章节）
- Modify: `src/ProxyYARP/ProxyYARP.csproj`（Version 1.0.1 → 1.1.0）

- [ ] **Step 1: Dockerfile 环境变量更新**

第 26 行 `ENV DB_PATH=/app/data/proxy.db` 替换为:

```dockerfile
ENV DB_TYPE=sqlite
ENV DB_CONNECTION="Data Source=/app/data/proxy.db;Cache=Shared;"
```

- [ ] **Step 2: 新建 docker-compose.yml（仓库根）**

```yaml
services:
  postgres:
    image: postgres:18-alpine
    environment:
      POSTGRES_USER: proxyyarp
      POSTGRES_PASSWORD: proxyyarp
      POSTGRES_DB: proxyyarp
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U proxyyarp -d proxyyarp"]
      interval: 5s
      timeout: 3s
      retries: 12

  proxy:
    build:
      context: .
      dockerfile: src/ProxyYARP/Dockerfile
    environment:
      DB_TYPE: pgsql
      DB_CONNECTION: Host=postgres;Port=5432;Database=proxyyarp;Username=proxyyarp;Password=proxyyarp
      ACCESS_KEY: change-me-on-first-run
    ports:
      - "8080:8080"
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped

volumes:
  pgdata:
```

- [ ] **Step 3: README 更新**

先 Read README.md 定位环境变量说明段，然后：
1. 删除 `DB_PATH` 行，替换为:

```
| `DB_TYPE` | 数据库类型：`sqlite`（默认）/ `pgsql` | `sqlite` |
| `DB_CONNECTION` | 数据库连接字符串（pgsql 必填；sqlite 留空用默认路径） | |
```

2. 新增「使用 PostgreSQL」小节，内容:

````markdown
### 使用 PostgreSQL

```bash
docker run -e DB_TYPE=pgsql \
  -e DB_CONNECTION="Host=your-pg;Port=5432;Database=proxyyarp;Username=proxyyarp;Password=***" \
  -e ACCESS_KEY=your-admin-key \
  -p 8080:8080 proxyyarp:latest
```

或使用 compose 一键启动（proxy + postgres）：

```bash
docker compose up -d
```
````

- [ ] **Step 4: 版本号 → 1.1.0**

`ProxyYARP.csproj` 第 12 行 `<Version>1.0.1</Version>` → `<Version>1.1.0</Version>`

- [ ] **Step 5: AOT 发布验证（零警告 + 可运行）**

Run: `dotnet publish src/ProxyYARP -c Release -r win-x64 -o artifacts/publish-check`
Expected: 发布成功；输出中**无新增** Npgsql/Dapper 相关 IL 警告（IL2026/IL3050 已在 NoWarn，关注 IL2070/IL2072 等新警告）

运行冒烟:

```bash
./artifacts/publish-check/ProxyYARP.exe -p 18080 -k testkey
```

另起终端: `curl http://localhost:18080/api/version`
Expected: 返回版本 JSON；启动 Banner 显示 `* Database    : sqlite | Data Source=...proxy.db;Cache=Shared;`。验证后 Ctrl+C 停止并删除 `artifacts/publish-check`。

- [ ] **Step 6: Docker 构建验证（可选，需 Docker）**

Run: `docker build -f src/ProxyYARP/Dockerfile -t proxyyarp:1.1.0 .`
Expected: 构建成功；`docker images proxyyarp:1.1.0` 体积 ≤ ~50 MB（基线 44M + Npgsql ~4M）

- [ ] **Step 7: 全量测试 + 提交**

Run: `dotnet test src/ProxyYARP.Tests`
Expected: 全部 PASS/SKIP

```bash
git add -A
git commit -m "feat: 多数据库支持 sqlite+pgsql（DB_TYPE/DB_CONNECTION），compose 编排，v1.1.0"
```

---

## 风险与回退记录

| 风险 | 触发点 | 应对 |
|---|---|---|
| Dapper.AOT 不支持 sqlite INTEGER→bool / TEXT→DateTime 转换 | Task 4 Step 6 | 停止上报；回退方案为 SELECT 显式列 + Provider 方言 SQL，禁止静默绕过 |
| Npgsql AOT 发布新警告 | Task 7 Step 5 | 分析警告来源；必要时 rd.xml 补 root 或调整 Npgsql 用法 |
| UseSetting("ProxyConfig:AdminKey") 不到达 Program 手动配置 | Task 6 冒烟 401 | 与现有 sqlite 工厂同机制，若现有机制失效则改 PostgresWebFactory 用环境变量注入 |
| Testcontainers 拉取 postgres:18-alpine 慢 | Task 6 | 首次运行预拉 `docker pull postgres:18-alpine` |
