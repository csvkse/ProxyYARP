# ProxyYARP 架构经验与踩坑记录

本文档用于记录在开发 ProxyYARP 过程中的一些核心架构设计思想、排错经验以及技术选型背后的考量，以供未来迭代和重构时参考。

## 一、前端踩坑记录 (Vue 3 + Tailwind CDN)

### 1. Vue 模板编译错误 (`Attribute name cannot contain U+0022...`)
- **现象**：当向 `index.html` 中粘贴带有 `<svg>` 标签的代码时，控制台抛出 `Template compilation error`，导致整个页面无法渲染。
- **原因**：在 Vue 的属性绑定中（如 `:class="[ ... ]"`），不能直接混入原始的 HTML/SVG 标签结构，这会被 Vue 的 HTML 词法分析器误认为是非法的属性名。
- **解决经验**：尽量将复杂的结构抽象为组件，或者使用 `v-html="nav.icon"`，将 HTML 标签作为字符串属性定义在 JS 数据结构中（如 `navItems` 数组）。

### 2. CSS 层叠上下文 (Stacking Contexts) 与 `z-index` 失效
- **现象**：全局提示框 (Toast) 的层级设置为 `z-index: 100`，而模态框 (Modal) 设置为 `z-index: 50`。但在实际渲染时，Toast 仍然被 Modal 的黑色半透明背景遮挡。
- **原因**：Toast HTML 被嵌套在了一个 `<main class="z-10">` 标签内。根据 CSS 规范，`<main>` 形成了一个独立的“层叠上下文”。在不同层叠上下文中，子元素的 `z-index` 哪怕高达 `9999`，也无法突破父元素的 `z-index: 10` 去和外部 `z-index: 50` 的元素抗衡。
- **解决经验**：**全局悬浮层（如 Modal、Toast、Tooltip）的 DOM 结构必须放置在同一个根层级（例如直接放在 `#app` 底部）**，以确保它们处于同一个基础层叠上下文中，此时 `z-index` 的绝对值大小才会严格生效。

---

## 二、后端与底层引擎踩坑记录 (.NET Native AOT)

### 1. 进程锁定导致构建失败 (MSB3021 / MSB3026)
- **现象**：在通过 `dotnet build` 或热编译时，常常报错 `The process cannot access the file ... ProxyYARP.exe because it is being used by another process.`。
- **原因**：Native AOT 编译的产物是独立的二进制可执行文件。在开发环境中，如果我们没有彻底杀死上一轮启动的 `ProxyYARP.exe` 进程（或者它卡在后台死循环），重新编译在覆盖时就会触发系统的文件锁定异常。
- **解决经验**：在重新触发构建前，必须确保端口释放且对应进程已被终止（可使用 `taskkill /IM ProxyYARP.exe /F`）。

### 2. AOT 的 JSON 序列化限制
- **现象**：运行时反序列化请求体或响应体报错。
- **原因**：AOT 裁剪了大量基于反射的代码，导致 `System.Text.Json` 无法在运行时通过反射解析未注册的类型。
- **解决经验**：任何进出 API 的 DTO（Data Transfer Object）模型（如 `TestConnectionRequest` 等），必须严格在 `AppJsonContext.cs` 中注册 `[JsonSerializable(typeof(T))]`，由 Source Generator 在编译期生成解析逻辑。

---

## 三、网关架构设计哲学

### 1. 声明式配置 vs 运行时状态
- **思考**：在配置 TCP 代理时，是否应该在保存配置的一瞬间去探测目标 IP 和端口？如果目标宕机，是否应该阻断用户的保存操作？
- **决定**：**不应阻断保存**。现代反向代理（如 YARP、Envoy、Nginx）遵循“声明式配置（Declarative Configuration）”原则：控制面（API）只负责接收期望的路由状态，不应当与数据面的网络连通性物理强绑定。
- **优雅解法**：在保存逻辑中只做“本地校验”（检查本地端口是否冲突，防止 `Bind` 异常导致引擎崩溃），而针对远程目标，我们在前端界面上提供一个【按需使用的测试连通性按钮】。这种解法既提升了用户配置时的体验，又保证了架构原则的不被破坏。

### 2. L4 层引擎的零分配 (Zero-Allocation)
- TCP/UDP 四层代理引擎是基于 `System.IO.Pipelines` / `System.Net.Sockets` 及 `ArrayPool<byte>` 自主封装的。
- 考虑到 Proxy 需要极高的吞吐量，在连接泵 (`PumpAsync`) 中严格使用了 `ArrayPool<byte>.Shared.Rent()`，避免了大数据量下的 GC 抖动，这与 AOT 的低内存占用特性相得益彰。
