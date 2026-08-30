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

## ai-huggingface-xet-cache

- **HuggingFace 缓存**（application，风险 medium）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-adobe-media-cache

- **Adobe 缓存**（application，风险 medium）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://helpx.adobe.com/premiere/desktop/troubleshooting/media-issues/manage-media-cache.html仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-battlenet-cache

- **暴雪战网缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://us.support.blizzard.com/en/article/34721、https://eu.support.blizzard.com/en/article/24123、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-chatgpt-cache

- **ChatGPT 桌面版缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://openai.com/index/introducing-the-codex-app/、https://help.openai.com/en/articles/20001276-moving-to-the-new-chatgpt-desktop-app、https://help.openai.com/en/articles/20001277-using-the-built-in-browser-in-the-chatgpt-desktop-app仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-discord-cache

- **Discord 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://support.discord.com/hc/en-us/articles/115004307527--Windows-Corrupt-Installation、https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-douyin-live-updater-cache

- **抖音直播伴侣更新器缓存**（updater，风险 low）应用内置更新器的安装包残留，按「保留最新 1 份」策略清理。被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-dropbox-rendering-cache

- **Dropbox 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://help.dropbox.com/installs/desktop-application-overview、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-ea-rendering-cache

- **EA App 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://help.ea.com/en/articles/technical-issues/clear-cache/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-electron-cache-slack

- **Slack 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-electron-cache-microsoft-teams

- **Microsoft Teams 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-electron-cache-figma

- **Figma 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-electron-cache-obsidian

- **Obsidian 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-electron-cache-insomnia

- **Insomnia 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-electron-cache-claude

- **Claude 桌面版缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-electron-cache-github-desktop

- **GitHub Desktop 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-electron-updater-cache

- **Electron 系更新器缓存**（updater，风险 low）应用内置更新器的安装包残留，按「保留最新 1 份」策略清理。被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-flashvoice-cache

- **FlashVoice 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-game-launcher-cache

- **游戏启动器（Epic/育碧）缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.epicgames.com/help/c-1/a202300000013316仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-gitmind-rendering-cache

- **GitMind 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://gitmind.com/download、https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-netease-cloud-music-cache

- **网易云音乐缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://music.163.com/#/download、https://github.com/microsoft/winget-pkgs/tree/master/manifests/n/NetEase/CloudMusic/3.1.37.205354、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-notion-cache

- **Notion 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.notion.com/help/reset-notion、https://www.notion.com/help/use-pages-offline、https://www.electronjs.org/docs/latest/api/app#appgetpathname仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-obs-diagnostic-cache

- **OBS Studio 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。仅命中 OBS 的日志/性能分析/崩溃目录。仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-postman-cache

- **Postman 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://learning.postman.com/latest-v-12/docs/getting-started/troubleshooting-inapp、https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-qq-rendering-cache

- **QQ 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://im.qq.com/index/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-signal-cache

- **Signal 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://github.com/signalapp/Signal-Desktop/tree/v8.22.0、https://www.electronjs.org/docs/latest/api/app#appgetpathname、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-sogou-input-cache

- **搜狗输入法缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://shurufa.sogou.com/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-spotify-rendering-cache

- **Spotify 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://support.spotify.com/us/article/storage-information/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-steam-rendering-cache

- **Steam 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://store.steampowered.com/news/101607/、https://store.steampowered.com/news/4186/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-teams-msix-cache

- **Teams 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://learn.microsoft.com/en-us/troubleshoot/microsoftteams/teams-administration/clear-teams-cache仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-telegram-temporary-cache

- **Telegram 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。仅限 tdata 下的 temp 与 dumps 目录，不触碰会话数据。参考：https://github.com/telegramdesktop/tdesktop/tree/v7.0.9仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-tencent-meeting-cache

- **腾讯会议缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://meeting.tencent.com/download/、https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/Tencent/TencentMeeting/3.44.10.457、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-vlc-cache

- **VLC 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://github.com/videolan/vlc/tree/3.0.23仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-wechat-diagnostic-cache

- **微信诊断日志**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-wechat-rendering-cache

- **微信渲染/小程序缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。仅命中小程序编译缓存与网页缓存，不触碰聊天记录与用户数据。参考：https://weixin.qq.com/cgi-bin/readtemplate?lang=zh_CN&t=download/windows、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-wecom-diagnostic-cache

- **企业微信缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://work.weixin.qq.com/仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-whatsapp-rendering-cache

- **WhatsApp 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://apps.microsoft.com/detail/9nksqgp7f2nh、https://www.whatsapp.com/download、https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-wps-cache

- **WPS 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.wps.cn/product/wpswin仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-wps-diagnostic-cache

- **WPS 日志与转储**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.wps.cn/product/wpswin仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-wps-rendering-cache

- **WPS 渲染缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://www.wps.cn/product/wpswin、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-zenaion-cache

- **Zenaion 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://zenai.bot/guide/core-features/view-all、https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## app-zoom-diagnostic-cache

- **Zoom 日志**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。仅限 Zoom 日志目录，14 天阈值。参考：https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0066286仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-2345-cache

- **2345 浏览器缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://www.2345.com/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-360-safe-cache

- **360 安全浏览器缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://browser.360.cn/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-360-speed-cache

- **360 极速浏览器缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://browser.360.cn/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-arc-cache

- **Arc 浏览器缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-brave-cache

- **Brave 缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://support.brave.com/hc/en-us/articles/360017903152-How-Do-I-Clear-Cookies-And-Site-Data-In-Brave仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-chromium-cache

- **Chromium 缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-duckduckgo-cache

- **DuckDuckGo 缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-firefox-cache

- **Firefox 缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://support.mozilla.org/en-US/kb/how-clear-firefox-cache、https://support.mozilla.org/en-US/kb/profiles-where-firefox-stores-user-data仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-gecko-family-cache

- **Gecko 系浏览器缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-opera-cache

- **Opera 缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://help.opera.com/en/latest/web-preferences/#clearBrowsingData仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-sogou-cache

- **搜狗浏览器缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://ie.sogou.com/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-uc-cache

- **UC 浏览器缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://www.uc.cn/zh-cn/browser/pc.html、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## browser-vivaldi-cache

- **Vivaldi 缓存**（browser-cache，风险 low）浏览器的网页缓存与着色器缓存，删除后按需重新下载，不涉及书签、密码、历史记录等用户数据。参考：https://help.vivaldi.com/desktop/tools/delete-browsing-data/仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## container-docker-desktop-rendering-cache

- **Docker Desktop 缓存**（application，风险 low）应用运行产生的网页缓存、着色器缓存、日志或崩溃报告，均为可再生数据，不涉及聊天记录、账号与用户配置。参考：https://docs.docker.com/desktop/settings-and-maintenance/settings/、https://docs.docker.com/desktop/settings-and-maintenance/backup-and-restore/、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-android-cache

- **Android SDK 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://developer.android.com/tools/sdkmanager仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-android-user-cache

- **Android 用户目录缓存（~/.android）**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://developer.android.com/studio/command-line/variables仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-build-accelerator-cache

- **编译加速器（sccache/Terraform）缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://github.com/mozilla/sccache/blob/main/docs/Configuration.md、https://developer.hashicorp.com/terraform/cli/config/config-file#provider-plugin-cache仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-ccache-cache

- **ccache 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。已排除配置文件 ccache.conf，保留用户缓存配置。参考：https://ccache.dev/manual/latest.html#_location_of_the_configuration_file仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-copilot-cli-cache

- **Copilot CLI 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference#changing-the-location-of-the-configuration-directory仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-dart-analysis-cache

- **Dart 分析服务缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://github.com/flutter/flutter/blob/master/dev/devicelab/lib/tasks/analysis.dart、https://github.com/flutter/flutter/blob/master/dev/snippets/test/filesystem_resource_provider.dart仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-editor-cache-code

- **VS Code 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://github.com/microsoft/vscode/tree/1.132.0、https://code.visualstudio.com/docs/configure/command-line#_advanced-cli-options、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-editor-cache-cursor

- **Cursor 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://github.com/microsoft/vscode/tree/1.132.0、https://code.visualstudio.com/docs/configure/command-line#_advanced-cli-options、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-editor-cache-windsurf

- **Windsurf 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://github.com/microsoft/vscode/tree/1.132.0、https://code.visualstudio.com/docs/configure/command-line#_advanced-cli-options、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-editor-cache-vscodium

- **VSCodium 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://github.com/microsoft/vscode/tree/1.132.0、https://code.visualstudio.com/docs/configure/command-line#_advanced-cli-options、https://chromium.googlesource.com/chromium/src/+/main/docs/user_data_dir.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-go-module-cache

- **Go 模块下载缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://go.dev/ref/mod#module-cache仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-hex-cache

- **Hex 包管理器缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://hex.hexdocs.pm/Mix.Tasks.Hex.Config.html仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-jetbrains-cache

- **JetBrains IDE 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://www.jetbrains.com/help/idea/tuning-the-ide.html#system-directory、https://www.jetbrains.com/help/idea/invalidate-caches.html仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-jvm-tooling-cache

- **JVM 工具链（sbt/Ivy）缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://www.scala-sbt.org/1.x/docs/Launcher-Getting-Started.html、https://ant.apache.org/ivy/history/latest-milestone/settings/caches.html仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-node-tooling-cache

- **Node 工具链（corepack/node-gyp/electron）缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://github.com/nodejs/corepack#environment-variables、https://github.com/nodejs/node-gyp#command-options、https://github.com/electron/get#how-it-works仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-nuget-cache

- **NuGet 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。包含全局包目录 ~/.nuget/packages，删除后首次还原构建会重新下载。参考：https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders、https://learn.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-locals仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-package-manager-cache

- **包管理器（Composer/deno/vcpkg）缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://getcomposer.org/doc/06-config.md#cache-dir、https://docs.deno.com/runtime/getting_started/installation/#cache-location、https://learn.microsoft.com/en-us/vcpkg/users/binarycaching#default-binary-cache仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-pnpm-cache

- **pnpm 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://pnpm.io/cli/store仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-python-tooling-cache

- **Python 工具链（Poetry/pyenv）缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://python-poetry.org/docs/configuration/#cache-dir、https://github.com/pyenv-win/pyenv-win仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-sccache-cache

- **sccache 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://github.com/mozilla/sccache/blob/main/docs/Configuration.md仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-user-tool-cache

- **用户级开发工具（bun/ruff/mypy 等）缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://bun.sh/docs/pm/global-cache、https://github.com/nodejs/corepack#environment-variables、https://github.com/nodejs/node-gyp#command-options仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-visual-studio-cache

- **Visual Studio 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://learn.microsoft.com/en-us/visualstudio/extensibility/managed-extensibility-framework-in-the-editor仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## dev-yarn-cache

- **Yarn 缓存**（dev-cache，风险 medium）开发工具链的下载/编译缓存，删除后由工具按需重建，不影响已安装的工具本身与项目源码。参考：https://classic.yarnpkg.com/lang/en/docs/cli/cache/仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## system-directx-shader-cache

- **GPU 着色器（DirectX/NVIDIA/AMD/Intel）缓存**（system，风险 low）Windows 系统组件缓存，由系统按需重建。删除后首次启动游戏/3D 应用会重新编译着色器，可能短暂变慢。仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## system-stale-partial-downloads

- **下载目录残留分块缓存**（system，风险 medium）Windows 系统组件缓存，由系统按需重建。仅命中浏览器下载产生的 .crdownload/.part 等分块临时文件，不触碰正常命名的下载文件，深度限制在下载目录 3 层以内。仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。

## system-thumbnail-cache

- **Windows 缩略图缓存**（system，风险 low）Windows 系统组件缓存，由系统按需重建。参考：https://learn.microsoft.com/en-us/windows/win32/api/thumbcache/nn-thumbcache-ithumbnailcache仅清理限定目录内容，被占用文件自动跳过并提示。
- 验证状态：官方文档来源，本机未验证，默认不勾选。
