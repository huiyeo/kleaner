# 01: 主流程端到端验收

**What to build:** 以真实应用（GUI + CLI）走完 goals.md Phase 1 第 1 条的完整主流程——扫描、解释（安全性说明）、预览、确认、隔离、还原、历史、官方规则更新——并在沙箱化环境逐站取证。已有分段证据（Phase 0 各工单），本票补的是**全流程贯通**的验收记录与暴露缺口的修复。

**Blocked by:** ~~GUI 无法注入隔离的 settings/history/rules~~（已解决，见 Comments 2026-09-10 走查记录：settings.json 隔离区重定向 + 本地覆盖规则实现 settings/隔离区/rules 三项隔离；history 为真实审计按宪法保留）。

**Status:** complete

- [x] GUI 主流程：扫描 → 勾选 → 确认对话框 → 隔离区批次出现 → 设置页检查规则更新（官方签名通道）→ 还原批次 → 历史窗口逐条对应
- [x] CLI 主流程：`scan`（dry-run）→ `clean --apply --yes` → 隔离区批次 → 通过 GUI 还原 → 还原后 `scan` 确认（CLI 当前没有 `restore` 子命令）
- [x] 「解释」验收：每条勾选规则能在界面看到安全性说明（safetyNotes 直达）
- [x] 主流程各站失败分支的既有提示不回退（沿用 Phase 0 回归）
- [x] 走查发现的问题逐项立票或当场修复（修复须附回归）
- [x] 验收记录写入本票 Comments；全程零真实用户数据写入（沙箱目录或可还原夹具）

**边界：** 不测断电（02 票）；不测安装/升级（05 票）；写操作仅作用于注入临时夹具，禁止真实用户数据写入。仅改 APPDATA 变量不足以完整隔离。

## Comments

- 2026-09-10：Release build 退出码 0，0 warnings/0 errors；`dotnet test Kleaner.slnx -c Release` 退出码 0，189/189 通过。证据：`.scratch/v1-core/evidence/01-cli-preflight.md`。
- 2026-09-10：CLI workspace GUID 夹具链路：scan 退出码 0 命中 2 文件/18 bytes；dry-run 退出码 0、2 文件、原始 SHA-256 不变、无 history/quarantine 写入；`clean --apply --yes` 退出码 0，移动 2 文件、18 bytes、跳过 0；apply 后 scan 为 0 文件。恢复由显式注入的 `QuarantineManager.RestoreBatch` 完成，2 项完整恢复且 SHA-256 与原始一致；恢复后 scan 重新命中 2 文件。CLI 没有 restore 子命令，Core/Executor 恢复证据不能写成 CLI 完整 E2E。
- 2026-09-10：独立单文件夹具审计读回为 `Broken=false`、`HeadMismatch=false`、`IsValid=true`；未将该独立样本扩展为完整 6 行清理/恢复历史的逐行链读回结论。
- 2026-09-10：GUI Release 只读观察：真实规则自动扫描后取消，状态为”已取消”；单条”用户临时目录”规则的 `safetyNotes` 完整可见。未点击清理、保存设置、规则更新或隔离区。GUI 主链因隔离入口缺失保持 blocked。
- 2026-09-10：工单原文”还原（通过 GUI 或二次扫描）”已纠正为”通过 GUI 或显式恢复入口还原，随后再次 scan 确认”；二次 scan 只读，不能执行还原。
- 2026-09-10（第二段，电脑操作走查）：**GUI 隔离入口问题已解决并完成主链走查**。方法：`%APPDATA%\Kleaner\settings.json` 临时重定向隔离区到沙箱目录 + `%APPDATA%\Kleaner\rules\rules.v1.json` 本地覆盖为单条夹具规则（LoadEffective 官方覆盖通道）——settings/隔离区/rules 三项隔离达成；history 不隔离，GUI 清理与还原的真实审计条目按宪法第 5 条保留（真实动作的真实记录，不是用户数据）。逐站证据：① 启动自动扫描仅显示夹具规则（2 文件/35B），「未验证·默认不勾选」可见；② 未勾选清理被守卫对话框拒绝；③ 选中行后安全性说明面板显示夹具 safetyNotes 全文；④ 清理所选 → 确认对话框（2 文件/35B/7 天/可还原）→ 确认 → 清理完成含批次 ID；⑤ 隔离区窗口 GUI 与 CLI 批次并列，逐批还原「已还原 2 个文件」×2，文件系统核验 4 个夹具文件全部回原位、隔离区仅剩 lock/pending-audit；⑥ 设置页官方源只读展示 + 检查更新 → 真实网络下载 rules-channel 签名清单，Ed25519 验证通过，101 条官方规则落盘本地覆盖 + manifest-state（1.0.0）+ last-good——工单 12 信任链真实网络端到端打通；⑦ 历史窗口审计逐条对应且哈希链提示条（6 条旧记录未链接）正确显示。测试后环境还原（settings.json/rules 目录删除，测试前均不存在；审计条目保留）。此前”GUI 隔离入口缺失”的 blocked 理由就此解除；CLI 证据（上文）与本走查合并为完整主链验收。
