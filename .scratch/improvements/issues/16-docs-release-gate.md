# 16: 统一权威文档与发布门禁

**What to build:** Phase 0 第 5 项。修正 README、architecture、context、deletion-path、publish 脚本与实际代码的全部冲突；补安装/升级/回退/卸载矩阵、发布产物校验和依赖治理，使发布门禁可机械执行。

**Blocked by:** None。

**Status:** ready-for-agent

- [ ] 逐项核对 README、architecture、context、deletion-path、rules 与实际代码：命令、路径、行为、限制条件全部一致，冲突以代码为准修正文档
- [ ] 发布矩阵：安装（Velopack）、升级、回退、卸载各路径的手工步骤与预期结果，含「升级中断」「回退后数据兼容」用例
- [ ] 发布产物校验：scripts/release.sh 产物的哈希/签名验证步骤写入发布检查单，发布前必须执行
- [ ] 依赖治理：列出全部 NuGet 依赖及用途，评估升级策略与锁定方式（无集中包管理是已知现状，architecture.md 已记录）
- [ ] 发布检查单汇总为 `docs/release-checklist.md`，作为 Phase 0 完成的发布门禁
- [ ] 文档修正不改变行为语义；行为问题另立修复票

**边界：** 工单 12 的签名信任（官方公钥、签名 manifest、版本策略）不在本票范围；本票的产物校验在签名体系落地前先用 SHA512 人工核对流程。

## Comments
