# 架构与模块边界

Kleaner 有 6 个产品工程：`Core`（规则与扫描）、`Analysis`（只读空间分析）、`Executor`（隔离区、历史、启动项副作用）、`SpecialOps`（系统只读指引）、`WebHost`（本地 ASP.NET Core + PWA）和 `ScanCli`（独立 CLI）。测试工程为 `Kleaner.Core.Tests` 与 `Kleaner.WebHost.Tests`。

依赖单向无环：Core 与 Analysis 不依赖其他工程；Executor 只引用 Core；SpecialOps 零依赖；WebHost 引用 Core、Executor、Analysis、SpecialOps；CLI 引用 Core、Executor、Analysis。WebHost 是唯一 GUI 入口。

## 入口与界面

- GUI：`src/Kleaner.WebHost/Program.cs`，启动 Velopack 钩子、本地回环 Web 服务与默认浏览器中的可安装 PWA。
- CLI：`tools/Kleaner.ScanCli/Program.cs`，保持独立的命令面与安全契约。
- WebHost 以 `WebHostAppFactory.Build` 组装安全中间件和 API；生产与 TestHost 测试共用该管线。

PWA 资源位于 `src/Kleaner.WebHost/wwwroot`，使用原生 Web Components、manifest 与 service worker。服务只绑定 `127.0.0.1`，API 受 Host、Origin、启动 token 与计划确认凭据保护。

## 分层约束

1. 规则语义、路径匹配、候选选择归 `Core`。
2. 只读空间分析归 `Analysis`；只读系统发现和操作指引归 `SpecialOps`。
3. 文件移动、历史与启动项写入只归 `Executor`。
4. API、PWA 交互、提权交接归 `WebHost`。
5. 批量无界面操作归 `ScanCli`。

删除文件只能通过 `QuarantineManager.Execute` 移入隔离区，并同时写入 `HistoryManager`；详细安全契约见 `deletion-path.md`。

## 资源与测试

WebHost 随发布包复制 `rules/rules.v1.json` 与散文件 `wwwroot`；发布形态为自包含单文件 exe 加静态资源。`Kleaner.Core.Tests` 覆盖引擎安全语义，`Kleaner.WebHost.Tests` 覆盖 API、安全中间件和 PWA 静态契约。
