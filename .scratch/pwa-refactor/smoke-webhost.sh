#!/usr/bin/env bash
# 工单 10 运行时冒烟：单实例二次启动 + 空闲自动退出
set -u
exe="/d/Projects/zwork/kleaner/src/Kleaner.WebHost/bin/Release/net10.0-windows/Kleaner.WebHost.exe"
svc="$APPDATA/Kleaner/service.json"

echo "--- 启动实例 A ---"
"$exe" &
A_PID=$!
sleep 4

echo "--- service.json ---"
cat "$svc" 2>/dev/null || echo "(未找到 service.json —— FAIL)"

echo "--- 启动实例 B（二次启动，应唤起浏览器后自行退出）---"
"$exe" &
B_PID=$!
sleep 5

if kill -0 "$B_PID" 2>/dev/null; then
  echo "B 仍在运行 —— FAIL（二次启动未退出）"
  kill "$B_PID" 2>/dev/null
else
  echo "B 已自行退出 —— OK"
fi

if kill -0 "$A_PID" 2>/dev/null; then
  echo "A 仍在运行 —— OK（首个实例存活）"
else
  echo "A 已提前退出 —— FAIL"
fi

echo "--- 等待空闲宽限期（30s + 检查间隔）---"
sleep 45

if kill -0 "$A_PID" 2>/dev/null; then
  echo "A 仍在运行 —— FAIL（空闲 30s 未退出）"
  kill "$A_PID" 2>/dev/null
else
  echo "A 已在空闲后自行退出 —— OK"
fi
