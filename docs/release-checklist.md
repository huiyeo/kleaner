# 发布检查单（Phase 0 发布门禁）

每个公开版本发布前逐项执行。本检查单汇总工单 16 的矩阵与治理要求，并覆盖**规则渠道（rules-channel）的独立发布流程**（与应用版本发布解耦）。v0.1.0 的历史发布操作记录保留在 `docs/publish.md`，后续发布以本文件为准。

## 1. 发布前置（不通过不得打包）

- [ ] `dotnet build Kleaner.slnx -c Release`：0 警告 0 错误
- [ ] `dotnet test Kleaner.slnx -c Release`：全绿；用例数与 README「质量」段一致
- [ ] 版本号确定：Velopack 包版本唯一来源是 `scripts/release.sh <版本号>` 的参数；`src/Kleaner.App/Kleaner.App.csproj` 的 `AssemblyVersion`/`InformationalVersion` 需同步手工改为同一版本（v1.1.0 打包时曾遗漏，安装后文件属性版本错为旧版）；同步更新 README「状态」段与 `docs/release-notes-v<版本>.md`
- [ ] 文档同步核对：README、architecture、context、deletion-path、performance-baseline 与实际代码一致（命令清单、限制声明、测试计数）
- [ ] 待发布行为与 `docs/goals.md` 安全宪法无冲突；Phase 0 发布阻断指标成立（零未授权清理、零未审计文件状态变化、部分失败零数据丢失）

## 2. 打包

- [ ] `scripts/release.sh <版本号>`（自包含散文件发布 → Velopack Setup / Portable / 更新清单）
- [ ] 产物四件齐全：`releases/Kleaner-win-Setup.exe`、`Kleaner-win-Portable.zip`、`RELEASES`、`Kleaner-<版本>-full.nupkg`

**CD（2026-09-13 起可用）**：推送 `v*` 标签（或在 Actions 页手动触发并填版本号）即自动完成——csproj 版本同步（自动改 AssemblyVersion/InformationalVersion，消除 v1.1.0 的手工同步坑）→ 发布门禁完整测试 → `release.sh` 打包 → SHA512 清单 → 创建 GitHub Release 并上传五件（四件产物 + SHA512.txt）。版本号带 `-` 后缀（如 `1.1.1-rc.1`）自动标记预发布。见 `.github/workflows/cd.yml`。注意：CD 在干净 runner 上构建，只产出 full 包（无 delta——应用内自动更新未接线，无影响）；规则渠道发布不走 CD（私钥只在所有者机器，见 3b 节）。

## 3. 产物校验（应用产物为人工哈希核对）

- [ ] 对四个产物逐一记录 SHA512 并写入发布说明：`sha512sum releases/*`
- [ ] 与独立通道（另一台机器或干净目录重新打包）比对新产生的 `full.nupkg` 哈希一致性
- [ ] Setup.exe 首次运行预期触发 SmartScreen「未知发布者」提示——发布说明须包含绕行指引（README 许可证段已有口径）

## 3b. 规则清单发布（rules-channel，独立于应用版本）

- [ ] 规则变更已在 main 提交并推送；完整测试全绿；`governance-report` 覆盖率指标不回退（验证覆盖率摊薄需在发布说明/工单中说明）
- [ ] `powershell -File scripts/sign-rules.ps1 -Version <semver> -MinAppVersion 0.2.4`——脚本内置 **LF 归一化**（2026-09-12 v1.2.0 首发曾因工作区 CRLF 摘要与线上 LF blob 不符被线上核验拦下重签，勿绕过脚本）
- [ ] 产物两件：`releases/rules-manifest.json` + `releases/rules.v1.json`；核对 manifest 的 `rulesSha512` 与副本文件一致
- [ ] 切 `rules-channel` 分支：拷贝两件到分支根、提交、快进推送（**推送属对外发布，需所有者确认**）
- [ ] **线上核验（绕 CDN）**：`gh api repos/huiyeo/kleaner/contents/<file>?ref=rules-channel` 取回字节 → SHA512 与 manifest 一致 → 以内嵌公钥独立验签（Ed25519）→ 规则条数与变更内容抽查
- [ ] 私钥只在所有者机器 `~/.kleaner-signing/`（丢失须换钥并发新应用版本，见 `rules-signing-workflow` 记忆与工单 12）

**已发布记录**：v1.0.0（2026-09-10，101 条）、v1.1.0（2026-09-12，103 条含 K1/K2）、v1.2.0（2026-09-12，4 条演练转正 + 全库证据复核回填）。

## 4. 安装 / 升级 / 回退 / 卸载矩阵（真机手工执行并记录）

| 路径 | 步骤 | 预期结果 | 已验证 |
|---|---|---|---|
| 全新安装 | 运行 Setup.exe（静默 `--silent`）→ 启动 | 开始菜单入口可用；应用正常扫描；current 267 文件 | ✅ 2026-09-10（0.3.1，本机） |
| 覆盖升级 | 在旧版本（0.2.6）上静默装 0.3.1 | 应用可启动；history/startup-backup 保留可读 | ✅ 2026-09-10（本机，真实 0.2.6→0.3.1） |
| 升级中断 | 安装中途强制结束安装器（800ms kill） | 已装应用仍可启动；history 完好 | ✅ 2026-09-10（近似：同版本重装中断） |
| 回退 | 重装旧版 Setup（0.3.1） | 应用可启动；1.0.0 写入的数据（哈希链 head/rules/历史）完整保留且可读 | ✅ 2026-09-10（v1.0.0↔0.3.1 双向真机验证） |
| 卸载 | `Update.exe --uninstall` | AppData\Local\Kleaner 完全移除、快捷方式移除；Roaming 用户数据保留 | ✅ 2026-09-10（0.3.1，本机） |

**矩阵执行发现并修复的缺陷（2026-09-10）**：`PublishSingleFile=true` 单文件发布经 Velopack 安装后 current 缺失 WPF 本机依赖（wpfgfx/D3DCompiler/PenImc/vcruntime140），应用启动即死。已将 `release.sh` 改回散文件 + `--self-contained`（0.2.6 已验证形态）并重打包验证。**后续发布严禁启用 PublishSingleFile。** 五行矩阵于 v1.0.0 发布时全部真机验证通过（回退行以 v1.0.0↔0.3.1 双向覆盖）；v1.1.0 重验「覆盖升级」行（2026-09-11，本机 1.0.0→1.1.0 静默升级，审计历史与哈希链头升级前后逐字节一致）；**全矩阵五行于下次发版窗口整体重验**（所有者裁决 D7b，2026-09-12）。

**已知门禁缺口（发布说明必须如实声明，不得粉饰）：**

- **应用内自动更新未接线**：`Program.Main` 只有 `VelopackApp.Build().Run()` 钩子，无 `UpdateManager` 检查逻辑；`RELEASES`/`full.nupkg` 当前仅是 Velopack 打包产物，不构成可用更新通道。升级路径=手动运行新版 Setup。
- ~~规则更新签名信任未落地~~ **已落地（工单 12，2026-09-10）**：设置页走官方 Ed25519 签名清单（回环获取、全链校验、抗降级 + last-good 回退）；规则渠道 v1.2.0 已发布并线上核验（2026-09-12）。注意 raw.githubusercontent CDN 缓存可能延迟分钟到小时级，核验用 `gh api` 绕行。
- 应用未签名（无代码签名证书），SmartScreen 提示属预期。

## 5. 依赖治理

| 包 | 版本 | 用途 | 升级策略 |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM 源生成器（ObservableProperty/RelayCommand） | 手动逐包升级；升级前全量测试 |
| MaterialDesignThemes | 5.3.2 | UI 主题（ADR 0001；5.0 配置与 4.x 教程不通用） | 大版本升级需重走 ADR 0001 的窗口回归 |
| Velopack | 1.2.0 | 安装器/便携包/更新运行时 | 升级需重测发布矩阵四行 |
| xunit / runner / Test.Sdk / coverlet | 2.9.3 / 3.1.4 / 17.14.1 / 6.0.4 | 仅测试工程 | 随需要升级 |

现状：无集中包管理（无 `Directory.Packages.props`）、无 SDK 版本锁定（无 `global.json`）——`docs/architecture.md` 已记录。工程引入新依赖时同步更新本表。

## 6. 发布后

- [ ] GitHub Release 附件与四件产物一致，SHA512 与发布说明一致
- [ ] `docs/goals.md` 进度记录追加一行（版本、测试数、遗留）
- [ ] 归档：`releases/` 不入库；产物以 Release 附件为准
