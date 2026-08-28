#!/bin/bash
# Kleaner 发布脚本：自包含单文件发布 → Velopack 打包（Setup/Portable/更新清单）
# 用法：scripts/release.sh <版本号>   示例：scripts/release.sh 0.1.0
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:?用法: release.sh <版本号>}"
DOTNET="${DOTNET:-dotnet}"

echo "== 发布 v$VERSION =="
"$DOTNET" publish src/Kleaner.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -o publish

echo "== Velopack 打包 =="
vpk pack -u Kleaner -v "$VERSION" -p publish -e Kleaner.App.exe -o releases --packTitle Kleaner

echo "== 产物 =="
ls -la releases/
