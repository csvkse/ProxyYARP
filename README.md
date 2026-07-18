# ProxyYARP

基于 **.NET 10 Native AOT + YARP** 构建的轻量级反向代理管理器。单一可执行文件同时提供 **L7 HTTP 反向代理** 与 **L4 TCP/UDP 端口转发**，配置持久化于 SQLite，内置 Web 管理界面，毫秒级热更新路由，无需重启。

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

* **双层代理**：
  * **L7 (YARP)** — HTTP/HTTPS 反向代理，支持路由匹配、集群负载均衡、目标健康检查。
  * **L4 (TCP/UDP)** — 原生套接字端口转发，支持 TCP 长连接与 UDP 数据报，自带连接测试接口。
* **原生编译 (Native AOT)**：极低内存占用、毫秒级启动、无 .NET 运行时依赖（主程序 + SQLite 原生库两个文件，同目录部署；Web 静态资源内嵌进主程序）。
* **配置热更新**：路由/集群/目标全部存于 SQLite，修改后毫秒级推送至 YARP 管道与 L4 引擎，**全程零重启**。
* **SQLite 持久化**：基于 Dapper.AOT 源码生成器（`DapperAotStrict`，禁止反射回退），AOT 完全兼容。
* **Web 管理界面**：Vue 3 + Tailwind CSS SPA 内嵌于二进制，路由、集群、密钥、TCP 转发可视化管理。
* **API Key 鉴权**：三种凭证来源（`X-Api-Key` Header / `?key=` Query / `api_key` Cookie），首启自动生成强随机管理员 Key 并掩码显示。
* **反代适配**：内置 `ForwardedHeaders`（仅信任回环地址），正确解析上游代理后的真实客户端 IP。
* **开发者文档**：Development 环境自动挂载 Scalar 可视化 API 调试界面（`/scalar/v1`），Production 零暴露。
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
 ProxyYARP - YARP Reverse Proxy Manager v1.0.0
==========================================================
* Port        : 8080
* DB Path     : Data Source=.../proxy.db;Cache=Shared;
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
| `DB_PATH` | `ProxyConfig:DbPath` | SQLite 数据库文件路径 | `<程序目录>/proxy.db` |

### 命令行参数

| 参数 | 全称 | 对应配置 | 说明 |
|------|------|----------|------|
| `-p` | `--Port` | `ProxyConfig:Port` | 监听端口 |
| `-k` | `--Key` | `ProxyConfig:AdminKey` | 初始管理员 Key |
| `-db` | `--Db` | `ProxyConfig:DbPath` | SQLite 数据库路径 |
| `-i` | `--install` | — | [Linux] 注册 systemd 服务 |
| — | `--uninstall` | — | [Linux] 卸载 systemd 服务 |

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

镜像内置 `VOLUME /app/data` 与 `ENV DB_PATH=/app/data/proxy.db`，挂载卷即可持久化配置：

```bash
docker run -d --restart always \
  --name proxyyarp \
  -p 8080:8080 \
  -e ACCESS_KEY="MySecretKey" \
  -v proxyyarp-data:/app/data \
  ghcr.io/csvkse/proxyyarp:latest
```

> `ACCESS_KEY` 仅在数据库为空的首启时生效；之后以 DB 内 Key 为准，更换需通过 Web 控制台或 API。

**docker-compose：**

```yaml
services:
  proxyyarp:
    image: ghcr.io/csvkse/proxyyarp:latest
    ports: ["8080:8080"]
    environment:
      ACCESS_KEY: MySecretKey
    volumes:
      - proxyyarp-data:/app/data
volumes:
  proxyyarp-data:
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
   (X-Api-Key)   │  │ (内嵌 wwwroot, AOT JSON)  │  │
                 │  └───────────┬──────────────┘  │
                 │              ▼ Dapper.AOT      │
                 │  ┌──────────────────────────┐  │
                 │  │ SQLite (routes/clusters/  │  │
                 │  │  destinations/keys/l4)    │  │
                 │  └───────────┬──────────────┘  │
                 │   变更通知    │                  │
                 │  ┌───────────┴──────────────┐  │
   HTTP 流量 ──▶ │  │ YARP 管道 (L7)            │  │
                 │  │ DatabaseProxyConfigProvider│  │
                 │  ├──────────────────────────┤  │
   TCP/UDP ────▶ │  │ L4 引擎 (TcpProxyEngine / │  │
                 │  │  UdpProxyEngine)          │  │
                 │  └──────────────────────────┘  │
                 └────────────────────────────────┘
```

**数据流**：
1. 管理员通过 Web UI / API 修改路由、集群或 L4 转发，写入 SQLite。
2. `ProxyConfigService` / `L4ConfigService` 触发变更通知。
3. `DatabaseProxyConfigProvider` 毫秒级向 YARP 推送新配置快照；TCP/UDP 引擎同步重建监听。
4. 流量按最新配置转发，**进程无需重启**。

---

## 📚 参考库与依赖

### 运行时依赖 (NuGet)

| 库 | 版本 | 用途 |
|----|------|------|
| [Yarp.ReverseProxy](https://github.com/dotnet/yarp) | 2.3.0 | L7 反向代理核心（路由/集群/负载均衡/健康检查） |
| [Dapper](https://github.com/DapperLib/Dapper) | 2.1.35 | 轻量 ORM，SQLite 数据访问 |
| [Dapper.AOT](https://github.com/DapperLib/DapperAOT) | 1.0.52 | 编译期 SQL 代码生成，AOT 零反射（`DapperAotStrict`） |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | 9.0.7 | SQLite 驱动 |
| [Microsoft.Extensions.FileProviders.Embedded](https://github.com/dotnet/aspnetcore) | 9.0.7 | wwwroot 静态资源内嵌进单文件 |
| [Microsoft.AspNetCore.OpenApi](https://github.com/dotnet/aspnetcore) | 10.0.10 | 内置 OpenAPI 3.1 文档生成（AOT 兼容） |
| [Scalar.AspNetCore](https://github.com/scalar/scalar) | 2.11.0 | OpenAPI 可视化调试 UI（仅 Development 挂载） |

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
| `Program.cs` | 入口：帮助/安装指令拦截、环境变量映射、配置加载、Kestrel 与代理模块装配 |
| `Api/` | Minimal API 端点（Auth / Keys / Routes / Clusters / L4Routes） |
| `Auth/` | `ApiKeyMiddleware`（Header/Query/Cookie 三来源鉴权） |
| `Data/` | SQLite 上下文、实体、Repository、Service、种子数据 |
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
