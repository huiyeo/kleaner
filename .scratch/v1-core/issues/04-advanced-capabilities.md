# 04: 高级能力 v1 转正/排除决定

**What to build:** goals.md Phase 1 第 4 条：启动项、WSL、注册表残留等高级能力不因已经存在就自动成为 v1 可靠性承诺，须独立验收后才能转正。本票对三项能力逐一验收并决定「转正为 v1 承诺」或「显式排除出 v1 声明」。

**Blocked by:** None。

**Status:** complete

- [x] **启动项**（StartupManager）：枚举/禁用/还原往返已有 CLI `startup-test` 自检（写真实注册表与启动文件夹）——审查该自检的覆盖面，真机走查 HKCU 路径与回滚路径，决定转正或排除（含「需提权的 HKLM 路径」边界声明）
- [x] **WSL vhdx**（WslInspector）：检测+压缩指引是引导型能力，验证检测准确性与指引文案正确性（不实际压缩），按「只引导」定位决定是否纳入 v1 声明
- [x] **注册表残留**（RegistryInspector）：只读扫描，验证「只读」承诺（无任何写路径），决定纳入 v1 声明或标注实验性
- [x] 三项决定写入 `docs/goals.md` Phase 1 勾选与 README「范围」段：转正者列出验收记录锚点，排除者写明排除原因
- [x] 任何能力转正前若发现行为缺陷：当场修复（附回归）或标记排除，不得带缺陷转正

**边界：** 本票不新增功能、不扩大任何能力的写路径。

## Comments

验收记录（2026-09-10）——三项全部验收通过并转正，无行为缺陷：

1. **注册表残留 → 转正（v1 只读扫描）**：代码级审查成立——`RegistryInspector` 全部 `OpenSubKey` 调用均为 `writable: false` 或只读接口（`IReadOnlyRegistryKey`），无任何 SetValue/DeleteValue/CreateSubKey 调用；已有 `SpecialOpsTests` 单测覆盖扫描逻辑（3 个默认根）。定位为 v1 只读扫描能力。
2. **WSL 虚拟磁盘 → 转正（v1 只读检测 + 引导）**：代码级审查成立——仅 `Directory.GetFileSystemEntries`/`File.GetAttributes` 只读枚举（含 reparse point 排除）；`wsl --shutdown` 等命令仅作为给用户复制的压缩指引文本，Kleaner 自身从不执行。定位为引导型能力（压缩由用户在系统工具执行）。
3. **启动项 → 转正（v1 管理，写路径有边界声明）**：真机往返自检 `startup-test` PASS（exit 0）——写真实 HKCU Run 值与启动文件夹文件 → 禁用 → 还原 → 数据核对一致 → 测试数据自清理。备份机制：禁用前自动备份到 `%APPDATA%\Kleaner\startup-backup`。边界声明：HKLM 启动项需 UAC 提权路径，自动化验收仅覆盖 HKCU + 文件路径；提权路径为产品功能但验收依赖人工提权操作，在发布说明中如实标注。

三项决定已同步 `docs/goals.md` Phase 1 勾选与 README「范围」段。
