# AGENTS.md

## 项目概览

ProxyYARP 是一个自托管的多租户反向代理管理端，.NET 后端（YARP + SQLite）+ 原生 Vue 3 前端。
前端页面为单 HTML 文件，位于 `src/ProxyYARP/wwwroot/`：`index`、`clusters`、`routes`、`keys`、`login`。

## 前端校验：Vue setup() 绑定完整性

### 规则

前端页面是单个 HTML 文件，模板与 `setup()` 写在同一处。Vue 3 组合式 API 中，**只有写进 `setup()` 末尾 `return` 列表的项，模板才能访问**。

漏写时页面照常渲染，不会立刻报错；直到用户点击按钮才抛：

```
TypeError: openWebsiteModal is not a function
```

因此：**在 `setup()` 中新增任何函数或状态后，必须同步补进 `return` 列表。**

### 校验命令

```bash
node src/ProxyYARP/wwwroot/tools/verify-vue-bindings.js
```

退出码：`0` 全部通过；`1` 参数错误；`2` 存在缺失绑定。

脚本会实际执行 `setup()` 拿到真实返回的绑定，再解析模板中所有表达式（`@click`、`v-model`、`v-if`、`{{ }}`、`:bind`、`v-for`）提取根标识符，逐一比对。它已覆盖全部 5 个页面，新增页面时把文件名（不含 `.html`）加进脚本的 `PAGES` 数组。

### 约束

- **只做静态校验，不引入构建链路。** 仓库没有 `package.json`，也不打算引入 npm/Vite 构建。该脚本是独立的 Node 脚本，仅读取文件，不产出任何东西。
- Vue 与 Tailwind 仍走 CDN `<script src>`，不打包、不本地化。
- 改前端时若新增页面，需确认它的架构属于哪一种：模板写在 HTML 里（如 `index.html`），还是写在 JS 的 `template: \`...\`` 选项里（如 `login.html`）。脚本两种情况都支持。

### 提交前

改完 `wwwroot/*.html` 后跑一次上述命令。报错会明确指出缺哪个名字以及属于哪个页面。
