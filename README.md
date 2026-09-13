# Kleaner（工作名）

> 白名单制的 Windows C 盘清理工具：只清理"能说清为什么安全"的东西。

## 定位

开源免费。差异化不在扫描引擎，而在**可审计的规则库**：针对通用工具不覆盖的个人软件残留（Electron 更新器安装包、开发工具缓存、游戏着色器缓存）逐条定义清理规则，每条规则附带安全性说明（目录用途、删除影响、验证方式），用户在界面中可直接查看。

## 安全模型（四道保险）

1. **严格白名单**：只有规则库中的类别才可清理，无黑名单推断。
2. **年龄阈值**：默认仅清理 14 天未修改的文件（浏览器缓存类 7 天，更新器按"保留最新 1 份"），规则级可覆盖。
3. **强制预览（dry-run）**：先展示"将移入隔离区什么、每类多少"，确认后才执行；执行器只接受 Core 根据本次扫描构造的清理计划，并在移动前再次复验。
4. **隔离区可还原**：清理前先原子创建可恢复 manifest 并确认审计可写，文件才会移入自管隔离区（默认非系统盘）；逐项状态可恢复，任一还原失败会保留批次。

引擎层固定排除：reparse point（OneDrive/云盘占位文件）、被占用文件（跳过并在报告提示）。

## 范围

- **v1**：用户级缓存 + 无争议系统级；高级模式（WSL vhdx 压缩引导、休眠/还原点/WinSxS 引导调起系统工具、注册表只扫描不删除）。
- **明确不做**：自动定期清理、注册表删除、休眠/页面文件等系统大件的直接操作、多用户 profile、云端 LLM。

## 状态

**v1.1.0（当前应用版本，已发布** [GitHub Release](https://github.com/huiyeo/kleaner/releases/tag/v1.1.0)**；规则渠道 v1.2.0 已独立发布并线上核验）**：四道保险完整主链（扫描→解释→预览→确认→隔离→还原→审计）经 GUI+CLI 端到端走查；安装/升级/回退/卸载矩阵真机 5/5（含 v1.0.0↔0.3.1 双向回退补验；v1.1.0 已重验覆盖升级行，全矩阵于下次发版窗口整体重验）；UI 无障碍（DPI 100/125/150、高对比度、Narrator 基础）实测通过；发布链路为散文件自包含 + Velopack 四件套（应用内自动更新未接线，升级路径=手动安装，见 `docs/publish.md`）。v1.1.0 相对 v1.0.0：真实用户目录全规则扫描 97.6s → 2.5s（单趟枚举，消逐条目补 stat）；修复清理计划构建在 UI 线程同步重扫导致的界面冻结（AppHang）与隔离区窗口同步操作；规则治理补维护责任/证据更新时间机制（rule-governance 06）。此后增量（规则渠道独立发布，随 `rules-channel` 更新到达已装应用）：新增系统内存转储与传递优化缓存两条 system 规则（真机演练转正 4 条，验证覆盖率 27.2%）；`rule-census` 真机普查工具；高级模式新增「Windows.old」只读检测与官方路径引导；AI 解释支持手动取消；行激活与 AI 输出播报两项无障碍修复。

- 规则库：103 条规则（temp/browser-cache/dev-cache/updater/system/application 六类别），每条附安全性说明、验证状态与维护责任标注（`maintainer`/`lastEvidenceCheck`，证据超龄 >180 天触发治理警告）；只有明确「本机实测」的规则默认勾选
- 规则治理：证据覆盖率 100%、验证覆盖率 27.2%（28/103，阶段目标 25% 已达成）、误伤信号（还原批次计事件）——`governance-report` 只读测量，口径见 [docs/rule-governance.md](docs/rule-governance.md)；`rule-census` 输出逐规则真机存在性×命中证据档案；支持规则撤回（`deprecated` 三层防线：策略否决/扫描跳过/呈现标注）
- 规则更新：官方 Ed25519 签名清单（`rules-channel` 分支，v1.2.0 在线核验通过），摘要→签名→降级→应用版本→发布时钟全链校验，原子替换 + last-good 回退；不接受用户任意 URL
- **AI 解释（预览，默认关闭）**：仅连接本机回环 OpenAI 兼容服务（如 Ollama），只发送分类/文件数/字节脱敏聚合，输出仅展示、永不进清理链路；六类故障全部降级为无 AI 模式（ADR 0004 威胁模型与故障矩阵）；挂起期可手动取消
- 高级模式（只扫描与引导，不直接改动系统项）：WSL vhdx 压缩引导、系统大件引导（休眠/还原点/WinSxS）、**Windows.old 只读检测与官方路径引导**（有界占用测量，Kleaner 不提供删除入口）、注册表只扫描不删除
- 工具箱（只读）：大文件、重复文件（内容指纹三级预筛，每组保一）、空间分析（列表+矩形图下钻）
- 启动项管理：启用/禁用/还原，HKLM 走 `reg.exe` 提权、失败回滚

质量：`dotnet test Kleaner.slnx -c Release` 当前 **243/243** 通过，覆盖规则校验（含撤回/维护字段与非法日期 fail-closed）、扫描/年龄阈值/keepNewest 语义、单趟枚举行为（隐藏文件参与匹配、reparse point 不深入）、重复文件选择策略、清理计划入口与执行前复验、部分清空后的清单保留、路径校验、持久化故障、历史哈希链、规则更新签名信任链（Ed25519 RFC 8032 验证）、AI 适配器全部故障矩阵（假 HTTP，含手动取消语义）、数据集作用域基准规则夹具、Windows.old 检测（reparse 排除/有界测量）、视图模型勾选-选中联动，以及清理/还原/清空汇总审计在独立进程中断后的补记。中断遗留临时清单会保留并报告 partial；移动路径的检查-移动间隙由句柄锚定关闭（ADR 0003）；凭据来源认证在同权限攻击者模型下不可达成，按 ADR 0002 明确为已知边界。性能口径见 `docs/performance-baseline.md`（SLO 已按合成数据集作用域重新冻结；真机口径作背景数据）。

当前路线与阶段结论见 [docs/goals.md](docs/goals.md)（Phase 0–4 收口；Phase 5 规则库真机可信覆盖已完成、决策包已全部裁决执行；Phase 6 可信维护循环持续达标中——队列内无立即待办，剩余项均为时间/用户门控）。规则贡献见 [CONTRIBUTING.md](CONTRIBUTING.md)（三关流程：权威来源 → 安全边界 → 真机验证）。

## 开发

```
dotnet build Kleaner.slnx -c Release
dotnet test Kleaner.slnx -c Release
dotnet run --project src/Kleaner.App -c Release
dotnet run --project tools/Kleaner.ScanCli -c Release -- scan
```

> .NET 装在非默认位置时（如用户目录安装），框架依赖启动需设置 `DOTNET_ROOT` 指向运行时目录。

- `src/Kleaner.Core`：规则加载/校验/扫描匹配（纯逻辑，无 UI 依赖）
- `src/Kleaner.Analysis`：只读空间分析（大文件、重复文件、磁盘占用、treemap 布局）
- `src/Kleaner.Executor`：隔离区（manifest/移动/还原）、操作历史、启动项写入、按需提权
- `src/Kleaner.SpecialOps`：WSL 压缩、大件跳转、注册表只读扫描
- `src/Kleaner.App`：WPF 界面（唯一 GUI 入口；Material Design + MVVM）
- `tools/Kleaner.ScanCli`：独立 CLI，自有命令面与安全契约
- `rules/`：规则库（`rules.v1.json` + schema + 安全性说明）

## 贡献规则

规则 PR 需同时提供：路径模式的实测依据（何机器何目录何大小）、`safetyNotes` 与 `rules/docs/safety-notes.md` 对应条目。风险等级 low 起步；任何"可能含用户数据"的目录不予合并。

## 许可证

MIT。首次发布若触发 Windows SmartScreen"未知发布者"提示，属未签名应用的正常现象，发布说明中会给出指引。
