# 17 次级页面 web：隔离区、历史、启动项、设置

Type: task
Status: resolved
Blocked by: 13, 14, 15

## Task

四个次级页面按 05 栈实施，调用 13/14 的 API：

- 隔离区：批次列表 / 整批还原 / 删除单批 / 清空 7 天前批次。
- 历史：只读列表。
- 启动项：禁用 / 还原，HKLM 提权走重连流程。
- 设置：3 项读写。

## Acceptance

- 删除路径相关操作的 UI 二次确认强度不低于 WPF 版（删除批次 / 清空明示不可还原）。
- 设置改动后 CLI 侧行为一致（同一 settings.json）。
- 6/7 窗口对等清点：Main(16) + Toolbox(18) + Quarantine/History/Settings/Startup(本票) 全部落位。

## Comments

- 隔离区、历史、启动项和设置已接入 13/14 的既有 API；设置直接读写与 CLI 共用的 `settings.json`。
- 删除隔离区批次与清空 7 天前批次均在浏览器中作即时二次确认，并明确「永久删除、不可还原」；还原仍遵循不覆盖原路径的服务端语义。
- 启动项页区分启用与已禁用备份。HKLM 项在非管理员宿主时先调用 16 的 `/api/elevate`，以同端口同 token 重启并通过 SSE 重连，再由用户再次确认禁用。
- 验证：浏览器无令牌壳页确认隔离区信息结构及不可还原文案；`dotnet test Kleaner.slnx -c Release --no-restore`（Core 60、WebHost 75 全绿），`node --check` 与 `git diff --check` 通过。未触发任何写入接口。
