# 04: CLI 安全契约自动化测试

**What to build:** CLI 与 GUI 是平级入口，其安全契约（只读预览、显式确认、拒绝删除的退出码）目前零自动化测试，全靠手测。把 `tools/Kleaner.ScanCli/Program.cs`（单文件 327 行）的命令解析与清理计划构建抽出为可测单元（或以 xunit 进程级跑构建产物做集成测试），把契约锁进测试。

**Blocked by:** None

**Status:** done（2026-09-04，进程级集成测试路径落地）

**实施记录**：选择进程级路径（保真度高于抽单元），为此 CLI 新增三个通用位置覆盖参数 `--rules`（显式指定时绕过更新通道覆盖，直接加载）、`--quarantine-root`、`--history-path`；测试工程引用 ScanCli，以 `dotnet exec` 跑真实入口，stdin 重定向构造非交互环境。新增 `CliSafetyContractTests` 5 例。

- [x] `scan` 全程只读：对注入的临时目录扫描后无任何文件被移动/删除，也不写历史
- [x] `clean` 无 `--apply`：只打印计划，退出码 0，不执行删除
- [x] `clean --apply` 非交互且无 `--yes`：拒绝执行删除，退出码 2
- [x] `--yes` 时跳过交互确认直接执行（注入的隔离区/历史承接真实删除路径）
- [x] `--format json` 输出可反序列化、关键字段（dryRun/files/applied/moved/errors）稳定存在
- [x] 测试全程使用注入的临时目录，不触碰真实用户目录与真实隔离区
- [x] 现有测试保持绿色（全套 65/65 通过）
