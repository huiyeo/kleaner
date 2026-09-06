# 16: 统一权威文档与发布门禁

**What to build:** Phase 0 第 5 项。修正 README、architecture、context、deletion-path、publish 脚本与实际代码的全部冲突；补安装/升级/回退/卸载矩阵、发布产物校验和依赖治理，使发布门禁可机械执行。

**Blocked by:** None。

**Status:** complete

- [x] 逐项核对 README、architecture、context、deletion-path、rules 与实际代码：命令、路径、行为、限制条件全部一致，冲突以代码为准修正文档
- [x] 发布矩阵：安装（Velopack）、升级、回退、卸载各路径的手工步骤与预期结果，含「升级中断」「回退后数据兼容」用例
- [x] 发布产物校验：scripts/release.sh 产物的哈希/签名验证步骤写入发布检查单，发布前必须执行
- [x] 依赖治理：列出全部 NuGet 依赖及用途，评估升级策略与锁定方式（无集中包管理是已知现状，architecture.md 已记录）
- [x] 发布检查单汇总为 `docs/release-checklist.md`，作为 Phase 0 完成的发布门禁
- [x] 文档修正不改变行为语义；行为问题另立修复票

## Comments

审计结果与修正（2026-09-06）：

1. **README 三处修正**：①「缓存类 7 天」改为「浏览器缓存类 7 天」（rules.v1.json 的 `ageDaysByCategory` 实际为 temp 14 / browser-cache 7 / dev-cache 14 / system 14）；② CLI 清单补 `startup-test` 与性能基准 `gen-dataset`/`bench`；③ 操作历史条目补哈希链说明（检测非预防）。
2. **rules.md「channel 代码从不读取」声明经代码检索证实准确**；101 条规则计数与 README 一致。
3. **publish.md 标注为 v0.1.0 历史记录**，后续发布以 release-checklist 为准；其「更新源」表述的隐患已在检查单中如实声明：**应用内自动更新未接线**（Program.Main 仅有 VelopackApp 钩子，无 UpdateManager 检查逻辑），当前升级路径=手动运行新版 Setup，发布说明不得宣称在线更新。
4. **升级/回退数据兼容结论有代码依据**：旧版构建反序列化新记录时忽略未知 `prev` 字段（System.Text.Json 默认行为），新记录在旧版展示为普通条目；旧记录在新版显示为未链段（真实环境走查已验证）。矩阵中四行真机验证项（覆盖升级/升级中断/回退/卸载）标记为待执行，发布前必须逐项完成并记录。
5. **context.md 术语表补充两个已固化领域词**：待补记凭据（含「不区分中断与进行中」的易错点）、哈希链与链检查点。
6. 依赖治理：全部 7 个 NuGet 依赖列表化并给出升级策略（MDIX 大版本需重走 ADR 0001 窗口回归；Velopack 升级需重测发布矩阵）。

文档修正均为说明性文字与清单，无行为语义改动；自动化与真机验收状态不变（完整测试 175/175、Release 零警告零错误）。
