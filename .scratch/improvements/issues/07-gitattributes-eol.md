# 07: 添加 .gitattributes 统一换行策略

**What to build:** 仓库无 `.gitattributes`，工作区 LF 与 CRLF 混存，每次 git 写操作都产生"LF will be replaced by CRLF"类告警。添加 `.gitattributes` 并一次性 renormalize，与 `.editorconfig` 的约定（`*.cs`/`*.xaml` 落 CRLF）对齐。

**Blocked by:** None（可立即开始）

**Status:** complete

- [x] `.gitattributes`：`* text=auto` 为基线；`*.cs`、`*.xaml`、`*.csproj`、`*.slnx`、`*.json`、`*.md` 显式 `text eol=crlf`（与 .editorconfig 一致）；`*.png`、`*.ico`、`*.exe` 等显式 `binary`
- [x] 执行 `git add --renormalize .`，提交前确认未产生已跟踪文本的内容或行尾差异
- [x] 提交前复验 `git add`/`git checkout` 不再出现换行告警
- [x] Windows 上 Release 构建 0 警告/0 错误，完整测试 72/72 通过

完成于 2026-09-05：规范化未产生既有文本差异；规则只新增换行与二进制文件声明。
