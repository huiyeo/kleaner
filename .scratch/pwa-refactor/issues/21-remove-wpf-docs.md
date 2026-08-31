# 21 删除 Kleaner.App 与文档同步

Type: task
Status: open
Blocked by: 20

## Task

观察期无回归后收尾：

1. 删 `Kleaner.App` 工程（前置已满足：19 Advanced 已落地、20 对等验收通过）；slnx / StartupObject / 发布脚本清理，WebHost 成为唯一 GUI 出口。
2. 文档同步（map 悬置项归属本票）：`docs/architecture.md`（工程表 7→6、入口点、分层约束、前端工程约定节）、`README.md`、`docs/deletion-path.md` 中 WPF 表述全部更新为 WebHost 语义。

## Acceptance

- 解决方案无 `Kleaner.App` 引用残留，全量构建 + 29 测试绿。
- 三份文档与代码事实一致；`deletion-path.md` 的 GUI 表述全部指向 WebHost。

## Comments
