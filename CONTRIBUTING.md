# 贡献指南

> 摘要：Kleaner 的产品质量核心是规则库的可审计性。规则 PR 走"三关"流程：权威来源核实 → 安全边界定义 → 真机验证；无法通过三关的规则不予合并。

## 规则贡献三关（借鉴 MangoDisk 并固化为流程）

1. **权威来源核实**：该目录的用途必须有权威出处——软件官方文档、微软官方文档、或软件自身的官方清理命令（如 `npm cache clean --force`、`uv cache clean`）。"网上都说能删"不算出处。
2. **安全边界定义**：明确删除影响（删什么、留什么、多久恢复）并在 `safetyNotes` 中写清；路径模式必须收窄到缓存/安装包本身，配置、会话、存档一律排除在外。拿不准的一律排除。
3. **真机验证**：在你自己的机器上实测该目录存在、内容与预期一致、清理后软件正常。无法真机验证时，必须在 `verified` 字段与 `docs/safety-notes.md` 中诚实标注"官方文档来源，本机未验证"。

## PR 清单

- `rules/rules.v1.json` 新增规则：`id`（稳定 kebab-case）、`category`、`risk`（low 起步）、`paths`（环境变量开头 + 通配）、`safetyNotes`（≥20 字）、`safetyDoc`（锚点）、`verified`（验证状态）
- `rules/docs/safety-notes.md` 对应条目：目录用途 / 删除影响 / 验证方式
- schema 校验通过：CI 会执行 `dotnet test`（含随库规则校验用例）

## 不予合并的规则

- 任何项目目录内的内容（node_modules、target、build 等）——项目目录内容一律不自动清理
- 用户主动下载的重资源（AI 模型、游戏、影音）
- 注册表写操作（当前版本只读）
- 常驻被锁、实际清不掉的文件（先验证可清性）

## 代码贡献

```
dotnet build Kleaner.slnx -c Release
dotnet test tests/Kleaner.Core.Tests -c Release
```

引擎改动必须附带单元测试；删除类操作必须走隔离区并写入操作历史。
