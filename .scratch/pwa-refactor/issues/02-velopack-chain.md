# 02 Velopack 发布链路适配

Type: research
Status: resolved

## Question

现有发布链路：`scripts/release.sh` 用 Velopack 出安装版/便携版/自动更新清单，目标是 WPF `WinExe`。换成「本地 ASP.NET Core 服务 + 浏览器入口」后：

1. Velopack 对非 WPF 应用（无 UI 框架的 WinExe）是否兼容？
2. 安装快捷方式如何指向"启动服务并打开浏览器"的入口？
3. 应用更新流程会不会打断正在运行的服务？
4. 需要哪些官方能力支撑（开机自启、托盘等）？

产出：发布链路的推荐配置，供迁移策略工单引用。

## Answer

**结论：Velopack 零障碍兼容，发布链路几乎不用改，改的是应用自身的启动入口。**

1. **兼容性**：Velopack 的宿主要求只是"一个 exe + `VelopackApp.Build().Run()` 在 `Main` 最先执行"，与 UI 框架无关。WPF、WinForms、控制台、无窗口 WinExe（`OutputType=WinExe` 但不引 WPF——不弹控制台黑窗）都支持。新 WebHost 工程用 `net10.0-windows` + `OutputType=WinExe`（无 `UseWPF`）即可：无控制台窗口，Velopack 钩子照常。

2. **快捷方式**：Velopack 安装器创建的开始菜单/桌面快捷方式直接指向应用 exe。入口行为由程序自己实现：`Main` → `VelopackApp.Build().Run()` → 检测端口/单实例 → 起Kestrel → `Process.Start` 打开默认浏览器指向 `http://127.0.0.1:<port>`。**不需要** Velopack 提供特殊支持。注意：若后续决策要"开机自启/托盘常驻"，Velopack 官方有 `VelopackApp` 生命周期钩子（FirstRun/Restarted）可挂钩子，但托盘图标需自引 `H.NotifyIcon` 之类——归「进程模型与 API 安全模型」工单决策。

3. **更新不打断语义**：Velopack 更新 = 下载新版本到 side-by-side 目录，`UpdateManager.ApplyUpdatesAndRestart()` 在**应用自己调用时**才退出重启。对服务形态意味着：更新确认动作应从 Web UI 发起（API 端点 → 服务端执行 apply+restart → 重启后浏览器重新连接）；更新期间正在跑的扫描/清理要先完成或取消，避免隔离区批次中断（继承四道保险：批次完整性优先）。

4. **便携版**：`vpk pack` 的便携包同样只是带 exe 的目录，wwwroot 随包，无额外工作。

风险提示：`release.sh` 需要把新工程的发布产物（exe + wwwroot）喂给 `vpk pack`；`--shortcuts`/入口参数与现在一致，脚本改动预计仅限产物路径。
