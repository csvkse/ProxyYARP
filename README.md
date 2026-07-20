# ProxyYARP

![.NET 10](https://img.shields.io/badge/.NET-10.0-blueviolet) ![-Native AOT](https://img.shields.io/badge/Native_AOT-Fast-success) ![YARP](https://img.shields.io/badge/YARP-Reverse_Proxy-blue) ![Vue 3](https://img.shields.io/badge/Vue.js-3.0-4FC08D)

基于 **.NET 10 Native AOT + YARP** 构建的轻量级反向代理管理器。同时提供 **L7 HTTP 反向代理** 与 **L4 TCP/UDP 端口转发**，配置持久化于 SQLite 或 PostgreSQL，内置 Web 管理界面，支持分布式节点管理与热迁移，毫秒级热更新路由，无需重启。

---

## 📖 目录

- [🌟 核心特性](#-核心特性)
- [⚡ 快速开始](#-快速开始)
- [🖥️ Web 控制台](#️-web-控制台)
- [⚙️ 高级配置](#️-高级配置)
- [📝 Metadata (Transforms) 配置](#-metadata-transforms-配置)
- [📦 生产环境部署](#-生产环境部署)
- [🔌 API 接口参考](#-api-接口参考)
- [🏗️ 架构与工作流](#️-架构与工作流)
- [📚 参考库与依赖](#-参考库与依赖)
- [🔧 开发者指南](#-开发者指南)

---

## 🌟 核心特性

* **多租户与分组架构 (Multi-Tenant & Groups)**：原生支持基于 `GroupId` 的配置隔离，在前端随时切换租户。可自由在群组之间分配和隔离路由资源。
* **分布式物理节点管理与热迁移 (Node Migration)**：不仅支持节点在线/离线大盘监控，还支持将节点**无缝热迁移**到其它分组（环境），实现蓝绿环境网关节点的动态调配。
* **双数据库引擎支持 (Dual DB)**：默认嵌入式 **SQLite** 零配置启动；一键通过环境变量无缝切换至 **PostgreSQL**，支持高可用分布式网关架构部署。
* **数据面/控制面解耦**：工作节点可通过配置关闭管控 API (`Management:Enabled=false`) 仅暴露数据面板与健康检查接口，阻断公网/业务域的安全风险，全面拥抱分布式网关形态。
* **双层代理**：
  * **L7 (YARP)** — HTTP/HTTPS 反向代理，支持路由匹配、集群负载均衡、目标健康检查。
  * **L4 (TCP/UDP)** — 原生套接字端口转发，支持 TCP 长连接与 UDP 数据报，自带连接测试接口。
* **原生编译 (Native AOT)**：极低内存占用、毫秒级启动、无 .NET 运行时依赖（主程序 + 数据库原生库同目录部署；Web 静态资源内嵌进主程序）。
* **配置热更新**：路由/集群/目标修改后毫秒级推送至 YARP 管道与 L4 引擎，**全程零重启**。
* **Web 管理界面**：Vue 3 + Tailwind CSS SPA 内嵌于二进制，具备路由、集群、密钥、系统物理节点看板等可视化功能。

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

# 浏览器打开管理界面
# http://localhost:8080/
```

> 🐳 **Docker 用户** 请直接跳转至 [Docker 部署](#docker-部署) 章节，支持 SQLite / PostgreSQL 一键启动，无需本地安装任何运行时。

---

## 🖥️ Web 控制台

访问根路径自动跳转控制台（SPA，所有功能均在同一页面的 Tab 中切换），共五组视图：

| 页面 | SPA Tab | 用途 |
|------|---------|------|
| **租户与分组** | Groups | 分组管理与切换，隔离管理不同环境。 |
| **分布式节点** | Nodes | 系统节点大盘、心跳状态、热迁移更改分组。 |
| **路由与集群** | Routes / Clusters | L7 HTTP 路由、元数据、集群负载均衡与目标节点配置。 |
| **L4 TCP/UDP** | TCP | 四层代理转发规则管理。 |
| **安全与帮助** | Keys / Help | API 密钥鉴权与内置详细文档指南。 |

---

## ⚙️ 高级配置

配置优先级：**命令行参数 > 环境变量 > appsettings.{ENV}.json > appsettings.json > 默认值**。

### 环境变量 (推荐 Docker 用户使用)

| 扁平变量名 | 描述 | 默认值 |
|--------|------|--------|
| `PROXY_PORT` | 监听端口 | `8080` |
| `ACCESS_KEY` | 初始管理员 Key（**仅首启空库时写入**） | 自动生成随机 Key |
| `DB_TYPE` | 数据库 Provider：`sqlite`（默认）/ `pgsql` | `sqlite` |
| `DB_CONNECTION` | 连接字符串。**SQLite** 默认路径 `data/proxy.db`（相对程序目录，留空即可）；**pgsql** 示例：`Host=127.0.0.1;Port=5432;Database=proxyyarp;Username=proxyyarp;Password=proxyyarp` | `""` |
| `MANAGEMENT_PATH` | 自定义控制面板的路径前缀 (如 `/_proxyadmin`)，用于防止管理端点与业务路由冲突 | `""` (挂载在根目录) |
| `NODE_GROUP_ID` | 该节点归属的租户集群 ID (默认属于 `default` 组) | `default` |
| `NODE_NAME` | 物理网关的友好名称 (若空则使用生成的 Guid) | `""` |
| `MANAGEMENT_ENABLED`| 是否在该节点开放管控 API 和 Web 管理界面？ | `true` |

---

## 📝 Metadata (Transforms) 配置

ProxyYARP 原生支持通过 JSON 配置 YARP Transforms。
在**路由**或**集群**的 Metadata 字段中填入 JSON 数组即可生效。

### 去除路径前缀 (PathRemovePrefix)
如果您想把 `/demo` 映射到目标站点的根路径 `/`，自动剥离前缀：
```json
[
  { "PathRemovePrefix": "/demo" }
]
```

### 增加路径前缀 (PathPrefix)
在请求发往后端之前，在请求路径的最前方强行增加一段指定的前缀：
```json
[
  { "PathPrefix": "/v1" }
]
```

### 注入自定义 HTTP 请求头 (RequestHeader)
在请求发往后端集群时，自动增加或覆盖指定的 HTTP 请求头（常用于多租户标识）：
```json
[
  { "RequestHeader": "X-Tenant-Id", "Set": "tenant-a" }
]
```

---

## 📦 生产环境部署

### Linux systemd 部署 (一键安装/卸载)

```bash
# 安装 (服务名 proxyyarp)
sudo ./ProxyYARP -p 8080 -k "MySecretKey" --install

# 卸载服务
sudo ./ProxyYARP --uninstall
```

### Docker 部署

#### 🗄️ SQLite（最简单，单节点，零依赖）

数据持久化到本地目录 `./data`，无需任何外部数据库：

```bash
docker run -d \
  --name proxyyarp \
  -p 8080:8080 \
  -e ACCESS_KEY="MySecretKey" \
  -e DB_TYPE=sqlite \
  -v "$(pwd)/data:/app/data" \
  --restart unless-stopped \
  ghcr.io/csvkse/proxyyarp:latest
```

> **Windows PowerShell** 用户请将 `$(pwd)` 替换为 `${PWD}`。

#### 🐘 PostgreSQL（推荐生产，多节点共享配置）

先启动 PostgreSQL，再启动 ProxyYARP：

```bash
# 1. 启动 PostgreSQL
docker run -d \
  --name proxyyarp-db \
  -e POSTGRES_USER=proxyyarp \
  -e POSTGRES_PASSWORD=proxyyarp \
  -e POSTGRES_DB=proxyyarp \
  -v pgdata:/var/lib/postgresql/data \
  --restart unless-stopped \
  postgres:18-alpine

# 2. 启动 ProxyYARP（连接 PostgreSQL）
docker run -d \
  --name proxyyarp \
  -p 8080:8080 \
  -e ACCESS_KEY="MySecretKey" \
  -e DB_TYPE=pgsql \
  -e DB_CONNECTION="Host=proxyyarp-db;Port=5432;Database=proxyyarp;Username=proxyyarp;Password=proxyyarp" \
  --link proxyyarp-db \
  --restart unless-stopped \
  ghcr.io/csvkse/proxyyarp:latest
```

#### 🚀 Docker Compose（高可用，控制面/数据面分离）

使用项目根目录的 `docker-compose.yml` 一键启动控制面和数据面分离的高可用环境：

```yaml
services:
  # 共享数据库层
  postgres:
    image: postgres:18-alpine
    ...

  # 控制面 (管控界面)
  control-plane:
    image: ghcr.io/csvkse/proxyyarp:latest
    ports: ["8080:8080"]
    environment:
      DB_TYPE: pgsql
      DB_CONNECTION: Host=postgres;...
      MANAGEMENT_ENABLED: true
      NODE_NAME: "Central-Controller"

  # 数据面网关 1 (租户A)
  data-plane-a:
    image: ghcr.io/csvkse/proxyyarp:latest
    ports: ["8081:8080"]
    environment:
      DB_TYPE: pgsql
      MANAGEMENT_ENABLED: false  # 纯网关，不暴露管控台
      NODE_GROUP_ID: "Tenant-A"
```

```bash
docker compose up -d
```

---

## 🔌 API 接口参考

所有接口前缀 `/api`，鉴权支持 `X-Api-Key` Header / `?key=` Query / `api_key` Cookie。

> 💡 **完整交互式文档**（开发模式下）：`http://localhost:8080/scalar/v1`

部分核心接口：

| 资源 | 方法 | 说明 |
|------|------|------|
| `/api/routes` | `GET` / `POST` | 查询 / 创建 L7 路由 |
| `/api/routes/{id}` | `GET` / `PUT` / `DELETE` | 查询 / 更新 / 删除单条路由 |
| `/api/clusters/{id}` | `GET` / `PUT` / `DELETE` | 集群管理 |
| `/api/nodes/{id}` | `GET` / `PUT` / `DELETE` | 分布式物理节点 & 热迁移分配 |
| `/api/tcp-routes/{id}` | `GET` / `PUT` / `DELETE` | L4 TCP/UDP 转发规则 |
| `/api/keys` | `GET` / `POST` / `DELETE` | API 密钥管理 |
| `/health` | `GET` | 健康检查（数据面节点始终开放） |

---

## 🏗️ 架构与工作流

```
                 ┌────────────────────────────────┐
                 │           ProxyYARP            │
                 │  ┌──────────────────────────┐  │
   浏览器管理 ──▶ │  │ Minimal API + Web UI      │  │
   (X-Api-Key)   │  │ (支持 NodeGroup 热迁移)   │  │
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
   TCP/UDP ────▶ │  │ L4 引擎 (TcpProxyEngine)  │  │
                 │  └──────────────────────────┘  │
                 └────────────────────────────────┘
```

---

## 📚 参考库与依赖

* [Yarp.ReverseProxy](https://github.com/dotnet/yarp)
* [Dapper.AOT](https://github.com/DapperLib/DapperAOT)
* [Npgsql](https://github.com/npgsql/npgsql) & SQLite
* Vue 3 & Tailwind CSS

---

## 🔧 开发者指南

**本地开发（Watch 模式）：**
```bash
dotnet run --project src/ProxyYARP
# 开发环境自动挂载 OpenAPI/Scalar 文档：http://localhost:8080/scalar/v1
```

**Windows (x64) Native AOT 编译：**
```powershell
dotnet publish -c Release -r win-x64
```

**Linux (x64) Native AOT 跨平台编译（需 Docker，在 Windows/macOS 上执行）：**
```powershell
# 使用项目自带脚本，输出到 publish/linux-x64/
./build-linux-aot.ps1
```

**Linux Docker 镜像构建：**
```bash
docker build -f src/ProxyYARP/Dockerfile -t proxyyarp .
```
