using Microsoft.Data.Sqlite;

// ============================================================================
// qdb —— ProxyYARP 配置库只读检视工具（诊断辅助，非产品代码）
//
// 用途：不起服务、不依赖 Web API，直接打开 SQLite 库查看运行时配置。
//       典型场景：多个实例端口冲突时，确认 L4 规则属于哪个 Group。
//
// 用法：
//   dotnet run --project docs/demo/qdb                      # 读默认库，表格输出
//   dotnet run --project docs/demo/qdb -- <库路径>           # 指定库
//   dotnet run --project docs/demo/qdb -- <库路径> --json    # JSON 输出（便于脚本处理）
// ============================================================================

var dbArg = args.FirstOrDefault(a => !a.StartsWith("--"));
var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);

// DB/ 被 .gitignore 忽略，所以这里按"仓库根目录"定位，而不是写死绝对路径。
// 无论从仓库根还是子目录执行都能找到；找不到就明确报错而不是静默失败。
var dbPath = dbArg is not null
    ? Path.GetFullPath(dbArg)
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "DB", "test.db"));

if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"[qdb] 找不到数据库文件: {dbPath}");
    Console.Error.WriteLine("       用法: dotnet run --project docs/demo/qdb -- <库路径> [--json]");
    return 1;
}

// 只读打开：检视工具不应产生 WAL 或改库，避免干扰正在运行的实例。
var cs = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadOnly
}.ToString();

using var conn = new SqliteConnection(cs);
conn.Open();

if (json)
    PrintJson();
else
    PrintText();

return 0;

// ─────────────────────────── 输出：人类可读 ───────────────────────────

void PrintText()
{
    Console.WriteLine($"数据库: {dbPath}");
    Console.WriteLine(new string('─', 62));

    Section("分组与配置版本", "ConfigGroups",
        "SELECT \"Id\" AS 分组ID, \"Name\" AS 名称, \"ConfigVersion\" AS 配置版本 FROM \"ProxyYARP_ConfigGroups\" ORDER BY \"Id\"");

    Section("L4 路由（谁监听哪个端口）", "Routes",
        """
        SELECT r."RouteId"     AS 规则,
               r."GroupId"     AS 归属分组,
               r."ListenPort"  AS 监听端口,
               CASE WHEN r."IsEnabled" = 1 THEN '启用' ELSE '禁用' END AS 状态,
               r."Protocol"    AS 协议
        FROM "ProxyYARP_L4Routes" r
        ORDER BY r."ListenPort"
        """);

    Section("L4 目标节点", "Destinations",
        """
        SELECT d."RouteId"    AS 规则,
               d."GroupId"    AS 归属分组,
               d."TargetHost" AS 目标主机,
               d."TargetPort" AS 目标端口,
               d."Weight"     AS 权重
        FROM "ProxyYARP_L4Destinations" d
        ORDER BY d."RouteId"
        """);

    Section("L7 路由", "Routes",
        """
        SELECT r."RouteId"   AS 规则,
               r."ClusterId" AS 集群,
               r."GroupId"   AS 归属分组,
               r."Path"      AS 路径,
               r."Order"     AS 优先级
        FROM "ProxyYARP_Routes" r
        ORDER BY r."GroupId", r."Order"
        """);

    Section("网站代理白名单", "Websites",
        """
        SELECT w."Name"          AS 名称,
               w."GroupId"       AS 归属分组,
               w."TargetUrl"     AS 目标网站,
               w."HostAuthority" AS 匹配用authority,
               CASE WHEN w."RewriteBody"    = 1 THEN '改' ELSE '-' END  AS 改正文,
               CASE WHEN w."RewriteCookies" = 1 THEN '隔' ELSE '-' END  AS 隔Cookie,
               CASE WHEN w."IsEnabled"      = 1 THEN '启用' ELSE '禁用' END AS 状态
        FROM "ProxyYARP_Websites" w
        ORDER BY w."GroupId", w."CreatedAt"
        """);
}

void Section(string title, string table, string sql)
{
    var rows = Query(sql);
    Console.WriteLine($"■ {title}  (共 {rows.Count} 条)");
    if (rows.Count == 0)
    {
        Console.WriteLine("  (空)");
        Console.WriteLine();
        return;
    }

    var headers = rows[0].Keys.ToList();
    var widths = headers.ToDictionary(h => h, h => h.Length);
    foreach (var row in rows)
    foreach (var h in headers)
        widths[h] = Math.Max(widths[h], DisplayWidth(row[h]));

    var line = "  " + string.Join(" │ ", headers.Select(h => h.PadRight(widths[h])));
    Console.WriteLine(line);
    Console.WriteLine("  " + string.Join("─┼─", headers.Select(h => new string('─', widths[h] + 1))));
    foreach (var row in rows)
        Console.WriteLine("  " + string.Join(" │ ", headers.Select(h => Pad(row[h], widths[h]))));
    Console.WriteLine();
}

// ─────────────────────────── 输出：JSON ───────────────────────────

void PrintJson()
{
    var result = new Dictionary<string, List<Row>>(StringComparer.Ordinal)
    {
        ["configGroups"] = Query("SELECT * FROM \"ProxyYARP_ConfigGroups\" ORDER BY \"Id\""),
        ["l4Routes"] = Query("SELECT * FROM \"ProxyYARP_L4Routes\" ORDER BY \"ListenPort\""),
        ["l4Destinations"] = Query("SELECT * FROM \"ProxyYARP_L4Destinations\" ORDER BY \"RouteId\""),
        ["l7Routes"] = Query("SELECT * FROM \"ProxyYARP_Routes\" ORDER BY \"GroupId\", \"Order\""),
        ["websites"] = Query("SELECT * FROM \"ProxyYARP_Websites\" ORDER BY \"GroupId\", \"CreatedAt\""),
    };

    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        AppJsonContext.Default.DictionaryStringListRow));
}

// ─────────────────────────── 查询与对齐 ───────────────────────────

/// <summary>执行查询。表不存在时返回空列表而不是抛异常——旧版本库可能还没建这张表。</summary>
List<Row> Query(string sql)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    var result = new List<Row>();
    try
    {
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return result;
        do
        {
            var row = new Row();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            result.Add(row);
        } while (reader.Read());
    }
    catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
    {
        // 迁移尚未执行到该版本，视作空表处理
    }
    return result;
}

static string Display(object? val) => val?.ToString() ?? "(null)";

/// <summary>按显示宽度对齐（中文算 2 列，保证表格不错位）</summary>
static int DisplayWidth(object? val)
{
    var s = Display(val);
    var w = 0;
    foreach (var ch in s)
        w += IsWide(ch) ? 2 : 1;
    return w;
}

static string Pad(object? val, int width)
{
    var s = Display(val);
    var w = 0;
    foreach (var ch in s)
        w += IsWide(ch) ? 2 : 1;
    return s + new string(' ', Math.Max(0, width - w));
}

static bool IsWide(char c) => c is >= '\u1100' and <= '\u115F'
    or >= '\u2E80' and <= '\uA4CF'
    or >= '\uAC00' and <= '\uD7A3'
    or >= '\uF900' and <= '\uFAFF'
    or >= '\uFE30' and <= '\uFE6F'
    or >= '\uFF00' and <= '\uFF60'
    or >= '\uFFE0' and <= '\uFFE6';

/// <summary>一列查询结果（字段名 → 值）</summary>
sealed class Row : Dictionary<string, object?>
{
    public Row() : base(StringComparer.Ordinal) { }
}

// SQLite 的 INTEGER/TIMESTAMP 会分别映射到 Int64 / String，都要注册，
// 否则 source-gen 的 TypeInfoResolver 会在运行期抛 NotSupportedException。
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, List<Row>>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(long))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
