# 02: 断电场景验收评估

**What to build:** 评估 goals.md「失败时保留数据」宪法在真正断电（掉电即失，文件系统缓冲丢失）下的表现，并给出验收或立票结论。已知事实：CrashWorker 的 `Environment.Exit(73)` 执行 finally 之外的即时退出，但**不等于掉电**——NT 缓存管理器中的脏数据在掉电时会丢失，两种中断窗口不同。

**Blocked by:** 01（主流程端到端验收的结论决定本票的测试面）。

**Status:** blocked

- [ ] 枚举掉电敏感窗口：pending manifest 临时文件、`.pending-audit` 凭据、history.jsonl 尾行、`.operation.lock` 句柄、rules.last-good.json 替换
- [ ] 论证每一窗口在「已 Flush/WriteThrough 落盘」前提下的掉电后果（哪些数据保证在盘、哪些可能半截）
- [ ] 可行时用可掉电环境（VM 硬断电脚本）验收；不可行则如实记录能力边界并给出置信论证
- [ ] 结论三选一：验收通过记录 / 缺口立新票 / 明确写入已知局限

**边界：** 不修改落盘协议来「修复」未证实的掉电问题——先证据后代码。

## Comments
