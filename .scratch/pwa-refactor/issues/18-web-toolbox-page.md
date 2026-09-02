# 18 工具箱页面 web

Type: task
Status: resolved
Blocked by: 14, 15

## Task

工具箱页：large-files / duplicates / usage 三视图（v1 treemap 降级为纯列表，06 已放宽，v1.x 补回 Squarify），接 14 的 API 与 11 的 job 取消；大件跳转系统工具的指引保留（`SystemToolGuide` 语义）。

## Acceptance

- 三个扫描可发起、可取消，结果与 WPF 版一致（同输入同输出）。
- 伪规则 id（large-files / duplicates）不出现在任何规则关联展示中。

## Comments

- 三个只读 job（大文件、重复文件、空间占用）均接入 14 的 API 与 11 的取消端点；结果仅作为分析列表展示，不会生成规则、隔离区批次或历史记录。
- 新增只读 `GET /api/tools/system-guide`，直接复用 `SystemToolGuide.Items` 展示系统大件处理指引，WebHost 不执行其中命令。
- 验证：`dotnet test Kleaner.slnx -c Release --no-restore`（Core 60、WebHost 77 全绿），`node --check` 与 `git diff --check` 通过。
