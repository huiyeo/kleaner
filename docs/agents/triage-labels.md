# Triage 标签

技能以五个规范 triage 角色表述。本文件把角色映射到本仓库工单系统的实际标签字符串。

| 角色（mattpocock/skills） | 本仓库标签 | 含义 |
| ------------------------ | ---------- | ---- |
| `needs-triage`           | `needs-triage` | 维护者需要评估此工单 |
| `needs-info`             | `needs-info`   | 等待报告者补充信息 |
| `ready-for-agent`        | `ready-for-agent` | 已完全明确，可交给 AFK agent |
| `ready-for-human`        | `ready-for-human` | 需人工实现 |
| `wontfix`                | `wontfix`       | 不予处理 |

技能提到某角色（如"应用 ready-for-agent 标签"）时，用本表对应的标签字符串。

本地 Markdown 模式下，标签以 `Status:` 行记录在工单文件顶部，不单独建文件。改右列即可适配实际词汇。
