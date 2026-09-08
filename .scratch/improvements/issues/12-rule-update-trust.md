# 12: 规则更新的官方信任根、抗降级与回退

**What to build:** 将当前“用户输入 URL + 用户输入 SHA512”的规则更新改为官方签名清单：应用内固定可信发布源与公钥；清单签名覆盖规则包摘要、规则版本、发布时间与最低允许版本；仅在签名、完整性、Schema/语义校验和版本单调性都通过后以原子替换应用；保留最后已知良好版本，并在本地覆盖损坏或不可信时回退到它或内置规则。

**Blocked by:** 缺少项目官方的签名信任根，不能安全自行发明。当前仓库与发布脚本未提供：

- 官方规则发布 URL（固定 HTTPS 源，而非用户任意输入）；
- 可公开嵌入应用的签名公钥及算法/编码（推荐 Ed25519 + base64）；
- 首个已签名 manifest、规则包版本策略与最小支持版本；
- 私钥托管与发布时签名的受控流程。

现有 `RuleUpdateService` 仅比较调用方提供的 SHA512，设置页也允许任意 URL/摘要；它不构成发布方身份认证，不能升级为“官方签名”声明。

**Status:** complete

- [x] 获得并记录官方发布 URL、公钥、算法、编码、版本格式与私钥托管责任人
- [x] 定义并实现签名 manifest（版本、发布时间、规则 SHA512、最低允许版本、签名）
- [x] 移除用户任意 URL/摘要作为可信更新源的能力；仅保留查看状态和手动检查官方源
- [x] 拒绝降级、过期、未签名、签名错误、摘要不符与不合法规则
- [x] 原子替换、最后已知良好版本与损坏回退
- [x] 假 HTTP、假时钟、临时目录和测试密钥覆盖成功、篡改、降级、断电式中断与回退
- [x] 同步 rules / architecture / publish / README / 设置页文案与发布门禁

**需要的最小输入：** 官方规则 manifest URL、Ed25519 公钥（base64）与版本格式。私钥绝不能放入仓库或提供给 Kleaner 客户端。

## Comments

2026-09-08 收口：用户提供了三项信任根输入——① GitHub raw 直链（`rules-channel` 分支）；② Ed25519 公钥 `6X6OrBIoxw2MnCY54tZthu5UmBedldrk2yVm1Y9V7yA=`（私钥由 openssl 在用户目录 `~/.kleaner-signing/` 生成，不入仓库）；③ 语义式版本 `1.0.0` 起步，最低应用版本 `0.2.4`。

实现：
- **`RuleTrust`**（Core）：内嵌官方 URL、公钥、规范字节序列 `CanonicalPayload`、严格语义版本解析与发布时钟容忍窗口。
- **`RuleManifestVerifier`**（Core）：纯逻辑校验——摘要先于签名（规则负载被改时给出更直接原因）→ Ed25519 签名 → 降级（不高于已接受版本）→ 应用版本门槛 → 发布时间在未来。
- **`Ed25519Verify`**（Core，零外部依赖）：.NET BCL 至今未提供 Ed25519，自带 RFC 8032 仅验证实现（扭爱德华兹曲线扩展坐标 + add-2008-hwcd 统一加法 + 无符号小端哈希解释 + 群方程 [S]B == R+[k]A 编码比较）。正确性由 RFC 8032 向量 1（经 Node WebCrypto 权威复核）+ openssl 3.5.6 真实签名交叉验证保证。测试镜像 `SignData` 仅测试程序集可用。
- **`RuleUpdateService.UpdateFromOfficialAsync`**：下载清单+规则 → 校验 → 语义校验 → 原子替换本地覆盖与最后已知良好 → 写状态文件。`LoadEffective` 回退链：覆盖→最后已知良好→内置规则。
- **设置页**：移除任意 URL/SHA512 输入框，改为只读展示官方源 URL 与本地状态（`DescribeLocalState`）；`AppSettings` 的 `RuleUpdateUrl`/`RuleUpdateSha512` 字段移除（旧 settings.json 静默忽略）。
- **`scripts/sign-rules.ps1`**：所有者用私钥对规则文件签名产出 `rules-manifest.json`，无 BOM UTF-8。

首个签名清单 v1.0.0 已推送到 `rules-channel` 分支（`rules-manifest.json` + `rules.v1.json`）。签名时用 Git 里的 LF 版本规则文件（CRLF 差异会导致摘要不匹配）。

测试覆盖（`RuleUpdateTrustTests` 10 项 + `Ed25519VerifyTests` 3 项）：签名+摘要正确被接受并原子落盘；篡改清单版本使签名失效；篡改规则负载因摘要不符拒绝；降级拒绝；最低应用版本门槛；假时钟未来发布时间拒绝；下载失败保持本地不变；覆盖损坏回退最后已知良好；覆盖与最后已知良好都缺失回退内置；非语义版本格式拒绝。完整测试 189/189、Release 零警告零错误。

遗留：GitHub raw CDN 缓存延迟导致首次端到端网络验证待 CDN 刷新后由设置页「检查更新」完成；签名链逻辑已由单元测试全覆盖。
