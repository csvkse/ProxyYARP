# qdb — 配置库只读检视工具

不起服务、不开 Web API、不需要管理 Key，直接打开 SQLite 库查看 ProxyYARP 的运行时配置。

## 为什么需要它

排查多节点问题时，控制面 API 拿不到（Key 不在手里、管理端被 `MANAGEMENT_ENABLED=false` 关掉、或者端口还没起来），这时候最可靠的办法是直接看库。

**典型场景**：`start-test.bat` 起了三个实例，其中某个报 `SocketException (10048) 端口被占用`。用 qdb 一眼就能看出是哪条 L4 规则、归属哪个分组：

```
■ L4 路由（谁监听哪个端口）  (共 1 条)
  规则    │ 归属分组    │ 监听端口 │ 状态 │ 协议
  ─────────┼────────────┼──────────┼──────┼──────
  tes     │ group-web  │ 8091     │ 启用 │ TCP
```

L4 规则按 `GroupId` 对同组所有节点生效，与 `-p` 参数无关——所以同组多个实例会互相抢端口。

## 运行

```bash
# 指定库路径（表格输出）
dotnet run --project docs/demo/qdb -- "DB/test.db"

# JSON 输出（便于脚本 / jq 处理）
dotnet run --project docs/demo/qdb -- "DB/test.db" --json

# 不传参数时，默认读仓库根的 DB/test.db
dotnet run --project docs/demo/qdb
```

退出码：库不存在或无法打开返回 `1`，正常返回 `0`，可用于脚本判断。

## 查看内容

| 分类 | 表 | 说明 |
|------|-----|------|
| 分组与配置版本 | `ProxyYARP_ConfigGroups` | 每个分组的 `ConfigVersion`，YARP 热重载就是靠比对这个值 |
| L4 路由 | `ProxyYARP_L4Routes` | **监听端口 + 归属分组**，排查端口冲突看这里 |
| L4 目标节点 | `ProxyYARP_L4Destinations` | 转发目标 `host:port` 与权重 |
| L7 路由 | `ProxyYARP_Routes` | 路径匹配、优先级、关联集群 |
| 网站代理白名单 | `ProxyYARP_Websites` | 已登记站点、匹配用 authority、改写开关状态 |

## 实现要点

### 只读打开，不干扰运行中的实例

```csharp
Mode = SqliteOpenMode.ReadOnly
```

检视工具绝不能写库。若用默认的 `ReadWrite` 打开，可能会触发 checkpoint、产生 `-wal`/`-shm` 变动，甚至和正在运行的实例抢文件锁。

### 表不存在视作空表，不抛异常

旧版本库尚未执行 `Migration 3` 时没有 `ProxyYARP_Websites` 表。工具对这种情况返回空列表而不是崩溃——否则跨版本排查时反而成了障碍：

```csharp
catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
{
    // 迁移尚未执行到该版本，视作空表处理
}
```

### 中文按 2 列宽对齐

表头含中文，按 `Length` 补齐会让表格错位。这里按东亚字符宽度计算，保证 `归属分组` 这类表头对齐：

```csharp
static bool IsWide(char c) => c is >= '\u1100' and <= '\u115F'
    or >= '\u2E80' and <= '\uA4CF'
    or >= '\uAC00' and <= '\uD7A3'
    or >= '\uF900' and <= '\uFAFF'
    ...
```

### JSON 模式用 source-gen 上下文

`System.Text.Json` 属性名默认大小写不敏感匹配，但 `Dictionary<string, object?>` 的值在运行期可能是 `long`（SQLite INTEGER）。不注册就会抛 `NotSupportedException`：

```csharp
[JsonSerializable(typeof(Dictionary<string, List<Row>>))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(string))]
partial class AppJsonContext : JsonSerializerContext;
```

### 路径按仓库根定位

`DB/` 被 `.gitignore` 忽略，写死绝对路径别人拿不到库。默认路径由输出目录相对推出：

```csharp
Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "..", "DB", "test.db"));
```

从仓库根或子目录执行都能找到；找不到则明确报错并提示用法。

## 与主项目的关系

- **不在 `ProxyYARP.sln` 中**。解决方案是硬编码项目列表，`dotnet build ProxyYARP.sln` 不会编译它，因此不参与主工程的 Native AOT 发布。
- **依赖主项目同款 `Microsoft.Data.Sqlite`**，保证读的是同一套文件格式。
- **构建产物已被忽略**。`.gitignore` 的 `bin/`、`obj/` 是全局规则，自动覆盖本目录。

## 单独构建

```bash
dotnet build docs/demo/qdb/qdb.csproj
```
