# 避坑指南：Windows 平台文本编码与前端语法错误

## 现象与背景
在开发和维护 ProxyYARP 的过程中，前端 Vue 单文件/模板内曾遇到以下两种形式的致命报错，最终页面白屏，控制台抛出如下错误。这两种报错均由**字符编码错乱（Mojibake）**导致：

1. **模板编译报错 (Template compilation error)**：
   ```text
   [Vue warn]: Template compilation error: Error parsing JavaScript expression: Invalid or unexpected token
   ```
2. **JavaScript 语法报错**：
   ```text
   Uncaught SyntaxError: Invalid or unexpected token
   ```

---

## 案例一：多字节字符乱码导致字符串未闭合
**场景：**
前端 HTML 模板中包含了一个对号图标 `✓`，代码为 `{{ toast.isError ? '!' : '✓' }}`。

**原因：**
当该 UTF-8 编码的文件在不同编辑器或系统环境被以外部系统默认编码（如 GBK/ANSI）打开和重新保存时，`✓` 会被错误转码并变成 `鉁?`。
最致命的是，这种基于字节错位的乱码过程经常会“吞噬”掉字符后方本应闭合的**单引号**，导致表达式变成了 `'鉁? `。因为字符串缺失结尾引号，Vue 模板解析器在提取其中的 JS 表达式时，立刻抛出了 `Invalid or unexpected token`。

**解决方案：**
不要在 JS 字符串表达式中直接硬编码容易受到各平台编辑器编码规则影响的特殊符号。应当采用其 **Unicode 转义序列** 来硬编码，这样无论外部文件编码怎么变，都不影响 JS 引擎解析。
例如，把对号替换为安全格式：
```html
<span class="mr-2">{{ toast.isError ? '!' : '\u2713' }}</span>
```

---

## 案例二：PowerShell 脚本破坏 UTF-8 (无 BOM) 文件
**场景：**
使用本地 PowerShell 脚本自动化对前端 `index.html`（其中包含了完整的 `zh-CN` 中文翻译对象）进行字符串替换，脚本运行后页面直接崩溃，提示行数正好是第一行中文翻译的位置。

**原因：**
在 Windows 环境下，PowerShell 5.1（Desktop 版）的 `Get-Content` cmdlet 默认会使用系统 ANSI 代码页（中文 Windows 为 GB2312/GBK）去读取文件。
当它遇到一个**不带 BOM 头的 UTF-8** 文本文件时，它不会自动识别其为 UTF-8，而是将其中的 UTF-8 字节流强行按 GBK 字符去解读（产生乱码）。随后，通过管道将这些内存里的乱码文本传递给带有 `-Encoding UTF8` 参数的 `Set-Content` 保存时，这些畸形乱码就被“永久固化”存入了文件。

此时，如果原文是 `title: '服务端管理',`（后面紧跟一个单引号），由于 UTF-8 的中文字节被按双字节的 GBK 截断和重组，最后剩余的字节刚好将那个**单引号字符 (`\x27`)** 吸纳了进去，组合成了一个新的、奇怪的汉字。
结果就变成了类似 `title: 'ProxyYARP - 鏈嶅姟绔鐞?,`，使得 JavaScript 字典对象里的字符串缺少了结束的单引号，再次引发全局语法崩溃。

**解决方案：**
1. **优先使用 Python、Node.js 编写文件处理脚本**：跨平台工具库在处理文本时的默认编码或显式编码选项更加可靠，例如 Python 的 `open('file', encoding='utf-8')` 是防错的最佳实践。
2. **如果在 PowerShell 中操作，请使用 .NET 底层方法**：
   如果不得不用 PS 脚本处理包含中文或非 ASCII 字符的代码文件，**坚决不要用** `Get-Content / Set-Content`，必须使用 .NET 底层的强类型 IO 方法，显式指定 UTF-8：
   ```powershell
   # 错误做法：
   # $content = Get-Content "index.html" -Raw
   # Set-Content "index.html" -Value $content -Encoding UTF8

   # 正确做法：
   $path = "index.html"
   $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
   $content = $content -replace "A", "B"
   # 注意：不带 BOM 的 UTF8 可以通过实例化一个不带BOM标记的 UTF8Encoding 对象来实现，这里简写
   [System.IO.File]::WriteAllText($path, $content, [System.Text.Encoding]::UTF8)
   ```

## 总结建议
1. 对于含有多语言或特殊字符的文件，统一团队所有的编辑器强制采用 **UTF-8 无 BOM** 格式保存。
2. JS/Vue 代码中的单体特殊符号，首选 `\uXXXX` 编码转义替代字面量。
3. 对代码仓库实施文本替换、预编译处理的 CI/CD 或自动化工具链，务必先用小文件验证字符集编解码是否存在隐患。
