# 07: D1 真实清理演练执行 + D3 签名发布 v1.2.0

**What to build:** 决策包裁决（所有者 2026-09-12「全按建议来」）后执行：对 9 条 Present 未验证规则分批真实清理演练（真实隔离区 + 真实审计），升级通过者 verified → 本机实测（D4c 类别策略），全库证据复核日回填，重签 v1.2.0 并推送 rules-channel（D3a）。

**Blocked by:** 03（决策包）+ 所有者裁决。

**Status:** complete

**演练结果（2026-09-12）：**

| 规则 | 结果 | 证据 |
|---|---|---|
| dev-pnpm-cache | ✅ 2902 项 / 858.8MB，skipped=0 | 沙箱→真实隔离区批次 manifest 2902 项；pnpm 健康抽查通过 |
| dev-android-user-cache | ✅ 42 项 / 3.4MB，skipped=0 | 批次 manifest + 审计 |
| windows-temp | ✅ 35 项 / 9.9MB，**skipped=1（占用跳过活体证据）** | 审计如实记 partial（宪法第 5 条自证） |
| wer-reports | ✅ 16 项 / 0.4MB，skipped=0 | 批次 manifest + 审计 |
| dev-jetbrains-cache / qoder-updater / workbuddy-desktop-updater | ➖ 0 候选（无可清文件），不升级 | 干跑记录 |
| system-stale-partial-downloads | ➖ 主动跳过：age-0 会误伤进行中的下载 | 本票记录 |
| kernel-dumps | ➖ 无真实样本（合成演练已于 K1 票完成） | 工单 04（phase4） |

合计真实清理 2995 项 / 872.5MB，全部移入真实隔离区可还原；审计链完整追加（含 partial 如实记录）。

## Comments

- 2026-09-12：所有者裁决落地。D2a：本机缺失≠撤回依据，本次无撤回候选；D5 维持观察；D6 待模型服务；D7b 绑定下次发版。
- 2026-09-12：**v1.2.0 首发踩坑重签**——工作区 rules.v1.json 被 CRLF 化（Edit 工具写入），签名摘要来自 CRLF 字节而线上 blob 为 LF，客户端必然 fail-closed 拒收；线上核验（SHA512 比对）当场抓出。修复：以 `git blob` 同源 LF 形式重签重发（rules-channel 290f360），线上摘要+签名双核验通过。**根治**：sign-rules.ps1 增加强制 LF 归一化（摘要与发布副本同源 LF），后续签名不再可能踩中。升级后治理报告：验证覆盖率 27.2%（28/103）≥25% 阶段目标，警告消失；默认勾选 28。
