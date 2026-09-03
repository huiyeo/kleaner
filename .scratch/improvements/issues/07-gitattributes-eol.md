# 07: 添加 .gitattributes 统一换行策略

**What to build:** 仓库无 `.gitattributes`，工作区 LF 与 CRLF 混存，每次 git 写操作都产生"LF will be replaced by CRLF"类告警。添加 `.gitattributes` 并一次性 renormalize，与 `.editorconfig` 的约定（`*.cs`/`*.xaml` 落 CRLF）对齐。

**Blocked by:** None（可立即开始）

**Status:** ready-for-agent

- [ ] `.gitattributes`：`* text=auto` 为基线；`*.cs`、`*.xaml`、`*.csproj`、`*.slnx`、`*.json`、`*.md` 显式 `text eol=crlf`（与 .editorconfig 一致）；`*.png`、`*.ico`、`*.exe` 等显式 `binary`
- [ ] 执行 `git add --renormalize .`，提交前确认 diff 仅含换行差异、无内容变化
- [ ] 提交后任意 `git add`/`git checkout` 不再出现换行告警
- [ ] Windows 上构建与 60/60 测试保持绿色
