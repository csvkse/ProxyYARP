# 前端页面结构与使用分析 (Frontend Pages Analysis)

本项目的前端管理界面由 `src/ProxyYARP/wwwroot` 目录下的 5 个 HTML 页面组成。它们采用原生 HTML + Vue.js 的方式构建，在 C# 后端中，这些文件通过内置的静态文件提供程序 (`ManifestEmbeddedFileProvider`) 被内嵌到程序集中，并通过 `app.UseStaticFiles()` 对外提供访问。

## 页面清单及核心作用

1. **`index.html` (主页/仪表盘)**
   - **路由映射**：在 `Program.cs` 中，默认的根路径 `/` 会被直接重定向到 `/index.html` (`app.MapGet("/", () => Results.Redirect("/index.html"));`)。
   - **功能**：作为管理后台的入口和整体状态仪表盘。

2. **`login.html` (登录认证)**
   - **路由机制**：前端页面的 JavaScript 会在执行时检查认证状态。当检测到 LocalStorage 中不存在 `apiKey`，或者向后端发起 API 请求后收到 `401 Unauthorized` 状态码时，会通过 `window.location.href = '/login.html'` 强制跳转至此页。
   - **功能**：用于输入系统的 Admin API Key 进行管理员身份验证。

3. **`routes.html` (路由规则管理)**
   - **页面导航**：前端各个页面的导航菜单中相互硬编码了链接（例如 `<a href="/routes.html">`）。
   - **功能**：配置和管理 YARP (Yet Another Reverse Proxy) 的反向代理路由规则（Routes）。

4. **`clusters.html` (集群目标管理)**
   - **页面导航**：包含在全局导航栏中（`<a href="/clusters.html">`）。
   - **功能**：管理 YARP 代理请求的后端集群和具体目标地址服务器（Clusters & Destinations）。

5. **`keys.html` (密钥权限管理)**
   - **页面导航**：包含在全局导航栏中（`<a href="/keys.html">`）。
   - **功能**：系统级别的 API Keys 维护和管理页面。

## 总结
这 5 个 HTML 文件协同工作，缺一不可：
- `index.html` 是应用的默认访问入口。
- `login.html` 是整个系统安全访问的守护者，没有它则无法提供登录交互界面。
- 其余 3 个页面分别承担了代理服务最核心的功能维护：路由控制（Routes）、上游节点配置（Clusters）以及安全密钥管理（Keys）。

前端页面并未引入复杂的单页应用前端路由系统（如 Vue Router ），而是采用了传统的纯页面间 `href` 跳转结合基于 LocalStorage 的请求 Header 拦截与 `401` 鉴权守护机制。这在单文件应用和轻量化系统（尤其是需要 AOT 发布的单可执行文件程序）中，是一种高效且降低构建复杂度的实现方式。
