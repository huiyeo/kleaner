# 06 功能对等范围与迁移策略

Type: grilling
Status: resolved

## Question

迁移的边界与节奏（HITL）：

1. **对等范围**：现 7 窗口（Main/Toolbox/Advanced/Quarantine/History/Settings/Startup）哪些必须进 web v1 才允许删 WPF？哪些可以后补（如 Advanced 高级模式）？
2. **迁移策略**：WPF 保留到 web 达到对等再删（过渡期双栈并存、发布脚本双出口），还是一次性切换？CLI 不动的边界是否也适用于 SpecialOps-only 功能（CLI 本就没有高级模式）？
3. **回退方案**：web 版发现问题时的退路。
4. **29 个测试与四道保险**：迁移完成的验收标准是否包含"删除路径行为与 WPF 版逐项一致"的对照清单（对照 `docs/deletion-path.md`）。

## Answer

**结论：6/7 窗口进 v1、双栈过渡 + 1 版观察期后删 WPF、回退靠 Velopack 降级、删除路径逐项对照清单验收、Advanced 落地 web 是删 WPF 的前置条件。（2026-08-31 与用户 grilling 确认，六问全按推荐采纳）**

1. **对等范围 = 6/7 窗口进 web v1**：Main / Toolbox / Quarantine / History / Settings / Startup 全部进 v1，缺任何一个（尤其删除路径相关的 Quarantine、Toolbox 清理、Startup）都谈不上对等。**Advanced 唯一后补**——只指引不删、与删除路径无关、无安全风险，最适合延后。细节放宽：Toolbox 的 treemap 可视化 v1 可降级为纯列表（Squarify 在 vanilla Web Components 里是额外工作量），v1.x 补回。

2. **迁移策略 = 双栈过渡 + 1 版观察期**：工单 04 已定过渡期双入口并存（WebHost 为默认 StartupObject）。对等达成并通过验收 → **再带 WPF 发布 1 个观察版本**，无回归反馈 → 下一个版本删除 Kleaner.App 工程，发布脚本只留 WebHost 出口。一次发布即删太激进，长期双栈已在 map.md 排除。

3. **回退方案 = Velopack 降级，不为回退付包体积代价**：观察期版本只发 WebHost（不做双 exe 出口——删除路径行为靠第 4 条对照清单在发布前兜住，为低概率回退路径让全部用户包体积翻倍不值）。用户侧真实回退手段 = 装回上一个 Velopack 版本（旧版全量包仍是 WPF）；开发者侧 WPF 工程保留在源码中可调试。

4. **CLI 边界 = 命令面与安全契约冻结，SpecialOps 只进 Web**：ScanCli 不新增命令（不加 `wsl`/`bigitems` 等），退出码契约与 `--apply` 闸门不动。Advanced 能力只通过 WebHost 暴露，且维持 SpecialOps"只扫描与指引、不直接改动系统项"原则——web 版也不给它加删除路径。

5. **验收标准 = 删除路径逐项对照清单（工单产出物之一）**：以 `docs/deletion-path.md` 为基准，列出全部删除路径行为（隔离区 manifest 结构、整批还原同名不覆盖、删除批次警告确认、清空 7 天前批次、HistoryManager 落盘、reparse point 排除、占用文件跳过并在报告中提示……），web 版逐项打勾验收；29 个引擎测试全绿是底线而非充分条件。

6. **Advanced 的截止时间 = 删 WPF 的前置条件**：观察期结束、删除 Kleaner.App 工程之前，Advanced 三个引导 Tab（WSL vhdx 指引、系统大件引导、注册表残留只读扫描）必须已在 web 版落地。节奏：v1 发布 → 观察期内完成 Advanced → 删 WPF。不许出现"删了 WPF 而 Advanced 两边都没有"的功能空窗——否则"功能对等才删 WPF"的前提被默默放弃。

## Comments

- 2026-08-31 grilling 两轮收口。第一轮四问（对等范围/迁移策略/CLI 边界/验收标准），第二轮两问（回退方案/Advanced 截止时间，由观察期决策解锁）。事实依据来自七窗口功能盘点：Advanced 是 SpecialOps 独占且只指引不删；全仓永久删除出口仅 QuarantineWindow 两处；Settings 仅 3 项；Startup 是系统启动项管理而非应用自启。
- 实施工单切分（map.md 中的悬置项）自此解锁，作为下一张前沿工单。
