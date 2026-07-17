# ProxyYARP 问题修复执行计划

基于 2026-07-17 的项目审计结果（构建 0 错误、117/118 测试通过），共发现 10 项问题。
按风险与依赖关系分为三个阶段执行。每阶段结束后运行 `dotnet build` + `dotnet test` 验证。

## 阶段一：低风险清理（不改变运行时行为）

### 1.1 修复中文注释乱码（7 个文件）
- 文件：`src/ProxyYARP/Api/AuthApi.cs`、`Api/KeysApi.cs`、`Api/L4RoutesApi.cs`、
  `Data/Services/DbInitService.cs`、`Data/Services/L4ConfigService.cs`、
  `Proxy/L4/L4LoadBalancerPolicies.cs`、`Proxy/Tcp/TcpProxyEngine.cs`
- 内容：将所有 U+FFFD（�）乱码按上下文恢复为正确中文注释
- 重点：`L4RoutesApi.cs:138` 是面向用户的报错文案 `连接超时 (2�?，...`，需恢复为 `连接超时 (2s)，无法连接到 ...`
- 全部文件统一保存为 UTF-8（与现有文件一致，带 BOM 与否按原文件）

### 1.2 清理 TCP→L4 重构残留
- `L4ConfigService.cs`：`TcpRouteDto` → `L4RouteDto`（同步改 `L4RoutesApi.cs` 引用），注释 "TCP 配置业务服务" → "四层配置业务服务"
- `DbInitService.cs:13-14`：字段 `_tcpRouteRepo/_tcpDestRepo` → `_l4RouteRepo/_l4DestRepo`
- `L4RoutesApi.cs:18`：删除 `IsPortInUse` 未使用的 `excludeId` 参数
- 注意：`/api/tcp-routes` 路由路径与前端耦合，**不改**（改动需前后端同步，不在本阶段）

### 1.3 修复编译警告 CS8602
- `src/ProxyYARP.Tests/Unit/DatabaseProxyConfigProviderTests.cs:79`：
  `Destinations["node-1"]` 加 null 断言处理（FluentAssertions 可用 `config.Clusters[0].Destinations["node-1"].Should().NotBeNull()` 前置断言）

### 1.4 Banner 环境显示修正
- `Program.cs:50`：`DOTNET_ENVIRONMENT` → 同时回退读 `ASPNETCORE_ENVIRONMENT`，与 ASP.NET Core 实际环境保持一致

### 1.5 删除根目录残留文件
- 删除：`refactor.cs`、`test.cs`、`repair.py`、`index_body.html`、`index_downloaded.html`
- 均已被 .gitignore 忽略，删除不影响仓库历史

## 阶段二：行为修复（改变运行时行为，需测试覆盖）

### 2.1 修复重启后打印无效管理员 Key
- 问题：`Program.cs` 每次启动都生成随机 Key 并打印"首次生成"，但 `SeedAdminKey` 只在 DB 为空时写入，重启后打印的 Key 无效
- 方案：`DbInitService.SeedAdminKey` 改为返回 `bool`（是否实际写入）；`Program.cs` 仅在实际写入时打印全量 Key 和"首次生成"提示，否则打印掩码 Key + "已存在配置"

### 2.2 密钥列表接口屏蔽明文
- 问题：`KeysApi.MapToDto` 列表接口返回 `KeyValue` 全文明文，与前端"只显示一次"承诺矛盾
- 方案：列表/详情接口只返回前缀（如前 8 字符 + `...`）；仅创建接口（POST）的响应保留全文明文
- 同步检查前端 `wwwroot/index.html` 密钥页面的展示逻辑是否需要配合调整

### 2.3 UDP 监听器绑定异常兜底
- 问题：`UdpListenerContext` 构造函数中 `Bind()` 异常会直接抛出
- 方案：对齐 TCP 引擎 `StartListener` 的做法，Bind 失败时记日志并跳过该端口，不影响其他监听器和 API 调用

## 阶段三：功能补全（需设计讨论，暂不排期）

### 3.1 YARP 健康检查配置落地
- 现状：`HealthCheckEnabled` 等配置只存库，`DatabaseProxyConfigProvider.BuildClusters` 完全未映射到 `ClusterConfig.HealthCheck`
- 需要先明确：支持主动健康检查还是被动、配置 JSON 的 schema、前端表单如何对应

### 3.2 恢复被跳过的 TCP 转发集成测试
- `TcpProxyEngineTests.TcpProxyEngine_Should_Forward_Traffic_With_LoadBalancing` 因不稳定被 Skip
- 方案方向：使用环回端口 0 动态分配 + 重试/超时策略替代固定端口

## 验收标准
- 每阶段后：`dotnet build` 0 错误 0 警告，`dotnet test` 全量通过
- 阶段二后：手动冒烟 —— 启动、二次启动（验证 Key 提示）、登录、创建/列出密钥（验证掩码）
- 所有改动文件 UTF-8 编码，无新增 U+FFFD
