# 16 主界面（变体 C）web

Type: task
Status: resolved
Blocked by: 11, 12, 15

## Task

主界面按 05 选定的变体 C「分类聚合」实施：左栏环形可释放汇总 + 类别折叠卡（规则行：勾选、风险 / 管理员 / 未验证徽标、安全说明、文件数与大小）+ 底部扫描条 + 右滑确认抽屉（按类别汇总 → 确认移入隔离区 → 结果含批次号与跳过数）。

- 默认勾选策略走 09 的 `RuleSelectionPolicy`；未验证项标「未验证·默认不勾选」且不勾选。
- 扫描进度接 11 的每规则事件，可取消；plan → confirm 走 12 的删除闸流程。

## Acceptance

- scan → plan → confirm 全流程在浏览器可用，预览 / 二次确认语义与 WPF 版一致（四道保险的产品语义不弱化）。
- 提权场景（requiresElevation 规则）走「重启中… + 重连」流程可用。
- 原型对照验收：信息架构与交互不偏离变体 C 决策；中文文案完整（按 15 落定的策略）。

## Comments

- 已实现变体 C 主流程：类别折叠卡使用中文类别名，规则行显示默认选择、风险、验证与管理员权限信息；左栏汇总随勾选变化。
- 每条 `scan.progress` SSE 事件累计已完成规则、文件数和大小；扫描可取消。计划与确认仍只调用 12 的一次性 plan/confirm 闸，确认页明确仅移入隔离区。
- `POST /api/elevate` 以同端口、同 token 的 runas 子进程交接；旧宿主在响应后退出，前端 SSE 退避重连。已覆盖已提权拒绝、交接参数以及 `dotnet app.dll` 启动时保留 DLL 参数。
- 验证：`dotnet test Kleaner.slnx -c Release --no-restore`（Core 60、WebHost 74 全绿）；`node --check` 与 `git diff --check` 通过；本地浏览器无令牌壳页验收确认中文变体 C 初始布局和安全文案。未生成计划或执行任何文件移动。
