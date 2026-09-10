# 01 CLI 隔离验收准备证据

日期：2026-09-10

本记录只覆盖 CLI 的安全可复现部分，以及一次显式注入 Executor 的夹具恢复探针。它不把 Core/Executor 恢复能力等同于 CLI 完整端到端，也不替代 GUI 验收。

## 环境与构建

工作目录：`D:\Projects\zwork\kleaner`，HEAD：`88c0565`。

README「开发」要求的命令使用已安装运行时的完整路径执行：

```powershell
& 'C:\Users\13739\AppData\Local\Microsoft\dotnet\dotnet.exe' build Kleaner.slnx -c Release
```

退出码 `0`；摘要：所有项目生成成功，0 warnings，0 errors。

```powershell
& 'C:\Users\13739\AppData\Local\Microsoft\dotnet\dotnet.exe' test Kleaner.slnx -c Release
```

退出码 `0`；摘要：`189/189` 通过，0 失败，0 跳过。该结果证明 Release 构建及自动化回归通过，不能单独证明真实 CLI、GUI、运行时文件状态变化或真机全流程均已验收。

## 注入边界与复现方案

CLI 支持三个位置覆盖：`--rules`、`--quarantine-root`、`--history-path`（`tools/Kleaner.ScanCli/Program.cs:36-38, 429`）。夹具根目录使用 workspace 下的 GUID 临时目录；规则中的 `%KLEANER_CLI_FIXTURE%\\**` 由子进程环境变量展开（`src/Kleaner.Core/GlobScanner.cs:9-10`）。clean 的 dry-run/apply 调用显式传入三个覆盖路径；scan 调用按实际命令只传入 `--rules` 与 `--quarantine-root`，没有传 `--history-path`。CLI 入口第 38 行只是构造 `HistoryManager`，不会读写默认 history 路径；scan 的实际文件访问因此限制在显式规则与隔离区夹具，clean 的历史写入落在显式 history 夹具。

夹具包含两个内容分别为 `fixture-a`、`fixture-b` 的 `.log` 文件，修改时间回退 30 天；规则为单条低风险测试规则，计划总计 2 个文件、18 bytes。夹具脚本及目录在取证后已删除；下面记录的是命令形态、退出码和实际输出摘要。

## 实际 CLI 结果

1. `scan --rules <fixture> --quarantine-root <quarantine> --format json`

   退出码 `0`。命中 `cli-e2e-rule` 的 2 个文件、18 bytes；JSON 含 `safetyNotes`；错误列表为空。

2. `clean --rule cli-e2e-rule --rules <fixture> --quarantine-root <quarantine> --history-path <history> --format json`

   退出码 `0`。输出 `dryRun=true`、`files=2`、`bytes=18`。前后文件 SHA-256 不变，dry-run 时 history 不存在，quarantine 文件数为 0。

3. 上述命令追加 `--apply --yes`

   退出码 `0`。输出 `applied=true`、`moved=2`、`bytes=18`、`skipped=0`；夹具源目录文件数为 0，隔离区包含 manifest、2 个隔离文件和 `.operation.lock`。批次 ID 为本次运行生成的 GUID 化时间批次标识。

4. apply 后再次执行第 1 步 `scan`

   退出码 `0`。规则仍被加载，但 `FileCount=0`、`TotalBytes=0`、错误列表为空。

## 恢复边界与结果

CLI 的命令列表只有 `scan`、`clean`、分析命令和 startup 命令，没有 `restore` 子命令（`tools/Kleaner.ScanCli/Program.cs:414-429`）。因此本次恢复不是 CLI 命令，而是对同一个 workspace 夹具调用公开的 `QuarantineManager.RestoreBatch`（`src/Kleaner.Executor/QuarantineManager.cs:217-351`），并显式注入同一 quarantine/history 路径。

恢复结果：`RestoredCount=2`、`Skipped=[]`、`Failed=[]`、`IsComplete=true`。原始与恢复后 SHA-256 完全一致：

```text
b.log  E0EF56D6EB603D9FFE87E74C28B8210B0EA93839E2EA4FB64F1FC2C66E95E391
a.log  06ADA57C26AA5CF429E9F2C0A99E3E4A42DAECD45FC4C955D7C1399AB4227AE8
```

恢复后再次执行 `scan`，退出码 `0`，重新命中 2 个文件、18 bytes。该 scan 只是恢复后的确认，不执行恢复。

本次清理/恢复夹具 history 共 6 行；隔离区收尾后仅剩 `.operation.lock`。另以单文件 GUID 夹具做了审计读回：CLI apply 退出码 `0`，`HistoryLines=2`，manifest 存在，`HistoryManager.RecentVerified()` 返回 `Broken=false`、`HeadMismatch=false`、`IsValid=true`、`UnchainedCount=1`。这证明该独立样本的历史链检查通过；没有把它扩展为对完整 CLI 清理/恢复 6 行历史的独立逐行链读回结论。历史与隔离区的实现入口分别见 `src/Kleaner.Executor/HistoryManager.cs:22-28, 43-93` 与 `src/Kleaner.Executor/QuarantineManager.cs:97-184`。

## GUI 观察与未完成项

同会话 GUI 走查中，Release 应用启动后自动读取真实规则并开始只读扫描，随后取消，状态回读为“已取消”。点击“用户临时目录”规则的说明单元，能读到完整 `safetyNotes`：`Windows 与各应用写入的用户级临时目录，删除后按需自动重建。仅清理 14 天未修改的文件，跳过被占用文件（如安装未完成的临时文件）。` 本次没有点击清理、保存设置、规则更新或隔离区操作，窗口随后关闭。

这次 GUI 观察证明单条 safetyNotes 入口和取消状态可见，不证明每条规则解释、GUI 隔离/还原/历史/更新主链。GUI 启动确实读取了真实规则并扫描了真实规则目标；本次没有发起真实数据的文件状态写入。

工单因此保持 `blocked`：CLI 的只读、dry-run、apply、隔离和夹具恢复已取得证据，但 CLI 没有 restore 命令；GUI 又没有可注入 settings/history/rules 的完整隔离入口。最小恢复条件是提供经设计的 GUI 依赖注入/测试账户或隔离 VM，再执行 GUI 的隔离、还原、历史和官方规则更新链。工单原文“还原（通过 GUI 或二次扫描）”应改为“通过 GUI 或显式恢复入口还原，随后再次 scan 确认”。
