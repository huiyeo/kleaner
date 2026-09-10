# 02: 断电场景验收评估

**What to build:** 评估 goals.md「失败时保留数据」宪法在真正断电（掉电即失，文件系统缓冲丢失）下的表现，并给出验收或立票结论。已知事实：CrashWorker 的 `Environment.Exit(73)` 执行 finally 之外的即时退出，但**不等于掉电**——NT 缓存管理器中的脏数据在掉电时会丢失，两种中断窗口不同。

**Blocked by:** ~~01~~（已收口，测试面确定）。

**Status:** complete

- [x] 枚举掉电敏感窗口：pending manifest 临时文件、`.pending-audit` 凭据、history.jsonl 尾行、`.operation.lock` 句柄、rules.last-good.json 替换
- [x] 论证每一窗口在「已 Flush/WriteThrough 落盘」前提下的掉电后果（哪些数据保证在盘、哪些可能半截）
- [ ] VM 硬断电实测：**本环境无掉电注入能力，未执行**——如实记录为能力边界（见 Comments 结论三的「已知局限」选项）
- [x] 结论：**置信论证通过**（协议行为与掉电语义一致，依据见 Comments）+ 已知局限写入 deletion-path.md

**边界：** 不修改落盘协议来「修复」未证实的掉电问题——先证据后代码。

## Comments

评估结论（2026-09-10，置信论证级——本环境无 VM 硬断电注入能力，未做实测，如实声明）：

**代码证据**（grep 全量核对）：全部五类关键写入均使用 `FileOptions.WriteThrough` + `Flush(flushToDisk: true)` 强制落盘 + 同目录临时文件 `File.Move` 原子替换——manifest（QuarantineManager.WriteManifestAtomic）、待补记凭据（WritePendingAuditReceipt）、history 追加与哈希链 head（HistoryManager.Append/AppendOnce/UpdateHead）、规则更新替换（RuleUpdateService.AtomicWrite）。

**逐窗口掉电后果论证**：
1. **批次 manifest**：WriteThrough+原子替换 ⇒ 掉电后要么旧版完整要么新版完整（NTFS 元数据日志保证替换原子性）；半截 `.tmp` 遗留不被任何读取路径消费，空目录收尾与读取路径均不消费 tmp ⇒ 不破坏还原能力。RESTORING 意图与 SHA256 摘要已先行落盘 ⇒ 掉电后重试核对可恢复。
2. **待补记凭据**：同上原子性 ⇒ 掉电后凭据要么在（下次操作补记，这正是协议目标）要么已被完整移除（历史已写）⇒ 不丢审计。
3. **history.jsonl 尾行**：Flush 落盘；掉电可能留下半截行 ⇒ 读取端逐行 try-catch 跳过 + 65536 上限 + 哈希链断裂可见（工单 14）⇒ 不阻塞展示，篡改/损坏可发现；AppendOnce 幂等防重复补记。
4. **.operation.lock**：句柄由 OS 持有，掉电即释放 ⇒ 无残留锁（设计如此，不靠文件删除）。
5. **rules.last-good/manifest-state**：原子替换 + 损坏时回退链（覆盖→last-good→内置）⇒ 拒绝降级逻辑不受影响。

**结论：置信论证通过。** 协议在掉电下的预期行为与「失败时保留数据」宪法一致：数据要么完整保留在盘（配合恢复协议可还原/补记），要么保持旧一致状态；不存在「半批移动且无法恢复」的窗口。**已知局限**：① 无 VM 硬断电实测，本结论为设计语义+落盘机制的论证，非实测验收；② NTFS 数据日志不覆盖文件内容本身，依赖 WriteThrough 的显式冲刷——若未来新增写入路径必须沿用同一模式（deletion-path.md 已有约定）。