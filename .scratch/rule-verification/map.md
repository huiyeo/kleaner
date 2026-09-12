# Map: Phase 5 规则库真机可信覆盖与治理深化

## Notes

- 授权出处：`docs/goals.md` Phase 5（2026-09-12 立项）。北星：103 条规则全部具备新鲜证据档案；决策包交所有者裁决。
- 铁律：自主范围 = 只读普查 + 合成夹具沙箱演练 + 代码/测试/文档。**不触真实清理、不发布渠道、不撤回规则、不升级 verified**——全部在决策包。
- 已知基线：验证覆盖率 23.3%（24/103 本机实测）；K2 的教训——未提权「absent」可能是 ACL 拒绝（Directory.Exists 静默 false），普查结论必须带 requiresElevation 语境。

## Decisions-so-far

- 2026-09-12：普查工具 `rule-census` 落在 Core（RuleCensus）+ ScanCli（动词），GlobScanner 增 `TryGetStartDir` 供路径存在性探测（单源复用，附测试）。
- 2026-09-12：verified 字段升级与默认勾选策略捆绑为决策项 D1/D4，不逐条零散升级（避免多次签名发布）。

## Fog

- 本机不存在（absent）的规则里，有多少是「应用已卸载」（撤回候选）vs「ACL 拒绝」（提权重核候选）——普查后量化。
- 22 个 DOSVC 缓存文件约 09-26 后陆续越过 14 天阈值，K2 实际清理补验窗口待观察。
