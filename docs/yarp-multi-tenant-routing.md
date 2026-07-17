# YARP 多域名同路径路由配置经验

## 场景需求

在使用 YARP（Yet Another Reverse Proxy）作为网关时，常常会遇到“多租户”或“多业务线”的路由场景。典型的需求是：
针对不同的请求域名（Host），即使请求的 URL 路径（Path）完全相同，也需要将流量转发到不同的目标后端集群（Cluster）。

例如：
- 请求 `api.client-a.com/users` -> 转发到 `Cluster A`
- 请求 `api.client-b.com/users` -> 转发到 `Cluster B`

## YARP 原生支持

YARP 原生完美支持这一特性，无需自行编写中间件。YARP 的路由匹配（`Match`）不仅可以根据 `Path` 匹配，还可以通过 `Hosts` 数组来精准匹配请求头中的 `Host` 字段。

### JSON 配置示例

如果你直接修改 `appsettings.json`，配置方式如下：

```json
{
  "ReverseProxy": {
    "Routes": {
      "routeA": {
        "ClusterId": "clusterA",
        "Match": {
          "Hosts": [ "api.client-a.com" ],
          "Path": "/users/{**catch-all}"
        }
      },
      "routeB": {
        "ClusterId": "clusterB",
        "Match": {
          "Hosts": [ "api.client-b.com" ],
          "Path": "/users/{**catch-all}"
        }
      }
    },
    "Clusters": {
      "clusterA": {
        "Destinations": {
          "dest1": { "Address": "https://server-a.internal/" }
        }
      },
      "clusterB": {
        "Destinations": {
          "dest1": { "Address": "https://server-b.internal/" }
        }
      }
    }
  }
}
```

## 在 ProxyYARP 中的实践配置

当前 ProxyYARP 项目通过 `DatabaseProxyConfigProvider` 动态从 SQLite 加载配置，代码层面已经处理了对 `Hosts` 字段的解析（以逗号分隔）。

要在前端管理面板（Web UI）中配置此功能：

1. **集群准备**：在【集群管理】页面，创建两个不同的集群（如 `ClusterA` 和 `ClusterB`），并分别为它们添加对应的目标节点。
2. **第一条路由**：
   - 进入【路由管理】，点击 **+ 新建路由**。
   - 填写路由 ID、目标集群 ID（选 `ClusterA`）。
   - **匹配路径**填写 `/users/{**catch-all}`。
   - **域名 (Hosts)**填写第一家客户的域名 `api.client-a.com`。
   - 保存。
3. **第二条路由**：
   - 再次点击 **+ 新建路由**。
   - 填写路由 ID、目标集群 ID（选 `ClusterB`）。
   - **匹配路径**必须和第一条**完全一致**，填写 `/users/{**catch-all}`。
   - **域名 (Hosts)**填写第二家客户的域名 `api.client-b.com`。
   - 保存。

配置保存后，YARP 热重载立刻生效，网关将会严格根据客户端发送的 HTTP 请求头（Host Header）将流量分发至各自的独立后端。
