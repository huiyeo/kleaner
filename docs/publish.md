# 发布 v0.1.0 操作指引

> 摘要：本机 gh 令牌已失效，GitHub 公开与首发 Release 需先重新认证，再执行本文两条命令；产物与发布说明均已备好。

## 一次性前置（需本人操作）

```
gh auth login -h github.com
```

按提示走浏览器授权即可。

## 发布命令（认证后执行）

```
gh repo create kleaner --public --source . --push
gh release create v0.1.0 "releases/Kleaner-win-Setup.exe" "releases/Kleaner-win-Portable.zip" "releases/RELEASES" "releases/Kleaner-0.1.0-full.nupkg" --title "Kleaner v0.1.0 — 首个公开版本" --notes-file docs/release-notes-v0.1.0.md
```

说明：`RELEASES` 与 `full.nupkg` 是 Velopack 自动更新通道的约定文件名，GitHub Release 附件会作为后续版本的更新源（应用内接上 UpdateManager 后指向 `https://github.com/<你的用户名>/kleaner` 即可）。

## 本地复现打包

```
scripts/release.sh 0.1.0
```

产物：`releases/Kleaner-win-Setup.exe`（安装版，68 MB）、`Kleaner-win-Portable.zip`（便携版）、`RELEASES`（更新清单）。已于本机实测：静默安装、快捷方式、卸载项、安装产物运行全部通过。
