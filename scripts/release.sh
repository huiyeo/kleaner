#!/bin/bash
# Kleaner 发布脚本：自包含发布 → Velopack 打包（Setup/Portable/更新清单）
# 用法：scripts/release.sh <版本号>   示例：scripts/release.sh 0.1.0
# 注意：不得启用 PublishSingleFile——WPF 本机依赖（wpfgfx/D3DCompiler/PenImc/
# vcruntime140_cor3）在单文件 bundle 下经 Velopack 安装后缺失，应用启动即死
# （安装矩阵 2026-09-10 实测抓取）。散文件 + --self-contained 是已验证形态。
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:?用法: release.sh <版本号>}"
DOTNET="${DOTNET:-dotnet}"

echo "== 发布 v$VERSION =="
"$DOTNET" publish src/Kleaner.App -c Release -r win-x64 --self-contained -o publish

echo "== Velopack 打包 =="
vpk pack -u Kleaner -v "$VERSION" -p publish -e Kleaner.App.exe -o releases --packTitle Kleaner --icon assets/icon/Kleaner.ico

echo "== 产物 =="
ls -la releases/
