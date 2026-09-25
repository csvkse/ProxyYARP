// ============================================================================
// verify-vue-bindings.js —— Vue 组合式 API 绑定完整性校验（静态检查，非构建）
//
// 作用：这些页面是单个 HTML 文件，模板和 setup() 写在同一个文件里。
//       setup() 里定义的函数/状态只有写进 return 列表，模板才能访问；
//       漏写时页面照常渲染，点按钮才抛 "xxx is not a function"。
//       本脚本实际执行 setup() 拿到真实返回的绑定，再比对模板里所有
//       表达式引用的根标识符，把这个错误挡在提交前。
//
// 用法：
//   node wwwroot/tools/verify-vue-bindings.js            # 校验全部页面
//   node wwwroot/tools/verify-vue-bindings.js index      # 只校验指定页面
//
// 退出码：0 全部通过；1 用法/参数错误；2 存在缺失绑定
//
// 说明：本脚本只读文件、不写任何产物，也不参与前端构建链路。
// ============================================================================

const fs = require('fs');
const path = require('path');

const WWWROOT = path.resolve(__dirname, '..');
const PAGES = ['index', 'clusters', 'routes', 'keys', 'login'];

// 只接受命令行给出的页面名，避免路径穿越
const requested = process.argv.slice(2).filter(a => !a.startsWith('-'));
const targets = requested.length
  ? requested.map(p => p.replace(/\.html$/, ''))
  : PAGES;
const unknown = targets.filter(p => !PAGES.includes(p));
if (unknown.length) {
  console.error(`未知页面: ${unknown.join(', ')}`);
  console.error(`可用页面: ${PAGES.join(', ')}`);
  process.exit(1);
}

// ---- 提取 Vue app 的定义并执行 setup() ----
// 页面里的 Vue 来自 CDN <script src>，且依赖 tailwind.config、localStorage、
// fetch 等；这里用最小 mock 顶掉外部依赖，只关心 setup() 的返回值。
function normalizeScript(src) {
  return src
    // ESM 页面：import { createApp, ref } from '...' -> 命名导入换成 mock
    .replace(/import\s*\{([^}]*)\}\s*from\s*['"][^'"]*['"]\s*;?/g,
             (_, names) => names.split(',')
               .map(n => n.trim()).filter(Boolean)
               .map(n => `const ${n}=Vue[${JSON.stringify(n)}];`).join(' '))
    // 经典页面：const { createApp, ref, ... } = Vue;
    .replace(/const \{ createApp, ref, reactive, computed, onMounted, watch \} = Vue;/,
             'const createApp=captureApp, ref=Vue.ref, reactive=Vue.reactive, computed=Vue.computed, onMounted=Vue.onMounted, watch=Vue.watch;')
    .replace(/createApp\(\{/, 'captureApp({')
    .replace(/\.mount\([^)]*\)\s*;?/, '');
}

const Vue = {
  ref: (v) => ({ value: v }),
  reactive: (v) => v,
  computed: () => ({ value: undefined }),
  onMounted: () => {},
  watch: () => {},
  createApp: () => ({ mount() {} }),
};

const window = { addEventListener() {}, location: { pathname: '/', search: '' } };
const localStorage = { getItem: () => null, setItem() {}, removeItem() {} };
const document = { title: '' };
const fetch = async () => ({ ok: true, json: async () => ({}) });
const tailwind = { config: {} };
const pageConsole = { log() {}, warn() {}, error() {} };

function runSetup(appScript) {
  let appDef = null;
  const code = normalizeScript(appScript);

  const runner = new Function(
    'Vue', 'captureApp', 'window', 'localStorage', 'document', 'fetch', 'console', 'tailwind', code
  );
  runner(Vue, (def) => { appDef = def; return { mount() {} }; },
         window, localStorage, document, fetch, pageConsole, tailwind);

  if (!appDef || !appDef.setup) throw new Error('未能捕获 Vue app 定义');
  return appDef.setup();
}

// 找到定义 Vue app 的那个 script 块（第二个 script 是 i18n/工具代码，不含 app）
function extractAppScript(html) {
  // <script>、<script type="module"> 都要覆盖
  const blocks = html.split(/<script(?:\s[^>]*)?>/).slice(1).map(p => p.split(/<\/script>/)[0]);
  const app = blocks.find(b => /createApp\(/.test(b));
  if (!app) throw new Error('文件中没有找到 createApp(...) 调用的 script 块');
  return app;
}

// ---- 从模板里提取引用了哪些根标识符 ----
// 模板有两处来源：HTML 里的行内模板，以及 setup 之外 JS 中 template: `...` 的字符串。
function collectTemplateIdentifiers(html) {
  const appScript = extractAppScript(html);
  // 取出 template: `...` / template: '...' / template: "..." 的字符串内容
  let jsTemplate = '';
  const tm = appScript.match(/template\s*:\s*`([\s\S]*?)`/)
           || appScript.match(/template\s*:\s*'([\s\S]*?)'/)
           || appScript.match(/template\s*:\s*"([\s\S]*?)"/);
  if (tm) jsTemplate = tm[1];

  const tpl = (html + '\n' + jsTemplate)
    .replace(/<style[\s\S]*?<\/style>/g, '')       // Tailwind 工具类名不是标识符
    .replace(/<script(?:\s[^>]*)?>[\s\S]*?<\/script>/g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\sclass="[^"]*"/g, ' ');

  const identifiers = new Set();
  const addRoots = (expr) => {
    let e = expr
      .replace(/'(?:[^'\\]|\\.)*'/g, ' ')          // 字符串内容是值，不是引用
      .replace(/"(?:[^"\\]|\\.)*"/g, ' ')
      .replace(/(^|[{,\s])([A-Za-z_$][\w$]*)\s*:/g, '$1 ')  // 对象字面量的 key
      .replace(/\.[A-Za-z_$][\w$]*/g, ' ')          // 成员访问的右侧
      .replace(/\[[^\]]*\]/g, ' ');
    for (const m of e.matchAll(/([A-Za-z_$][\w$]*)/g)) identifiers.add(m[1]);
  };

  for (const m of tpl.matchAll(/@[a-z]+="([^"]*)"/g)) addRoots(m[1]);        // 事件处理
  for (const m of tpl.matchAll(/:[a-z-]+="([^"]*)"/g)) addRoots(m[1]);       // 属性绑定
  for (const m of tpl.matchAll(/v-(?:if|else-if|show|html|model|text)="([^"]*)"/g)) addRoots(m[1]);
  for (const m of tpl.matchAll(/v-for="[^"]*?\sin\s+([A-Za-z_$][\w$]*)/g)) identifiers.add(m[1]);
  for (const m of tpl.matchAll(/\{\{([^}]*)\}\}/g)) addRoots(m[1]);          // 插值

  // 这些词不是 setup() 该暴露的：JS 内建、关键字、v-for 别名
  const allowlist = new Set([
    'Math', 'Date', 'JSON', 'Object', 'Array', 'String', 'Number', 'Boolean',
    'parseInt', 'parseFloat', 'isNaN', 'isFinite', 'encodeURIComponent', 'decodeURIComponent',
    'true', 'false', 'null', 'undefined', 'new', 'typeof', 'in', 'of', 'if', 'else',
    'console', 'window', 'document', 'event',
    'includes', 'length', 'map', 'filter', 'join', 'push', 'trim', 'split',
    'toLowerCase', 'toUpperCase', 'replace', 'startsWith', 'endsWith', 'indexOf',
    'some', 'every', 'forEach', 'find', 'reduce', 'keys', 'values', 'entries', 'assign', 'from',
    'toLocaleString', 'substring', 'substr', 'slice', 'toString', 'concat', 'sort', 'reverse', 'toFixed',
    't', 'idx', 'i', 'index', 'key', 'id', 'type', 'value', 'item', 'nav',
    // v-for 别名：本仓库页面里用到的循环变量
    'w', 'n', 'c', 'r', 'd', 'k', 'g', 'grp', 'route', 'dest', 'node', 'cluster',
  ]);

  return [...identifiers].filter(id => !allowlist.has(id));
}

// ---- 逐个页面比对 ----
let failures = 0;
for (const page of targets) {
  const file = path.join(WWWROOT, `${page}.html`);
  if (!fs.existsSync(file)) {
    console.error(`${page}.html 不存在`);
    failures++;
    continue;
  }
  const html = fs.readFileSync(file, 'utf8');
  if (!/createApp\(/.test(html)) {
    console.log(`── ${page}.html：没有 Vue app，跳过`);
    continue;
  }

  try {
    var bindings = new Set(Object.keys(runSetup(extractAppScript(html))));
  } catch (e) {    console.error(`── ${page}.html：执行 setup() 失败 — ${e.message}`);
    failures++;
    continue;
  }

  const referenced = collectTemplateIdentifiers(html);
  const missing = referenced.filter(id => !bindings.has(id));

  if (missing.length) {
    failures++;
    console.log(`── ${page}.html：FAIL (setup 返回 ${bindings.size} 项，${missing.length} 项缺失)`);
    for (const m of missing) console.log(`     - ${m}`);
  } else {
    console.log(`── ${page}.html：PASS (绑定 ${bindings.size} 项，模板引用 ${referenced.length} 项全部匹配)`);
  }
}

if (failures) {
  console.log(`\n结果：${failures} 个页面失败 — 检查 setup() 的 return 列表是否漏写了函数/状态`);
  process.exit(2);
}
console.log('\n结果：全部通过');
