# ProxyYARP

基于 **.NET 10 Native AOT + YARP** 构建的轻量级反向代理管理器。同时提供 **L7 HTTP 反向代理** 与 **L4 TCP/UDP 端口转发**，配置持久化于 SQLite，内置 Web 管理界面，毫秒级热更新路由，无需重启。

---

## 📖 目录

- [🌟 核心特性](#-核心特性)
- [⚡ 快速开始](#-快速开始)
- [🖥️ Web 控制台](#️-web-控制台)
- [⚙️ 高级配置](#️-高级配置)
- [📦 生产环境部署](#-生产环境部署)
  - [Linux systemd 一键部署](#linux-systemd-部署-一键安装卸载)
  - [Docker 部署](#docker-部署)
- [🔌 API 接口参考](#-api-接口参考)
- [🏗️ 架构与工作流](#️-架构与工作流)
- [📚 参考库与依赖](#-参考库与依赖)
- [🔧 开发者指南](#-开发者指南)

---

## 🌟 核心特性

* **多租户架构隔离 (Multi-Tenant)**：原生支持基于 `GroupId` 的配置隔离，在前端顶部随时切换环境，使单节点控制台即可完美管理多租户的配置树 (Routes / Clusters / TCP)。
* **分布式物理节点监控 (Node Diagnostics)**：内置心跳汇报机制，并在控制面提供全局节点大盘。智能识别并标红离线节点，实时监控各个数据面的存活状态和外部访问 URL。
* **数据面/控制面解耦**：工作节点可通过配置关闭管控 API (`Management:Enabled=false`) 仅暴露数据面板与健康检查接口，阻断公网/业务域的安全风险，全面拥抱分布式网关形态。
* **双层代理**：
  * **L7 (YARP)** — HTTP/HTTPS 反向代理，支持路由匹配、集群负载均衡、目标健康检查。
  * **L4 (TCP/UDP)** — 原生套接字端口转发，支持 TCP 长连接与 UDP 数据报，自带连接测试接口。
* **原生编译 (Native AOT)**：极低内存占用、毫秒级启动、无 .NET 运行时依赖（主程序 + SQLite 原生库两个文件，同目录部署；Web 静态资源内嵌进主程序）。
* **配置热更新**：路由/集群/目标全部存于 SQLite，修改后毫秒级推送至 YARP 管道与 L4 引擎，**全程零重启**。
* **SQLite / PostgreSQL 双支持**：默认嵌入式 SQLite 零配置；可切换至 PostgreSQL，通过 `DB_TYPE`+`DB_CONNECTION` 环境变量一键切换，docker-compose 一键起完整栈。
* **Web 管理界面**：Vue 3 + Tailwind CSS SPA 内嵌于二进制，具备路由、集群、密钥、系统物理节点看板等可视化功能。
* **API Key 鉴权**：三种凭证来源（`X-Api-Key` Header / `?key=` Query / `api_key` Cookie），首启自动生成强随机管理员 Key。
* **开发者文档**：Development 环境自动挂载 Scalar 可视化 API 调试界面，Production 零暴露。
* **systemd 一键部署**：`--install` / `--uninstall` 内置服务注册，无需手写 unit 文件。

---

## ⚡ 快速开始

### 一键安装（Linux 推荐）

```bash
URL="https://github.com/csvkse/ProxyYARP/releases/latest/download/ProxyYARP-linux-x64.tar.gz"; \
(curl -fsSL $URL || wget -qO- $URL) | tar -xz && chmod +x ProxyYARP && \
sudo ./ProxyYARP -p 8080 -k "MySecretKey" --install
```

### 手动运行（前台调试）

```bash
# 指定端口与管理员 Key 启动
./ProxyYARP -p 8080 -k "MySecretKey"

# 不提供 Key 时首启自动生成随机 Key（banner 打印一次，请保存）
./ProxyYARP -p 8080

# 浏览器打开管理界面
# http://localhost:8080/
```

启动 Banner 示例：

```
==========================================================
 ProxyYARP - YARP Reverse Proxy Manager v1.1.0
==========================================================
* Port        : 8080
* Database    : sqlite | Data Source=.../proxy.db;Cache=Shared;
* Admin Key   : abcd*** (已存在配置)
* Environment : Production
* Web UI      : http://localhost:8080/
==========================================================
```

---

## 🖥️ Web 控制台

访问根路径自动跳转控制台，共四组视图：

| 页面 | 路径 | 用途 |
|------|------|------|
| **路由管理** | `/routes.html` | L7 路由增删改查（匹配规则、集群绑定） |
| **集群管理** | `/clusters.html` | 集群与目标（Destination）管理、负载均衡、健康检查 |
| **密钥管理** | `/keys.html` | API Key 创建/禁用/删除 |
| **首页** | `/index.html` | 概览与 TCP/UDP 转发入口 |

**鉴权方式**：登录页输入管理员 Key，或 URL 携带 `?key=MySecretKey`，亦可通过 `X-Api-Key` 请求头调用 API。

---

## ⚙️ 高级配置

配置优先级：**命令行参数 > 环境变量 > appsettings.{ENV}.json > appsettings.json > 默认值**。

### 环境变量 (推荐 Docker 用户使用)

| 扁平变量名 | 对应层级配置 | 描述 | 默认值 |
|--------|----------|------|--------|
| `PROXY_PORT` | `ProxyConfig:Port` | 监听端口 | `8080` |
| `ACCESS_KEY` | `ProxyConfig:AdminKey` | 初始管理员 Key（**仅首启空库时写入**，DB 已有 Key 则忽略） | 自动生成随机 Key |
| `DB_TYPE` | `Database:Provider` | 数据库 Provider：`sqlite`（默认）/ `pgsql` | `sqlite` |
| `DB_CONNECTION` | `Database:ConnectionString` | 连接字符串（pgsql 必填，如 `Host=pg;Port=5432;...`） | `""` |
| `NODE_GROUP_ID` | `Management:GroupId` | 该节点归属的租户集群 ID (默认属于 `default` 组) | `default` |
| `NODE_NAME` | `Management:NodeName` | 物理网关的友好名称 (若空则使用生成的 Guid) | `""` |
| `MANAGEMENT_ENABLED`| `Management:Enabled` | 是否在该节点开放管控 API 和 Web 管理界面？(设为 `false` 则退化为纯数据面网关) | `true` |
| `MANAGEMENT_URL` | `Management:Url` | 汇报给中心节点大盘的公网/内网可达管理网址 (用于快速跳转或外网探活) | `""` |

### 命令行参数

| 参数 | 全称 | 对应配置 | 说明 |
|------|------|----------|------|
| `-p` | `--Port` | `ProxyConfig:Port` | 监听端口 |
| `-k` | `--Key` | `ProxyConfig:AdminKey` | 初始管理员 Key |
| `--db-type` | — | `Database:Provider` | 数据库类型：sqlite / pgsql |
| `--db-conn` | — | `Database:ConnectionString` | 数据库连接字符串 |
| `-i` | `--install` | — | [Linux] 注册 systemd 服务 |
| — | `--uninstall` | — | [Linux] 卸载 systemd 服务 |

### 使用 PostgreSQL

```bash
docker run -d \
  -e DB_TYPE=pgsql \
  -e DB_CONNECTION="Host=pg.example.com;Port=5432;Database=proxyyarp;Username=u;Password=***" \
  -e ACCESS_KEY="MySecretKey" \
  -p 8080:8080 \
  ghcr.io/csvkse/proxyyarp:latest
```

或使用项目根目录的 `docker-compose.yml` 一键启动（proxy + postgres）：

```bash
docker compose up -d
```

首次启动 `MigrationRunner` 自动建表（幂等，记录在 `__SchemaMigrations` 表）。

---

## 📦 生产环境部署

### Linux systemd 部署 (一键安装/卸载)

项目内置 systemd 服务注册命令，**无需手写 unit 文件**。在 Linux 上以 `root` 执行：

```bash
# 安装 (服务名 proxyyarp)
sudo ./ProxyYARP -p 8080 -k "MySecretKey" --install

# 常用管理命令
sudo systemctl status proxyyarp        # 查看状态
sudo systemctl restart proxyyarp       # 重启
journalctl -u proxyyarp -f             # 实时日志

# 卸载服务
sudo ./ProxyYARP --uninstall
```

> ⚠️ 服务名固定为 `proxyyarp`。同机只能装一个实例。

### Docker 部署

镜像内置 `VOLUME /app/data` 与 `ENV DB_CONNECTION="Data Source=/app/data/proxy.db;Cache=Shared;"`（`DB_TYPE=sqlite`），挂载卷即可持久化配置：

```bash
docker run -d --restart always \
  --name proxyyarp \
  -p 8080:8080 \
  -e ACCESS_KEY="MySecretKey" \
  -v proxyyarp-data:/app/data \
  ghcr.io/csvkse/proxyyarp:latest
```

**PostgreSQL 一键起完整栈：**

```bash
docker compose up -d
```

> `ACCESS_KEY` 仅在数据库为空的首启时生效；之后以 DB 内 Key 为准，更换需通过 Web 控制台或 API。

> 📌 镜像以**非 root 用户（UID 1654）**运行，基于 `runtime-deps:10.0-noble-chiseled`（精简 Ubuntu，无 shell/ICU/tzdata，仅约 44M；`InvariantGlobalization` 下无需 ICU）。
> 从旧版 root 镜像升级且复用已有 volume 时，需先修正属主，否则报 `attempt to write a readonly database`：
> ```bash
> docker run --rm -v proxyyarp-data:/data --user root alpine chown -R 1654:1654 /data
> ```

**docker-compose (结合多租户/控制面与数据面分离)：**

```yaml
services:
  # 共享数据库层
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: proxyyarp
      POSTGRES_PASSWORD: proxyyarp
      POSTGRES_DB: proxyyarp
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U proxyyarp -d proxyyarp"]
      interval: 5s

  # 控制面 (管控界面)
  control-plane:
    image: ghcr.io/csvkse/proxyyarp:latest
    ports: ["8080:8080"]
    environment:
      DB_TYPE: pgsql
      DB_CONNECTION: Host=postgres;Port=5432;Database=proxyyarp;Username=proxyyarp;Password=proxyyarp
      ACCESS_KEY: "SuperAdminKey123"
      MANAGEMENT_ENABLED: true
      NODE_NAME: "Central-Controller"
    depends_on:
      postgres:
        condition: service_healthy

  # 数据面网关 1 (租户A)
  data-plane-a:
    image: ghcr.io/csvkse/proxyyarp:latest
    ports: ["8081:8080"]
    environment:
      DB_TYPE: pgsql
      DB_CONNECTION: Host=postgres;Port=5432;Database=proxyyarp;Username=proxyyarp;Password=proxyyarp
      MANAGEMENT_ENABLED: false  # 纯网关，不暴露管控台
      NODE_GROUP_ID: "Tenant-A"
      NODE_NAME: "Gateway-A-Worker1"
    depends_on:
      - control-plane

volumes:
  pgdata:
```

---

## 🔌 API 接口参考

所有接口前缀 `/api`，鉴权支持 `X-Api-Key` Header / `?key=` Query / `api_key` Cookie。

| 方法 | 路径 | 说明 |
|------|------|------|
| `POST` | `/api/auth/login` | 登录并写入 `api_key` Cookie |
| `POST` | `/api/auth/logout` | 登出 |
| `GET` | `/api/auth/me` | 当前凭证信息 |
| `GET/POST` | `/api/routes` | L7 路由列表 / 创建 |
| `GET/PUT/DELETE` | `/api/routes/{id}` | 路由详情 / 更新 / 删除 |
| `GET/POST` | `/api/clusters` | 集群列表 / 创建 |
| `GET/PUT/DELETE` | `/api/clusters/{id}` | 集群详情 / 更新 / 删除 |
| `GET/POST` | `/api/clusters/{clusterId}/destinations` | 目标列表 / 添加 |
| `PUT/DELETE` | `/api/clusters/destinations/{id}` | 目标更新 / 删除 |
| `GET/POST` | `/api/tcp-routes` | L4 (TCP/UDP) 转发列表 / 创建（`Protocol` 字段区分） |
| `GET/PUT/DELETE` | `/api/tcp-routes/{id}` | 转发详情 / 更新 / 删除 |
| `POST` | `/api/tcp-routes/test-connection` | 目标连通性测试 |
| `GET/POST` | `/api/keys` | API Key 列表 / 创建 |
| `GET/PUT/DELETE` | `/api/keys/{id}` | Key 详情 / 更新 / 删除 |
| `GET` | `/api/version` | 版本信息（免鉴权） |

---

## 🏗️ 架构与工作流

```
                 ┌────────────────────────────────┐
                 │           ProxyYARP            │
                 │  ┌──────────────────────────┐  │
   浏览器管理 ──▶ │  │ Minimal API + Web UI      │  │
   (X-Api-Key)   │  │ (支持 GroupId 租户切换)    │  │
                 │  └───────────┬──────────────┘  │
                 │              ▼ Dapper.AOT      │
                 │  ┌──────────────────────────┐  │
                 │  │ Shared Postgres/SQLite   │  │
                 │  │ (中心化配置、节点心跳表)    │  │
                 │  └───────────┬──────────────┘  │
                 │   变更通知    │  (心跳探活)      │
                 │  ┌───────────┴──────────────┐  │
   HTTP 流量 ──▶ │  │ YARP 管道 (L7)            │  │
                 │  │ DatabaseProxyConfigProvider│  │
                 │  ├──────────────────────────┤  │
   TCP/UDP ────▶ │  │ L4 引擎 (TcpProxyEngine / │  │
                 │  │  UdpProxyEngine)          │  │
                 │  └──────────────────────────┘  │
                 └────────────────────────────────┘
```

**数据流与节点分离**：
1. **控制面隔离**：配置项按 `GroupId` (组 ID) 在数据库底层彻底隔离，不同的业务网关互不干扰。
2. **零停机分发**：管理员通过 Web UI / API 修改路由、集群或 L4 转发，写入数据库。`ProxyConfigService` 触发变更通知，毫秒级向各工作节点的 YARP 推送新配置快照，**全程无需重启进程**。
3. **数据面守护**：工作节点仅配置数据库连接串和属于自己的 `NODE_GROUP_ID`。配置 `MANAGEMENT_ENABLED=false` 即可关闭 Web 端点，成为完全封闭的高并发纯代理机器。工作节点每 10 秒向中心库写入自己的心跳状态（在线/离线），供管控端监控。

---

## 📚 参考库与依赖

### 运行时依赖 (NuGet)

| 库 | 版本 | 用途 |
|----|------|------|
| [Yarp.ReverseProxy](https://github.com/dotnet/yarp) | 2.3.0 | L7 反向代理核心（路由/集群/负载均衡/健康检查） |
| [Dapper](https://github.com/DapperLib/Dapper) | 2.1.79 | 轻量 ORM，SQLite/PostgreSQL 数据访问 |
| [Dapper.AOT](https://github.com/DapperLib/DapperAOT) | 1.0.52 | 编译期 SQL 代码生成，AOT 零反射（`DapperAotStrict`） |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | 10.0.10 | SQLite 驱动（默认 Provider） |
| [Npgsql](https://github.com/npgsql/npgsql) | 10.0.3 | PostgreSQL 驱动（Native AOT 官方兼容，纯托管） |
| [Microsoft.Extensions.FileProviders.Embedded](https://github.com/dotnet/aspnetcore) | 10.0.10 | wwwroot 静态资源内嵌进单文件 |
| [Microsoft.AspNetCore.OpenApi](https://github.com/dotnet/aspnetcore) | 10.0.10 | 内置 OpenAPI 3.1 文档生成（AOT 兼容） |
| [Scalar.AspNetCore](https://github.com/scalar/scalar) | 2.16.15 | OpenAPI 可视化调试 UI（仅 Development 挂载） |

### 平台与前端

| 技术 | 说明 |
|------|------|
| **.NET 10 + Native AOT** | `PublishAot`，`CreateSlimBuilder`，`rd.xml` 裁剪描述符 |
| **Vue 3** (CDN) | Web 管理界面响应式框架 |
| **Tailwind CSS** (CDN) | Web 管理界面样式 |

### 参考项目

- [dotnet/yarp](https://github.com/dotnet/yarp) — 反向代理配置模型与热更新机制
- [ServerLatency](https://github.com/csvkse/ServerLatency) — Git Tag 版本注入、GHCR 多架构构建工作流、systemd 安装器模式

---

## 🔧 开发者指南

### 项目结构

| 文件/目录 | 职责 |
|------|------|
| `ProxyYARP.csproj` | 核心项目，Minimal API + `PublishAot`，`wwwroot/**` 内嵌，`DapperAotStrict` |
| `Program.cs` | 入口：帮助/安装指令拦截、环境变量映射、配置加载、Provider 工厂、Kestrel 与代理模块装配 |
| `Api/` | Minimal API 端点（Auth / Keys / Routes / Clusters / L4Routes） |
| `Auth/` | `ApiKeyMiddleware`（Header/Query/Cookie 三来源鉴权） |
| `Data/Db/` | `IDbProvider` 抽象 + Sqlite/PostgreSQL 实现 + `MigrationRunner` 版本化迁移 |
| `Data/Models/` · `Data/Repositories/` · `Data/Services/` | 实体（原生 bool/DateTime）、Repository（注入 Provider）、Service 业务层、种子数据 |
| `Proxy/Yarp/` | `DatabaseProxyConfigProvider`：DB → YARP 配置热推送 |
| `Proxy/Tcp` · `Proxy/Udp/` | L4 转发引擎（HostedService） |
| `Common/LinuxServiceInstaller.cs` | systemd 服务注册/卸载 |
| `Serialization/` | AOT 兼容 `JsonSerializerContext` 注册表 |
| `wwwroot/` | 纯前端静态资源，Vue 3 + Tailwind CSS |

### 编译发布 (Native AOT)

Native AOT 编译为主程序 + SQLite 原生库（`e_sqlite3.dll` / `libe_sqlite3.so`）两个文件，**Web 静态资源自动内嵌**进主程序。

**Windows (x64)：**
```powershell
dotnet publish -c Release -r win-x64
```

**Linux (x64) — Docker 构建（推荐）：**
.NET AOT 不支持跨 OS 交叉编译，Windows 上直接用项目 Dockerfile 构建 Linux 产物：
```bash
docker build -f src/ProxyYARP/Dockerfile -t proxyyarp .
```

### 版本与发版

版本号唯一真实来源是 **Git Tag**，代码内无硬编码版本字符串：

```bash
git tag v1.0.1 && git push origin v1.0.1
```

GitHub Actions 自动提取 Tag → `APP_VERSION` build-arg → `dotnet publish -p:Version=` → 推送 `ghcr.io` 镜像标签 `1.0.1` / `1.0` / `latest`。`.csproj` 中的 `<Version>` 仅作本地开发默认值，发版后同步到下一目标版本。

### 开发者文档 (OpenAPI + Scalar)

Development 环境自动挂载可视化 API 文档，**Production 不暴露**：

```bash
# 本地
DOTNET_ENVIRONMENT=Development dotnet run
# 打开 http://localhost:8080/scalar/v1

# Docker
docker run -d -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ACCESS_KEY="MySecretKey" \
  ghcr.io/csvkse/proxyyarp:latest
```

| 路径 | 说明 |
|------|------|
| `/scalar/v1` | Scalar 可视化调试 UI（可填 `X-Api-Key` 直接调试受保护接口） |
| `/openapi/v1.json` | OpenAPI 3.1 文档源（可导入 Postman / Apifox） |

> ⚠️ Development 模式会将完整 API 结构**无鉴权公开**（接口本身仍需 Key 调用），公网部署请保持默认 Production。

> **注意：AOT 与反射**。AOT 环境不支持运行时反射序列化，新增 DTO 必须在 `Serialization/AppJsonContext.cs` 注册 `[JsonSerializable]`；新增 SQL 查询走 Dapper.AOT 源码生成，禁止反射回退。
