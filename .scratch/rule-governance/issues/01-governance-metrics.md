# 01: 规则治理指标定义与测量工具

**What to build:** 为 goals.md Phase 2 的五项指标给出**可测量的定义**与测量工具（CLI 子命令或脚本），对 `rules/rules.v1.json` 输出机器可读的指标报告：

- **软件覆盖率**：规则 `paths` 命中的目标软件集合 / 分类覆盖（可从规则数据直接计算）
- **证据覆盖率**：具备 `safetyDoc` 锚点且 `safetyNotes` ≥ 20 字的规则占比
- **验证覆盖率**：`verified` 以「本机实测」开头的规则占比（已有语义，复用 RuleSetLoader）
- **推荐准确率**：定义需要数据（默认勾选规则在用户扫描中的命中分布做代理——本票先实现「默认勾选规则的扫描命中率」代理指标，准确率精确定义标记待确认）
- **误伤事件数**：归 02 票，本票留接口

**Blocked by:** None。

**Status:** ready-for-agent

- [ ] 五项指标的可测量定义写入 `docs/rule-governance.md`（含口径、数据源、不可测部分的声明）
- [ ] `Kleaner.Core` 新增指标计算（纯函数，附 xunit：全量/空库/边界）
- [ ] CLI 子命令（如 `governance-report`）输出 JSON + 文本
- [ ] 对 101 条规则库跑出首份基线报告
- [ ] Release/变更不改变任何清理行为（纯只读测量）

**边界：** 不修改 rules.v1.json；「准确率」精确定义与「误伤」归 02/03 票。

## Comments
