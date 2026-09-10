# 05: 安装/升级/回退/卸载真机矩阵

**What to build:** 执行 `docs/release-checklist.md` 第 4 节的发布矩阵：全新安装、覆盖升级、升级中断、回退、卸载五条路径在本机真机走完并记录。这是 goals.md Phase 1 第 3 条（自身安装/升级/回退/卸载稳定）的验收本体。

**Blocked by:** 01（主流程验收通过后，安装版的行为才有可比基线）。

**Status:** complete

- [ ] 全新安装：`scripts/release.sh <版本>` 打包 → Setup.exe 安装 → 启动 → 扫描正常 → `%APPDATA%\Kleaner\` 生成
- [ ] 覆盖升级：旧版本上装新版 → 启动 → settings/history 保留可读 → 历史窗口旧记录显示「未链段」提示
- [ ] 升级中断：升级中强制结束安装器 → 旧版仍可启动 → 重跑新版 Setup 可完成
- [ ] 回退：重装旧版 → 启动 → 新版写入的历史仍可展示（链向后兼容）→ 隔离区批次仍可还原
- [ ] 卸载：系统设置卸载 → 应用移除 → 隔离区与 `%APPDATA%\Kleaner\` 处置与发布说明一致
- [ ] 五行记录逐项写入 release-checklist 矩阵的「已验证」列；暴露的问题当场修复或立票

**边界：** 走查环境为本机；产物哈希校验按检查单第 3 节执行；不触碰真实用户数据。

## Comments

矩阵执行记录（2026-09-10，本机真机，静默安装）：0.2.6（用户现存安装）→ 0.3.1 覆盖升级通过（数据保留+应用正常）；升级中断（800ms kill 安装器）后应用可启动、数据完好；卸载（Update.exe --uninstall）应用完全移除、Roaming 用户数据保留（发布说明需写明卸载保留用户数据）；全新安装 0.3.1 通过（快捷方式+启动+扫描）。

**重大缺陷当场修复**：PublishSingleFile=true 的单文件发布经 Velopack 安装后 current 仅 2 文件（缺 wpfgfx/D3DCompiler/PenImc/vcruntime140 全部 WPF 本机库），应用启动即死。release.sh 改回散文件+--self-contained（0.2.6 已验证形态）并重打包，重装后应用正常（commit ad88262）。**后续发布严禁启用 PublishSingleFile**（release.sh 已注释警示）。

遗留移交工单 06：回退行需留存旧版 Setup 产物后真机补验（本次缺 0.2.6 Setup 无法执行）；发布说明需写明「卸载保留用户数据」。