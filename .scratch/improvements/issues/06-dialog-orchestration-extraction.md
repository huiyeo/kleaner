# 06: 对话框编排逻辑下沉（code-behind → 可注入服务/VM）

**What to build:** `Kleaner.App` 仅 MainWindow 有 ViewModel，6 个对话框窗口的编排逻辑（选中过滤、提权确认前置、失败回滚提示、刷新）承载在 code-behind，只能手测。分步下沉为可注入服务层或 ViewModel，从启动项窗口起步（已有 `StartupManagerTests` 底座），其余窗口按同模式开后续工单跟进。

**Blocked by:** 建议 05 之后开始（复用其注入模式与经验，非硬依赖）

**Status:** complete

- [x] 启动项窗口：禁用/还原编排抽出后，UI 事件层只剩转发，无业务分支
- [x] 编排逻辑有单测：选中含 HKLM 项时先触发提权确认、删值失败回滚备份记录、文件型往返一致
- [x] 新增文案继续走 `S.Get/S.Format` 通道（docs/conventions.md §3）
- [x] 行为不回归：注入假管理器走查禁用→还原往返、刷新指令与状态结果正常
- [x] 现有测试保持绿色

**完成说明：** StartupWindowCoordinator 接管选中筛选、HKLM 确认、批量执行与状态汇总；窗口事件只转发和应用刷新结果。IStartupManager 与 IStartupWindowDialog 使 3 项协调器测试在假实现上完成，既覆盖已有 StartupManagerTests 的删除失败备份回滚、文件往返，又不触碰真实注册表、启动目录或 UAC。
