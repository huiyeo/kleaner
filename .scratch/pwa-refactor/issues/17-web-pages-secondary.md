# 17 次级页面 web：隔离区、历史、启动项、设置

Type: task
Status: open
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
