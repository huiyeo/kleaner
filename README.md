# Kleaner（工作名）

> 白名单制的 Windows C 盘清理工具：只清理"能说清为什么安全"的东西。

## 定位

开源免费。差异化不在扫描引擎，而在**可审计的规则库**：针对通用工具不覆盖的个人软件残留（Electron 更新器安装包、开发工具缓存、游戏着色器缓存）逐条定义清理规则，每条规则附带安全性说明（目录用途、删除影响、验证方式），用户在界面中可直接查看。

## 安全模型（四道保险）

1. **严格白名单**：只有规则库中的类别才可清理，无黑名单推断。
2. **年龄阈值**：默认仅清理 14 天未修改的文件（缓存类 7 天，更新器按"保留最新 1 份"），规则级可覆盖。
3. **强制预览（dry-run）**：先展示"将删除什么、每类多少"，确认后才执行。
4. **隔离区可还原**：删除即移入自管隔离区（默认非系统盘），manifest 记录原路径，保留 7 天，支持一键还原。

引擎层固定排除：reparse point（OneDrive/云盘占位文件）、被占用文件（跳过并在报告提示）。

## 范围

- **v1**：用户级缓存 + 无争议系统级；高级模式（WSL vhdx 压缩引导、大件跳转系统工具、注册表只扫描不删除）。
- **明确不做（v1）**：自动定期清理、注册表删除、休眠/页面文件等系统大件的直接操作、多用户 profile。

## 状态

**v0.2（当前）**：在 v0.1 基础上参照 [MangoDisk](https://github.com/harry0703/MangoDisk) 的安全方法论升级——

- 规则引擎：`%ENV%` + `*`/`**` 通配扫描、年龄阈值、keepNewest 版本保留、exclude、reparse point 一律排除、被占用文件跳过
- 隔离区：删除即移入（默认剩余空间最大的非系统盘），manifest 记录原路径，整批还原（冲突不覆盖）、7 天保留手动清空
- **操作历史**：删除类操作全部自动落 `history.jsonl` 审计，界面可查
- **CLI 安全契约**：`scan`/`clean --apply`/`--yes`/`large-files`/`duplicates`/`usage`，非交互无 `--yes` 拒绝删除（退出码 2），支持 `--format json`
- **工具箱**：大文件清理、重复文件清理（内容指纹三级预筛，每组保一）、空间分析（列表下钻）——全部只读扫描、清理进隔离区
- 规则库：20 条规则，每条附安全性说明与验证状态标注；真机实测可释放约 2 GB
- 高级模式：WSL vhdx 检测与压缩指引、休眠/还原点/WinSxS 引导（调起系统工具）、注册表卸载残留**只读**扫描
- 发布链路：自包含单文件（免装 .NET）+ Velopack 安装版/便携版/自动更新清单（`scripts/release.sh`）

质量：`dotnet test` 29/29 通过（引擎端到端、隔离区还原/冲突/占用、大文件/重复/空间分析、历史审计、更新校验）。

Roadmap：卸载器、启动项管理、treemap 空间视图。规则贡献见 [CONTRIBUTING.md](CONTRIBUTING.md)（三关流程：权威来源 → 安全边界 → 真机验证）。

## 开发

```
dotnet build Kleaner.slnx -c Release
dotnet test tests/Kleaner.Core.Tests -c Release
dotnet run --project src/Kleaner.App -c Release
```

> .NET 装在非默认位置时（如用户目录安装），框架依赖启动需设置 `DOTNET_ROOT` 指向运行时目录。

- `src/Kleaner.Core`：规则加载/校验/扫描匹配（纯逻辑，无 UI 依赖）
- `src/Kleaner.Executor`：隔离区（manifest/移动/还原）、按需提权
- `src/Kleaner.SpecialOps`：WSL 压缩、大件跳转、注册表只读扫描
- `src/Kleaner.App`：WPF 界面
- `rules/`：规则库（`rules.v1.json` + schema + 安全性说明）

## 贡献规则

规则 PR 需同时提供：路径模式的实测依据（何机器何目录何大小）、`safetyNotes` 与 `rules/docs/safety-notes.md` 对应条目。风险等级 low 起步；任何"可能含用户数据"的目录不予合并。

## 许可证

MIT。首次发布若触发 Windows SmartScreen"未知发布者"提示，属未签名应用的正常现象，发布说明中会给出指引。
