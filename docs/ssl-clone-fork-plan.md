# SSL 证书 / 配置克隆 / 分组 Fork 功能实现方案

> **版本**：v1.0  
> **日期**：2026-07-19  
> **状态**：待执行  
>
> 本文档综合了初稿讨论与框架审核报告，是最终可执行的技术方案。涵盖三大功能模块：
> 1. **SSL 证书管理**（手动上传 + ACME 自动申请 + 热加载 + 跨组授权）
> 2. **L7 / L4 配置克隆与迁移**（路由+集群原子复制、跨组转移）
> 3. **分组 Fork / 模板克隆**（整组配置一键复制）

---

## 一、核心设计决策（已定论）

### 1.1 HTTP 与 HTTPS 端口

HTTP 和 HTTPS **必须分开监听两个端口**。TLS 握手的首字节（`0x16 0x03`）与 HTTP 明文协议不兼容，同一端口无法自动识别协议。

```
Port 80  / PROXY_PORT   → HTTP（现有行为不变）
Port 443 / HTTPS_PORT   → HTTPS（新增，不配置则不启用）
```

### 1.2 证书存储模型：全局资源 + 多对多授权

证书不绑定 `GroupId`，而是全局资源，通过授权关联表与分组挂钩。

```
ProxyYARP_Certificates  (全局)
        ↕  多对多
ProxyYARP_CertificateGrants  (关联表)
        ↕
ProxyYARP_ConfigGroups  (分组)
```

- A 节点申请的证书 → 写入全局表 → B 节点（同组）直接读取，零额外操作  
- 跨组共享 = 新增一条 Grant 记录，不复制证书数据  
- 跨组迁移 = 撤销源组 Grant + 新增目标组 Grant

### 1.3 ACME 实现：手写精简客户端（不使用 Certes）

`Certes` 依赖 Bouncy Castle（大量反射），**与本项目的 Native AOT 严格模式不兼容**。

手写 ACME HTTP-01 客户端，仅依赖：
- `HttpClient` — ACME REST 调用
- `ECDsa.Create()` — ACME 账户密钥（P-256）
- `RSA.Create()` — 证书私钥
- `CertificateRequest` — 生成 CSR
- `System.Text.Json` (Source Gen) — JSON 序列化

以上全部 AOT 兼容，是 .NET 原生 API，无需第三方库。

### 1.4 路由/集群克隆模型：复制（非共享）

路由和集群是"配置"，不是"资源"，每个组的后端地址不同，不适合共享。  
克隆操作：深度复制整个依赖链（Route → Cluster → Destinations），目标组独立维护。

### 1.5 私钥保护：`ICertificateProtector` 接口

私钥通过可插拔接口保护，默认 NoOp，用户设置 `CERT_ENCRYPTION_KEY` 后自动切换为 AES-GCM。

---

## 二、数据库变更（Migration）

### Migration 3 — 证书相关表

同时添加到 `SqliteDbProvider.cs` 和 `PostgreSqlDbProvider.cs`。

**SQLite 版本：**
```sql
-- 全局证书表（不绑定 GroupId）
CREATE TABLE "ProxyYARP_Certificates" (
    "Id"                  TEXT PRIMARY KEY,
    "Domain"              TEXT NOT NULL UNIQUE,   -- 支持通配符 *.example.com
    "CertPem"             TEXT NOT NULL,          -- 证书链 PEM
    "KeyPem"              TEXT NOT NULL,          -- 私钥 PEM（可被 ICertificateProtector 加密）
    "ExpiresAt"           TEXT NOT NULL,
    "Provider"            TEXT NOT NULL DEFAULT 'manual',  -- manual | letsencrypt
    "AutoRenew"           INTEGER NOT NULL DEFAULT 0,
    "Status"              TEXT NOT NULL DEFAULT 'active',  -- pending | active | expired | error
    "LastRenewalAttempt"  TEXT,                   -- 防多节点并发续期的乐观锁
    "CreatedAt"           TEXT NOT NULL,
    "UpdatedAt"           TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS "idx_certs_domain" ON "ProxyYARP_Certificates"("Domain");

-- 证书授权关联表（多对多）
CREATE TABLE "ProxyYARP_CertificateGrants" (
    "CertificateId" TEXT NOT NULL,
    "GroupId"       TEXT NOT NULL,
    "GrantedAt"     TEXT NOT NULL,
    PRIMARY KEY ("CertificateId", "GroupId"),
    FOREIGN KEY ("CertificateId") REFERENCES "ProxyYARP_Certificates"("Id") ON DELETE CASCADE,
    FOREIGN KEY ("GroupId") REFERENCES "ProxyYARP_ConfigGroups"("Id") ON DELETE CASCADE
);
```

**PostgreSQL 版本差异：**  
`INTEGER` → `BOOLEAN`，`TEXT` 日期 → `TIMESTAMPTZ`，其余结构相同。

### Migration 4 — 全局配置表 + ACME Challenge 表

```sql
-- 全局键值配置（ACME 账户、未来扩展用）
CREATE TABLE "ProxyYARP_GlobalSettings" (
    "Key"   TEXT PRIMARY KEY,
    "Value" TEXT NOT NULL
);
-- 存储项示例：
--   Key = "acme.account.json"   → ACME 账户私钥 JSON（全局唯一）
--   Key = "acme.directory"      → Let's Encrypt 目录 URL

-- ACME HTTP-01 验证临时表（所有节点均可应答）
CREATE TABLE "ProxyYARP_AcmeChallenges" (
    "Token"            TEXT PRIMARY KEY,
    "KeyAuthorization" TEXT NOT NULL,
    "ExpiresAt"        TEXT NOT NULL   -- 后台任务定期清理
);
```

---

## 三、新增与修改文件总览

### 3.1 模块结构图（新增文件）

```
ProxyYARP/
├── Proxy/
│   └── Ssl/                              ← 新增目录
│       ├── HttpsProxyModule.cs           ← [NEW] 遵循 IProxyModule 接口
│       ├── SslCertificateStore.cs        ← [NEW] 内存缓存 + SNI 选择器
│       ├── AcmeCertificateService.cs     ← [NEW] 手写 ACME HTTP-01 客户端
│       ├── CertificateRenewalService.cs  ← [NEW] 后台自动续期 HostedService
│       ├── ICertificateProtector.cs      ← [NEW] 私钥保护接口
│       ├── NoOpCertificateProtector.cs   ← [NEW] 默认：不加密
│       └── AesGcmCertificateProtector.cs ← [NEW] 可选：AES-GCM 加密
├── Data/
│   ├── Models/
│   │   └── CertificateEntity.cs         ← [NEW]
│   ├── Repositories/
│   │   └── CertificateRepository.cs     ← [NEW]
│   └── Services/
│       └── GroupService.cs              ← [NEW] Fork 业务逻辑
└── Api/
    └── CertificatesApi.cs               ← [NEW]
```

### 3.2 需修改的现有文件

| 文件 | 修改内容 |
|------|---------|
| `Data/Db/SqliteDbProvider.cs` | 添加 Migration 3、4 |
| `Data/Db/PostgreSqlDbProvider.cs` | 添加 Migration 3、4 |
| `Data/Services/ProxyConfigService.cs` | 新增 `CloneRoute` / `MoveRoute` / `CloneCluster` |
| `Data/Services/L4ConfigService.cs` | 新增 `CloneL4Route` / `MoveL4Route` |
| `Data/Repositories/ProxyConfigGroupRepository.cs` | `GetGroupDetails()` SQL 补充 `CertCount` |
| `Cluster/NodeHeartbeatService.cs` | 分组切换时追加 `certStore.Reload()` |
| `Api/NodesApi.cs` | `GroupDetailDto` 新增 `CertCount`；新增 Group 创建 + Fork API |
| `Api/RoutesApi.cs` | 新增 `clone-to` / `move-to` 端点 |
| `Api/ClustersApi.cs` | 新增 `clone-to` 端点 |
| `Api/L4RoutesApi.cs` | 新增 `clone-to` / `move-to` 端点 |
| `Serialization/AppJsonContext.cs` | 注册所有新 DTO（见第七节） |
| `Program.cs` | 注册 `HttpsProxyModule`；注册新服务；注册新 API |

---

## 四、模块一：SSL 证书管理

### 4.1 `HttpsProxyModule.cs` — 封装 HTTPS 功能

遵循现有 `IProxyModule` 接口，加入 `proxyModules[]` 数组。

```csharp
// Proxy/Ssl/HttpsProxyModule.cs
public class HttpsProxyModule : IProxyModule
{
    private readonly int? _httpsPort;
    internal SslCertificateStore? _certStore;   // Build() 后由 Program.cs 回填

    public HttpsProxyModule(int? httpsPort) { _httpsPort = httpsPort; }

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<CertificateRepository>();
        services.AddSingleton<SslCertificateStore>();
        services.AddSingleton<AcmeCertificateService>();
        services.AddHostedService<CertificateRenewalService>();
        // 私钥保护：有加密 Key 则用 AES-GCM，否则 NoOp
        // (由 Program.cs 根据配置注册，见 4.6)
    }

    public void ConfigureKestrel(KestrelServerOptions options)
    {
        if (!_httpsPort.HasValue) return;
        options.ListenAnyIP(_httpsPort.Value, listenOptions =>
        {
            listenOptions.UseHttps(httpsOptions =>
            {
                // 闭包捕获 this；ServerCertificateSelector 是运行时每次 TLS 握手回调
                // 此时 Build() 已完成，_certStore 必然不为 null
                httpsOptions.ServerCertificateSelector = (_, sniName) =>
                    _certStore?.SelectCertificate(sniName);
            });
        });
    }

    public void ConfigurePipeline(WebApplication app)
    {
        // ACME challenge 路由必须在 YARP catch-all 之前注册！
        app.Map("/.well-known/acme-challenge/{token}",
            async (string token, CertificateRepository repo) =>
            {
                var ch = repo.GetAcmeChallenge(token);
                return ch == null ? Results.NotFound() : Results.Text(ch.KeyAuthorization);
            }).AllowAnonymous();
    }
}
```

**`Program.cs` 仅需修改三处：**

```csharp
// 1. 读取 HTTPS 端口配置
var httpsPort = config.GetValue<int?>("ProxyConfig:HttpsPort");

// 2. 将模块加入数组
var proxyModules = new IProxyModule[]
{
    new YarpProxyModule(),
    new TcpProxyModule(),
    new HttpsProxyModule(httpsPort)   // ← 新增一行
};

// 3. Build() 之后，回填 DI 实例解决时序问题
var app = builder.Build();
var httpsModule = proxyModules.OfType<HttpsProxyModule>().FirstOrDefault();
if (httpsModule != null)
    httpsModule._certStore = app.Services.GetRequiredService<SslCertificateStore>();
```

### 4.2 `SslCertificateStore.cs` — 内存证书缓存

```csharp
// Proxy/Ssl/SslCertificateStore.cs
public class SslCertificateStore
{
    // domain → X509Certificate2，支持精确匹配和通配符
    private readonly ConcurrentDictionary<string, X509Certificate2> _cache = new();

    // SNI 选择器回调
    public X509Certificate2? SelectCertificate(string? sniName)
    {
        if (string.IsNullOrEmpty(sniName)) return null;

        // 1. 精确匹配
        if (_cache.TryGetValue(sniName.ToLowerInvariant(), out var cert)) return cert;

        // 2. 通配符匹配（*.example.com → api.example.com）
        var parts = sniName.Split('.', 2);
        if (parts.Length == 2)
        {
            var wildcard = "*." + parts[1].ToLowerInvariant();
            if (_cache.TryGetValue(wildcard, out var wcert)) return wcert;
        }
        return null;
    }

    // 热重载：从 DB 加载指定分组的授权证书
    public void Reload(string groupId, CertificateRepository repo,
                       ICertificateProtector protector)
    {
        var certs = repo.GetGrantedToGroup(groupId);
        var newCache = new ConcurrentDictionary<string, X509Certificate2>();
        foreach (var entity in certs)
        {
            try
            {
                var keyPem = protector.Unprotect(entity.KeyPem);
                var x509 = X509Certificate2.CreateFromPem(entity.CertPem, keyPem);
                newCache[entity.Domain.ToLowerInvariant()] = x509;
            }
            catch (Exception ex)
            {
                // 解析失败不阻断其他证书加载
                Console.Error.WriteLine($"[SSL] Failed to load cert for {entity.Domain}: {ex.Message}");
            }
        }
        // 原子替换缓存
        _cache.Clear();
        foreach (var (k, v) in newCache) _cache[k] = v;
    }
}
```

### 4.3 `AcmeCertificateService.cs` — ACME HTTP-01 申请

**整体流程（全部使用 .NET 原生 API）：**

```
Step 1: 读取/创建 ACME 账户（ECDsa P-256 密钥，存 GlobalSettings）
Step 2: POST /acme/new-order        → 创建订单
Step 3: GET  /acme/authz/{id}       → 获取 challenge token
Step 4: DB.SaveAcmeChallenge(token, keyAuthorization)
          ↑ 写库后，所有节点的 /.well-known/acme-challenge/{token} 均可应答
Step 5: POST /acme/challenge        → 通知 CA 开始验证
Step 6: 轮询订单状态（有效期内最多 60s，每 3s 一次）
Step 7: RSA.Create(2048)            → 生成证书私钥
Step 8: CertificateRequest          → 构建 CSR
Step 9: POST /acme/finalize         → 提交 CSR
Step 10: GET  /acme/certificate     → 下载 PEM 证书链
Step 11: DB.Upsert(cert)            → 证书写库
Step 12: SslCertificateStore.Reload() → 热更新，零停机
```

关键签名方法：
```csharp
public async Task<AcmeResult> RequestCertificateAsync(
    string domain,
    string groupId,
    bool autoRenew,
    CancellationToken ct = default);
```

### 4.4 `CertificateRenewalService.cs` — 后台自动续期

**防多节点并发续期（乐观锁方案）：**

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        // 每天凌晨 3:00 检查
        await WaitUntilNextCheckAsync(hour: 3, stoppingToken);

        var expiring = _certRepo.GetExpiringAutoRenew(daysAhead: 30);
        foreach (var cert in expiring)
        {
            // 原子 CAS：抢占续期令牌（2小时内只允许一个节点续期）
            bool grabbed = _certRepo.TryMarkRenewalAttempt(cert.Id,
                threshold: DateTime.UtcNow.AddHours(-2));
            if (!grabbed) continue;   // 其他节点已在处理，跳过

            try
            {
                await _acmeService.RenewCertificateAsync(cert, stoppingToken);
                _certStore.Reload(_identityManager.GroupId, _certRepo, _protector);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to renew certificate for {Domain}", cert.Domain);
            }
        }

        // 清理过期的 ACME Challenge 记录
        _certRepo.CleanExpiredChallenges();
    }
}
```

### 4.5 `ICertificateProtector.cs` — 私钥保护接口

```csharp
// Proxy/Ssl/ICertificateProtector.cs
public interface ICertificateProtector
{
    string Protect(string plaintext);    // 写入 DB 前调用
    string Unprotect(string ciphertext); // 从 DB 读出后调用
}

// 默认：明文（内网部署，DB 访问权限已受控）
public class NoOpCertificateProtector : ICertificateProtector
{
    public string Protect(string p) => p;
    public string Unprotect(string c) => c;
}

// 可选：AES-256-GCM（需要 CERT_ENCRYPTION_KEY 环境变量）
public class AesGcmCertificateProtector : ICertificateProtector
{
    // AES-GCM 加密，Key 从启动参数读取，IV 随机生成并附在密文前缀
    ...
}
```

### 4.6 证书 API 端点

```
# 证书 CRUD
GET    /api/certificates                    → 查所有证书（含授权组列表）
GET    /api/certificates/{id}               → 查单个证书详情
POST   /api/certificates                    → 手动上传证书（PFX or PEM+KEY）
PUT    /api/certificates/{id}               → 更新元数据（AutoRenew 等）
DELETE /api/certificates/{id}               → 删除（级联删 Grants）

# ACME 申请与续期
POST   /api/certificates/acme              → 发起 ACME 申请
       Body: { domain, groupId, autoRenew }
POST   /api/certificates/{id}/renew        → 手动触发续期

# 跨组授权管理
GET    /api/certificates/{id}/grants            → 查看授权的组列表
POST   /api/certificates/{id}/grants            → 授权给 Group { groupId }
DELETE /api/certificates/{id}/grants/{groupId}  → 撤销 Group 授权
```

### 4.7 `NodeHeartbeatService.cs` 修改点

分组切换完成后，追加证书缓存重载：

```csharp
// 原有代码
configProvider?.ForceReload();

// 新增（紧随其后）
var certStore = scope.ServiceProvider.GetService<SslCertificateStore>();
var certRepo  = scope.ServiceProvider.GetService<CertificateRepository>();
var protector = scope.ServiceProvider.GetService<ICertificateProtector>();
if (certStore != null && certRepo != null && protector != null)
    certStore.Reload(_identityManager.GroupId, certRepo, protector);
```

---

## 五、模块二：L7 / L4 配置克隆与迁移

### 5.1 通用返回类型

```csharp
// Data/Services/ 中定义，注册到 AppJsonContext
public record CloneResult(
    bool Success,
    List<string> NewIds,       // 新建的实体 Id 列表
    List<string> Conflicts,    // 冲突描述（有则 Success=false）
    string? ErrorMessage
);
```

### 5.2 设计原则

- 克隆时所有子实体 `Id` 重新生成（`Guid.NewGuid()`），`GroupId` 改为目标组
- 克隆/迁移在**单一事务**内完成，失败则回滚
- 操作完成后**源组和目标组**的 `ConfigVersion` 均自增（触发 YARP 热重载）
- 提前检测冲突，返回 `409 Conflict`，不进入事务

### 5.3 `ProxyConfigService.cs` 新增方法

```csharp
// 克隆路由（含关联 Cluster + Destinations）到目标组
// - 若路由的 ClusterId 在目标组已存在，不重复克隆 Cluster（共用）
// - 返回 409 如果 RouteId 冲突
public CloneResult CloneRoute(string sourceGroupId, string routeId, string targetGroupId);

// 批量克隆多条路由
public CloneResult CloneRoutes(string sourceGroupId,
    IEnumerable<string> routeIds, string targetGroupId);

// 迁移（克隆 + 删源，原子事务）
public CloneResult MoveRoute(string sourceGroupId, string routeId, string targetGroupId);

// 仅克隆 Cluster（含 Destinations）
public CloneResult CloneCluster(string sourceGroupId,
    string clusterId, string targetGroupId);
```

**克隆路由内部实现示意（单事务）：**

```csharp
public CloneResult CloneRoute(string srcGroup, string routeId, string targetGroup)
{
    var route = _routeRepo.GetById(routeId, srcGroup);
    if (route == null) return new CloneResult(false, [], [], "Route not found");

    // 冲突预检
    if (_routeRepo.ExistsByRouteId(route.RouteId, targetGroup))
        return new CloneResult(false, [], [$"RouteId '{route.RouteId}' already exists in target group"], null);

    var newIds = new List<string>();
    ExecuteWithVersionBumpBoth(srcGroup, targetGroup, (conn, tx) =>
    {
        // 1. 克隆 Cluster（若目标组没有同名 ClusterId）
        if (!_clusterRepo.ExistsByClusterId(route.ClusterId, targetGroup))
        {
            var cluster = _clusterRepo.GetByClusterId(route.ClusterId, srcGroup);
            var newClusterId = Guid.NewGuid().ToString();
            conn.Execute(/* INSERT cluster with new Id, targetGroup */, tx);
            newIds.Add(newClusterId);

            // 2. 克隆 Destinations
            var dests = _destRepo.GetByCluster(route.ClusterId, srcGroup);
            foreach (var d in dests)
            {
                var newDestId = Guid.NewGuid().ToString();
                conn.Execute(/* INSERT dest with new Id, targetGroup */, tx);
                newIds.Add(newDestId);
            }
        }

        // 3. 克隆 Route
        var newRouteId = Guid.NewGuid().ToString();
        conn.Execute(/* INSERT route with new Id, targetGroup */, tx);
        newIds.Add(newRouteId);
    });

    return new CloneResult(true, newIds, [], null);
}

// 双组版本自增（克隆/迁移时源组和目标组都需要 bump）
private void ExecuteWithVersionBumpBoth(string g1, string g2,
    Action<IDbConnection, IDbTransaction> action)
{
    using var conn = _provider.CreateConnection();
    if (conn.State != ConnectionState.Open) conn.Open();
    using var tx = conn.BeginTransaction();
    try
    {
        action(conn, tx);
        const string bumpSql = """
            UPDATE "ProxyYARP_ConfigGroups"
            SET "ConfigVersion" = "ConfigVersion" + 1
            WHERE "Id" = @Id
            """;
        conn.Execute(bumpSql, new { Id = g1 }, tx);
        if (g1 != g2) conn.Execute(bumpSql, new { Id = g2 }, tx);
        tx.Commit();
    }
    catch { tx.Rollback(); throw; }
}
```

### 5.4 `L4ConfigService.cs` 新增方法

```csharp
// 克隆 L4 路由（含 L4Destinations）
// 冲突检测：同时检查 ListenPort 和 RouteId（两个 UNIQUE 约束）
public CloneResult CloneL4Route(string sourceGroupId, string l4RouteId, string targetGroupId);

// 迁移 L4 路由（克隆 + 删源）
public CloneResult MoveL4Route(string sourceGroupId, string l4RouteId, string targetGroupId);
```

**L4 双重冲突检测：**

```csharp
var conflicts = new List<string>();
if (_routeRepo.ExistsByPort(source.ListenPort, targetGroupId))
    conflicts.Add($"Port {source.ListenPort} already in use in target group");
if (_routeRepo.ExistsByRouteId(source.RouteId, targetGroupId))
    conflicts.Add($"RouteId '{source.RouteId}' already exists in target group");
if (conflicts.Any())
    return new CloneResult(false, [], conflicts, "Conflict detected");
```

### 5.5 新增 API 端点

```
# L7 路由克隆/迁移
POST /api/routes/{id}/clone-to/{targetGroupId}    → 克隆路由（含关联 Cluster）
POST /api/routes/{id}/move-to/{targetGroupId}     → 迁移路由（克隆+删源）

# L7 集群克隆
POST /api/clusters/{clusterId}/clone-to/{targetGroupId}  → 克隆 Cluster+Destinations

# L4 路由克隆/迁移
POST /api/l4routes/{id}/clone-to/{targetGroupId}  → 克隆 L4 路由
POST /api/l4routes/{id}/move-to/{targetGroupId}   → 迁移 L4 路由
```

---

## 六、模块三：分组 Fork / 模板克隆

### 6.1 `GroupService.cs`

```csharp
// Data/Services/GroupService.cs
public class GroupService
{
    public void CreateGroup(string groupId, string name);

    // Fork：一次原子事务复制源组所有配置到新组
    // 包括：Routes + Clusters + Destinations + L4Routes + L4Destinations
    // 证书：将源组的 CertificateGrants 授权给新组（不复制证书数据）
    // 节点：不克隆（新组等待节点自行加入）
    public ForkResult ForkGroup(string sourceGroupId, string newGroupId, string newGroupName);
}

public record ForkResult(
    bool Success,
    string NewGroupId,
    int ClonedRoutes,
    int ClonedClusters,
    int ClonedL4Routes,
    int GrantedCerts,
    string? ErrorMessage
);
```

**Fork 内部执行顺序（单事务）：**

```
1. 校验：newGroupId 不能已存在
2. INSERT 新 ConfigGroup
3. 克隆所有 Clusters（新 Id，新 GroupId）
4. 克隆所有 Destinations（关联新 Cluster Id）
5. 克隆所有 Routes（ClusterId 保持原值，但 GroupId 改为新组）
6. 克隆所有 L4Routes（新 Id，新 GroupId）
7. 克隆所有 L4Destinations（关联新 L4Route Id）
8. 将源组的 CertificateGrants 复制授权给新组
9. 源组 ConfigVersion 不变（Fork 不影响源组）
```

### 6.2 API 端点

```
POST /api/groups                          → 创建空组
     Body: { "groupId": "prod-v2", "name": "Prod V2" }

POST /api/groups/{sourceGroupId}/fork     → Fork 分组
     Body: { "newGroupId": "prod-v2", "name": "Prod V2" }
     Response: ForkResult（含克隆统计数据）
```

---

## 七、AppJsonContext 新增注册（完整清单）

> [!CAUTION]
> AOT 项目中，遗漏任何一个 DTO 类型会导致运行时序列化崩溃（`NotSupportedException`）。
> 以下所有类型**必须**添加到 `Serialization/AppJsonContext.cs`。

```csharp
// --- 证书相关 ---
[JsonSerializable(typeof(CertificateDto))]
[JsonSerializable(typeof(List<CertificateDto>))]
[JsonSerializable(typeof(CreateCertificateRequest))]
[JsonSerializable(typeof(AcmeCertificateRequest))]
[JsonSerializable(typeof(UpdateCertificateRequest))]
[JsonSerializable(typeof(CertificateGrantDto))]
[JsonSerializable(typeof(List<CertificateGrantDto>))]
[JsonSerializable(typeof(AddGrantRequest))]

// --- 克隆/迁移相关 ---
[JsonSerializable(typeof(CloneResult))]

// --- 分组 Fork 相关 ---
[JsonSerializable(typeof(CreateGroupRequest))]
[JsonSerializable(typeof(ForkGroupRequest))]
[JsonSerializable(typeof(ForkResult))]
```

同时，`GroupDetailDto` 需新增 `CertCount` 字段并同步更新 `GetGroupDetails()` SQL：

```csharp
public class GroupDetailDto
{
    public string GroupId { get; set; } = "";
    public int Version { get; set; }
    public int NodeCount { get; set; }
    public int RouteCount { get; set; }
    public int ClusterCount { get; set; }
    public int L4RouteCount { get; set; }
    public int CertCount { get; set; }    // ← 新增
}
```

---

## 八、命令行与环境变量扩展

| 新增参数 | 命令行 | 环境变量 | 说明 |
|---------|--------|---------|------|
| HTTPS 监听端口 | `--https-port <port>` | `HTTPS_PORT` | 不配则不启用 HTTPS |
| 证书私钥加密密钥 | `--cert-key <key>` | `CERT_ENCRYPTION_KEY` | 不配则使用 NoOp（明文）|

在 `Program.cs` 的 `switchMappings` 和 `envMappings` 中添加对应映射：

```csharp
// envMappings
{ "HTTPS_PORT",            "ProxyConfig:HttpsPort" },
{ "CERT_ENCRYPTION_KEY",   "ProxyConfig:CertEncryptionKey" },

// switchMappings
{ "--https-port", "ProxyConfig:HttpsPort" },
{ "--cert-key",   "ProxyConfig:CertEncryptionKey" },
```

帮助信息（`PrintHelp()`）中同步补充说明。

---

## 九、执行阶段划分

### 阶段一（基础层）— 约 2 天
- [ ] `SqliteDbProvider.cs` + `PostgreSqlDbProvider.cs` 添加 Migration 3、4
- [ ] `CertificateEntity.cs` 模型
- [ ] `CertificateRepository.cs` 所有 DB 操作
- [ ] `ICertificateProtector.cs` + `NoOpCertificateProtector.cs`
- [ ] `AppJsonContext.cs` 注册新 DTO 类型（占位，DTO 文件稍后创建）

### 阶段二（SSL 核心层）— 约 3 天
- [ ] `SslCertificateStore.cs`（内存缓存 + SNI + 通配符匹配）
- [ ] `AcmeCertificateService.cs`（手写 ACME HTTP-01，.NET 原生 API）
- [ ] `CertificateRenewalService.cs`（后台续期 + 乐观锁防并发）
- [ ] `HttpsProxyModule.cs`（封装 Kestrel + Pipeline + 时序修复）
- [ ] `Program.cs` 改动（注册模块 + Build 后回填 certStore）

### 阶段三（SSL API）— 约 1 天
- [ ] `CertificatesApi.cs`（全部端点）
- [ ] `NodeHeartbeatService.cs` 修改（追加 certStore.Reload）
- [ ] 补全 `AppJsonContext.cs` 注册

### 阶段四（克隆与迁移）— 约 2 天
- [ ] `ProxyConfigService.cs` 新增方法（含 `ExecuteWithVersionBumpBoth`）
- [ ] `L4ConfigService.cs` 新增方法（含双重冲突检测）
- [ ] `RoutesApi.cs` / `ClustersApi.cs` / `L4RoutesApi.cs` 新增端点

### 阶段五（分组 Fork）— 约 1 天
- [ ] `GroupService.cs` + `ForkGroup` 实现
- [ ] `NodesApi.cs` 新增创建组 + Fork 端点
- [ ] `ProxyConfigGroupRepository.cs` `GetGroupDetails()` SQL 扩展
- [ ] `GroupDetailDto` 新增 `CertCount`

---

## 十、验证计划

### 单元测试（ProxyYARP.Tests）

| 测试目标 | 关键验证点 |
|---------|-----------|
| `SslCertificateStore` | SNI 精确匹配、通配符匹配、无证书返回 null |
| `CertificateRepository` | CRUD、Grant 授权/撤销、乐观锁 TryMarkRenewalAttempt |
| `CloneRoute` | 正常克隆、RouteId 冲突 409、事务回滚 |
| `CloneL4Route` | Port 冲突、RouteId 冲突、双重冲突同时检测 |
| `ForkGroup` | 克隆计数正确、证书 Grant 复制、新组节点为空 |

### 手动集成验证

1. **HTTPS 基本功能**：上传自签名证书 → 配置对应域名路由 → HTTPS 访问成功
2. **同组多节点共享证书**：节点 A 上传证书 → 节点 B 启动后自动加载 → HTTPS 均可用
3. **节点换组后 HTTPS**：节点从 Group A 迁移到 Group B → HTTPS 自动加载 B 组证书
4. **跨组证书授权**：将 A 组证书授权给 B 组 → B 组 HTTPS 立即生效
5. **L7 路由克隆**：克隆路由到新组 → 新组 YARP 配置正确（检查 ConfigVersion 自增）
6. **L4 路由克隆+冲突**：克隆端口冲突时返回 409，不执行操作
7. **Fork 分组**：Fork 后新组路由/集群/L4/证书授权均完整，节点列表为空
8. **ACME 申请**（需公网域名）：填写域名 → 自动申请 → 证书下载并热加载生效
