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

## 数据来源

- 本机占用数据：2026-08-24 对 `C:\Users\13739` 及系统目录的只读扫描（du 统计）。
- 后续候选规则（待验证后进入规则库）：ms-playwright 浏览器缓存（属开发工具二进制，删除导致下次重新下载，暂缓）、`%LOCALAPPDATA%\Microsoft\Windows\INetCache`（本机实测仅 0.1 MB，价值低）。
