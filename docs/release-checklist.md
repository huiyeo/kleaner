# 发布检查单（Phase 0 发布门禁）

每个公开版本发布前逐项执行。本检查单汇总工单 16 的矩阵与治理要求；签名信任（工单 12）落地前的校验均为人工哈希核对。v0.1.0 的历史发布操作记录保留在 `docs/publish.md`，后续发布以本文件为准。

## 1. 发布前置（不通过不得打包）

- [ ] `dotnet build Kleaner.slnx -c Release`：0 警告 0 错误
- [ ] `dotnet test Kleaner.slnx -c Release`：全绿；用例数与 README「质量」段一致
- [ ] 版本号确定：版本唯一来源是 `scripts/release.sh <版本号>` 的参数（工程文件不含 Version 属性）；同步更新 README「状态」段与 `docs/release-notes-v<版本>.md`
- [ ] 文档同步核对：README、architecture、context、deletion-path、performance-baseline 与实际代码一致（命令清单、限制声明、测试计数）
- [ ] 待发布行为与 `docs/goals.md` 安全宪法无冲突；Phase 0 发布阻断指标成立（零未授权清理、零未审计文件状态变化、部分失败零数据丢失）

## 2. 打包

- [ ] `scripts/release.sh <版本号>`（自包含单文件发布 → Velopack Setup / Portable / 更新清单）
- [ ] 产物四件齐全：`releases/Kleaner-win-Setup.exe`、`Kleaner-win-Portable.zip`、`RELEASES`、`Kleaner-<版本>-full.nupkg`

## 3. 产物校验（签名体系落地前为人工哈希核对）

- [ ] 对四个产物逐一记录 SHA512 并写入发布说明：`sha512sum releases/*`
- [ ] 与独立通道（另一台机器或干净目录重新打包）比对新产生的 `full.nupkg` 哈希一致性
- [ ] Setup.exe 首次运行预期触发 SmartScreen「未知发布者」提示——发布说明须包含绕行指引（README 许可证段已有口径）

## 4. 安装 / 升级 / 回退 / 卸载矩阵（真机手工执行并记录）

| 路径 | 步骤 | 预期结果 | 已验证 |
|---|---|---|---|
| 全新安装 | 运行 Setup.exe（静默 `--silent`）→ 启动 | 开始菜单入口可用；应用正常扫描；current 267 文件 | ✅ 2026-09-10（0.3.1，本机） |
| 覆盖升级 | 在旧版本（0.2.6）上静默装 0.3.1 | 应用可启动；history/startup-backup 保留可读 | ✅ 2026-09-10（本机，真实 0.2.6→0.3.1） |
| 升级中断 | 安装中途强制结束安装器（800ms kill） | 已装应用仍可启动；history 完好 | ✅ 2026-09-10（近似：同版本重装中断） |
| 回退 | 重装旧版 Setup | 应用可启动；新版写入的数据旧版可读 | ⚠️ 缺 0.2.6 Setup 产物未真机执行；数据向后兼容已论证（旧版 STJ 忽略 `prev` 字段，14 条新记录可读；旧版不校验哈希链） |
| 卸载 | `Update.exe --uninstall` | AppData\Local\Kleaner 完全移除、快捷方式移除；Roaming 用户数据保留 | ✅ 2026-09-10（0.3.1，本机） |

**矩阵执行发现并修复的缺陷（2026-09-10）**：`PublishSingleFile=true` 单文件发布经 Velopack 安装后 current 缺失 WPF 本机依赖（wpfgfx/D3DCompiler/PenImc/vcruntime140），应用启动即死。已将 `release.sh` 改回散文件 + `--self-contained`（0.2.6 已验证形态）并重打包验证。**后续发布严禁启用 PublishSingleFile。**

**已知门禁缺口（发布说明必须如实声明，不得粉饰）：**

- **应用内自动更新未接线**：`Program.Main` 只有 `VelopackApp.Build().Run()` 钩子，无 `UpdateManager` 检查逻辑；`RELEASES`/`full.nupkg` 当前仅是 Velopack 打包产物，不构成可用更新通道。升级路径=手动运行新版 Setup。
- **规则更新签名信任未落地**（工单 12）：设置页的规则更新为「用户输入 URL + SHA512」，不构成发布方身份认证；发布说明不得表述为「官方签名更新」。
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
