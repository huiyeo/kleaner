# Issue tracker: 本地 Markdown

工单与规格以 Markdown 文件存于 `.scratch/` 下。

## 约定

- 一个功能一个目录：`.scratch/<feature-slug>/`
- 规格文件：`.scratch/<feature-slug>/spec.md`
- 实现工单：每张一张文件，`.scratch/<feature-slug>/issues/<NN>-<slug>.md`，从 `01` 编号，禁止合并为单个文件
- triage 状态记录在每张工单文件顶部附近的 `Status:` 行（角色词见 `docs/agents/triage-labels.md`）
- 评论与讨论记录追加到文件底部 `## Comments` 标题之下

## 技能说"发布到工单系统"时

在 `.scratch/<feature-slug>/` 下新建文件（目录不存在则创建）。

## 技能说"取相关工单"时

读取引用路径指向的文件。用户通常直接给出路径或工单编号。

## Wayfinding 操作

供 `/wayfinder` 使用。**map** 是一个文件，每张**子工单**一个文件。

- **Map**：`.scratch/<effort>/map.md`（Notes / Decisions-so-far / Fog 正文）
- **子工单**：`.scratch/<effort>/issues/NN-<slug>.md`，从 `01` 编号，正文含问题；`Type:` 行记录工单类型（`research`/`prototype`/`grilling`/`task`）；`Status:` 行记录 `claimed`/`resolved`
- **阻塞**：顶部 `Blocked by: NN, NN` 行。所列出文件全部 `resolved` 前视为未解除阻塞
- **Frontier**：扫描 `.scratch/<effort>/issues/`，取打开、未阻塞、未认领的文件；按编号从小到大
- **认领**：开工前把 `Status:` 设为 `claimed` 并保存
- **解决**：在 `## Answer` 标题下追加答案，`Status:` 设为 `resolved`，然后在 `map.md` 的 Decisions-so-far 追加上下文指针（gist + 链接）
