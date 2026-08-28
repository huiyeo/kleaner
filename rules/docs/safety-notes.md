# 规则安全性说明（v1）

> 摘要：逐条说明白名单规则"为什么安全"：目录用途、删除影响、验证方式与数据来源。新增规则必须同时补充本文件对应条目，PR 审核以此为准。

## user-temp

- **目录用途**：`%LOCALAPPDATA%\Temp`，Windows 与各应用运行期写入的临时文件（解压中间产物、日志转储、安装中间文件）。
- **删除影响**：目录按需自动重建；正在使用的文件被锁定、自动跳过；14 天阈值避开"安装到一半"等在途操作。
- **验证方式**：Windows 官方"磁盘清理/存储感知"同样清理该目录（存储感知默认档为 14 天未使用）。

## chrome-http-cache

- **目录用途**：Chrome 的 HTTP 缓存（Cache）与编译 JS 字节码缓存（Code Cache），均为可再生的网络资源缓存。
- **删除影响**：仅网页首次加载变慢，自动重建。Cookie/登录态/历史/扩展存于同级其他目录，不在本规则路径内。
- **验证方式**：Chrome 内置"清除浏览数据→缓存的图片和文件"清理的即同一目录；本机实测（2026-08-24）两项合计约 698 MB。

## edge-http-cache

- 同 chrome-http-cache，目录为 Edge 对应位置。本机实测占用接近 0，规则保留用于其他环境。

## npm-cache

- **目录用途**：npm 下载缓存（cacache 内容寻址存储）。
- **删除影响**：下次 `npm install` 重新下载，不影响任何已安装项目与全局包。
- **验证方式**：官方命令 `npm cache clean --force` 清除的即此目录；本机实测（2026-08-24）约 360 MB。

## pip-cache

- **目录用途**：pip 下载的 wheel/sdist 缓存。
- **删除影响**：下次安装重新下载，不影响已安装环境。
- **验证方式**：官方命令 `pip cache purge` 清除的即此目录；本机实测（2026-08-24）约 243 MB。

## kimi-desktop-updater

- **目录用途**：Kimi 桌面版（Electron）自动更新器存放历史版本安装包。
- **删除影响**：不影响应用本体、配置与登录态；仅匹配 `.exe`/`.nupkg` 安装包，保留最新 1 份用于回滚。
- **验证方式**：目录内以版本号命名的安装包为主；本机实测（2026-08-24）目录约 1.2 GB，为可回收大户。

## workbuddy-desktop-updater

- 同类 Electron 更新器目录（`@genieworkbuddy-desktop-updater`）。仅匹配安装包扩展名，配置文件不受影响；本机实测（2026-08-24）目录约 388 MB。

## qoder-updater

- 同类 Electron 更新器目录（`qoder-work-cn-updater`）。仅匹配安装包扩展名；本机实测（2026-08-24）目录约 240 MB。

## zcode-desktop-updater

- 同类 Electron 更新器目录（`@zcodedesktop-updater`）。仅匹配安装包扩展名；本机实测（2026-08-24）目录约 141 MB。

## quark-updater

- 夸克网盘更新器目录（`QuarkCloudDriveUpdater`，本机实测约 48 MB）。**注意**：同级的 `QuarkCloudDriveMini`（约 300 MB）是应用数据目录，含用户配置，刻意不纳入任何规则——这是白名单纪律的边界示例。

## windows-temp

- **目录用途**：`%SystemRoot%\Temp`，系统服务与安装程序写入的临时文件。
- **删除影响**：与 user-temp 相同；需管理员权限，14 天阈值。
- **验证方式**：官方磁盘清理同样清理该目录。本机实测（2026-08-24）占用接近 0。

## windows-update-download

- **目录用途**：Windows Update 下载缓存（安装文件）。
- **删除影响**：需要时自动重新下载，不影响已安装更新；官方"磁盘清理→Windows 更新清理"同类。
- **验证方式**：本机实测（2026-08-24）占用接近 0（近期无待装更新）。

## uv-cache

- **目录用途**：uv（Python 包管理器）下载与构建缓存。
- **删除影响**：下次安装重新下载；已创建的虚拟环境内是硬链接/副本，不受影响。
- **验证方式**：官方命令 `uv cache clean` 清除的即此目录；本机实测目录存在（2026-08-28，占用 0）。

## playwright-browsers

- **目录用途**：Playwright 测试浏览器二进制（Chromium/Firefox/WebKit 按版本号目录存放）。
- **删除影响**：下次运行 `npx playwright install` 重新下载；不删项目代码与测试结果。
- **验证方式**：本机实测（2026-08-28）约 394 MB；阈值取 30 天，避免清掉正在使用的版本。

## cargo-registry

- **目录用途**：Cargo 下载的 crate 缓存（registry/cache 压缩包 + registry/src 解压副本）。
- **删除影响**：下次构建重新下载；已编译的 target 目录不在本规则路径内。
- **验证方式**：官方文档（doc.rust-lang.org/cargo/reference/cargo-home.html）。**本机未安装 Rust，未真机验证**。

## go-build-cache

- **目录用途**：Go 构建缓存（`go env GOCACHE` 默认值 `%LOCALAPPDATA%\go-build`）。
- **删除影响**：官方 `go clean -cache` 同类操作；下次首次构建稍慢。
- **验证方式**：官方文档（go.dev）。**本机未安装 Go，未真机验证**。

## gradle-caches

- **目录用途**：Gradle 依赖与构建缓存（`~/.gradle/caches`）。
- **删除影响**：下次构建重新下载依赖并重建缓存；项目内 `.gradle` 目录不在本规则路径内。
- **验证方式**：官方文档（docs.gradle.org/userguide/directory_layout.html）。**本机未安装 Gradle，未真机验证**。

## maven-repository

- **目录用途**：Maven 本地依赖仓库（`~/.m2/repository`）。
- **删除影响**：下次构建重新下载；`settings.xml` 等配置不在本规则路径内。
- **验证方式**：官方文档（maven.apache.org/guides/introduction/introduction-to-repos.html）。**本机未安装 Maven，未真机验证**。

## crash-dumps

- **目录用途**：应用崩溃 minidump（`%LOCALAPPDATA%\CrashDumps`，LocalDumps 默认位置）。
- **删除影响**：仅丢失崩溃排查材料，系统与应用不受影响；14 天阈值。
- **验证方式**：微软文档（learn.microsoft.com LocalDumps）。本机当前无此目录（无崩溃记录）。

## wer-reports

- **目录用途**：Windows 错误报告队列（ReportQueue 待上报 / ReportArchive 已归档）。
- **删除影响**：微软官方磁盘清理同类对象；仅丢失历史问题反馈材料。需管理员权限。
- **验证方式**：官方文档（learn.microsoft.com WER）。本机未验证具体占用。

## 数据来源

- 本机占用数据：2026-08-24 与 2026-08-28 对 `C:\Users\13739` 及系统目录的只读扫描（du 统计）。
- 未采纳的候选（记录原因）：缩略图缓存（thumbcache_*.db 常驻被 Explorer 锁定，引擎跳过策略下几乎不可清理，价值低）、Ollama/HF 模型（用户主动下载的重资源，删除代价高，不属"无风险"）、项目内 node_modules/target（白名单纪律：项目目录内容一律不自动清理）。
