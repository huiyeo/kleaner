# 15 PWA 壳：wwwroot、manifest、SW 与重连

Type: task
Status: resolved
Blocked by: 10

## Task

按 01/05 落定前端壳：

1. `wwwroot/`：index.html、`manifest.webmanifest`、`sw.js`（缓存壳资源；散文件随包分发）；`MapStaticAssets()` + `MapFallbackToFile("index.html")`。
2. App 壳：左栏导航（隔离区 / 历史 / 工具箱 / 启动项 / 设置）、底部状态条、变体 C 容器布局（页面内容各票填充；原型 `.scratch/pwa-refactor/prototype/index.html` 为实施参照）。
3. fetch 流式 SSE 客户端 + 指数退避重连（统一覆盖提权重启与 Velopack 更新重启，03/07 语义）；token 存 `sessionStorage`、全请求带 `X-Kleaner-Token`。
4. **本票内落定两个悬置项**（写入 Comments）：本地化文案策略（后端出文案 API vs 前端 i18n，现文案在 `Strings.zh-CN.json`）；SW 缓存与更新策略细节（壳资源版本化、SW 更新提示，注意与 `RuleUpdateService` 是两回事——后者走 API）。

## Acceptance

- PWA 可安装（manifest 校验通过）；SW 缓存壳后断网可打开壳。
- 重连可验证：杀进程 → 重启 → 前端自动恢复（自动化或脚本）。
- 两个悬置项有明确结论并记录。

## Comments

- 本地化落定为前端静态 i18n：壳文案由 `wwwroot/locales/zh-CN.json` 提供，离线时回退内嵌中文文案；过渡期不新增后端文案 API，也不改动 WPF 的 `Strings.zh-CN.json`，工单 21 再统一清理双栈遗留。
- SW 策略落定为版本化壳缓存（`kleaner-shell-v1`）：安装时预缓存 HTML/CSS/JS/manifest/icon/locale；导航 network-first、失败回 `index.html`，壳资源 cache-first；永不缓存 `/api/*`，activate 清理旧壳缓存。发现新 SW 仅展示更新提示，用户点击后发送 `SKIP_WAITING` 并在 controllerchange 刷新；规则库更新仍只走 `POST /api/rules/update`。
- 验证：PwaShellTests 覆盖 manifest、SW、前端路由与带 token 的指数退避契约；Chrome 实测可安装入口、SSE 已连接、停服刷新后离线壳可用，并在同端口同 token 的 SSE 恢复后自动重连。全量 `dotnet test Kleaner.slnx -c Release --no-restore`：127/127 通过。
- 审核修正：每次 SSE 连接前先带 token 拉取 `/api/jobs`，以 `kleaner.jobs-snapshot` 事件交给页面恢复断线期间遗漏的任务快照；401/403 明确显示令牌失效并停止退避，避免旧会话无限重试。
