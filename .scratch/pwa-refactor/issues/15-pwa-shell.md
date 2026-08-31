# 15 PWA 壳：wwwroot、manifest、SW 与重连

Type: task
Status: open
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
