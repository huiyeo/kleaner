# 规则库契约

规则库是 Kleaner 的核心资产。改动它之前先读 `../CONTRIBUTING.md` 的三关流程——本文件讲机制，那一份讲验收。

## 文件构成

| 文件 | 作用 |
|---|---|
| `rules/rules.v1.json` | 规则数据本体 |
| `rules/schema/v1/kleaner-rules.schema.json` | JSON Schema（draft 2020-12） |
| `rules/docs/safety-notes.md` | 每条规则的目录用途 / 删除影响 / 验证方式 |
| `src/Kleaner.Core/RuleSetLoader.cs` | 加载与语义校验 |
| `src/Kleaner.Core/RuleUpdateService.cs` | 在线更新（SHA512 + 语义校验） |

规则经 `safetyDoc` 字段的锚点（`docs/safety-notes.md#<rule-id>`）与安全说明文档关联。

## Schema 字段

`additionalProperties: false` 在根、`defaults`、`rule` 三层都生效——**任何未声明字段都会校验失败**。

### 根

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `schemaVersion` | const `1` | 是 | 不等于 1 直接抛异常 |
| `channel` | string | 否 | 规则更新通道标识。*代码从不读取* |
| `defaults.ageDays` | integer ≥1 | 否 | 全局默认年龄阈值 |
| `defaults.ageDaysByCategory` | object | 否 | 按分类覆盖全局默认；键必须是 category 枚举 |
| `rules` | array ≥1 | 是 | 规则数组 |

### 规则

必填 7 项：`id`、`name`、`category`、`risk`、`paths`、`requiresElevation`、`safetyNotes`。

| 字段 | 类型 | 默认 | 约束与语义 |
|---|---|---|---|
| `id` | string | — | `^[a-z0-9][a-z0-9-]*$`，≤64 字符。稳定标识，事故追溯靠它 |
| `name` | string | — | 1–64 字符 |
| `category` | enum | — | `temp` / `browser-cache` / `dev-cache` / `updater` / `system` / `application` |
| `risk` | enum | — | `low` / `medium`。**没有第三档** |
| `paths` | string[] | — | 1–32 项；每项须匹配 `^(%VAR%\|[A-Za-z]:)[\\/].+` |
| `exclude` | string[] | `[]` | ≤32 项；优先级高于 `paths` |
| `ageDays` | integer≥1 / null | `null` | `null` 表示继承分类默认，再继承全局默认 |
| `keepNewest` | integer≥1 / null | `null` | 保留最新 N 份，**设置后豁免年龄阈值** |
| `requiresElevation` | boolean | — | 需 UAC 提权才执行 |
| `enabled` | boolean | `true` | 关闭则不扫描 |
| `safetyNotes` | string | — | **≥20 字符**。目录用途、删除影响、验证方式 |
| `safetyDoc` | string | 无 | 安全说明文档锚点，PR 必填 |
| `verified` | string | 无 | 验证状态声明 |
| `lockedFilePolicy` | enum `["skip"]` | `"skip"` | *代码从不读取*，行为硬编码在隔离区移动逻辑 |

## 加载与校验

`RuleSetLoader.LoadFromJson` 是**严格解析**：缺 `id` / `name` / `category` / `risk` / `paths` / `requiresElevation` / `safetyNotes` 任一字段，或 category / risk 取值非法，直接抛 `FormatException`。

解析通过后另有一轮**语义校验**（`Validate`），收集而非抛出：

- 规则 `id` 重复
- `safetyNotes` 不足 20 字符
- 既无年龄阈值也无 `keepNewest` → 按安全默认拒绝执行
- `paths` 为空

**注意**：`Validate` 的返回值需要调用方自己检查并决定如何处理。CLI 的 `scan` 与 `clean` 都会先 `Validate`，非空则 `Fail` 返回 1；`RuleUpdateService` 更新时非空则拒绝应用。新增调用点时不要忘记这一步。

## 路径模式

`GlobScanner` 负责：

- `%ENV%` 展开（如 `%LOCALAPPDATA%`）
- `*` 匹配单段路径，`**` 跨任意层级
- **reparse point 一律排除**（OneDrive 占位文件、目录联结），规则里无需声明

`exclude` 编译为整路径正则后对命中结果做剔除，优先级高于 `paths`。

## 年龄阈值的解析顺序

`RuleSetLoader.EffectiveAgeDays`（扩展方法）：

1. 规则设了 `keepNewest` → 返回 `null`（豁免年龄，改按版本保留）
2. 规则级 `ageDays`
3. `defaults.ageDaysByCategory[category]`
4. `defaults.ageDays`

四步都落空 → `Validate` 报"按安全默认拒绝执行"。

`RuleSelector.Apply` 兑现这层语义：`keepNewest` 生效时按修改时间倒序跳过前 N 份；否则保留 `mtime < now - ageDays` 的文件。

*为什么 `keepNewest` 不按父目录分组*：更新器的现行包可能位于 `pending/` 等子目录，按父目录分组会让新旧包各自保留 N 份，起不到淘汰旧版本的作用。

## `risk` 与 `verified` 的分工

容易混淆，二者语义**不重叠**：

- **`risk` 只影响界面显示**，当前不参与任何执行分支。
- **`verified` 才有行为**：以「本机实测」开头的规则默认勾选；其余默认**不勾选**并标注「未验证·默认不勾选」。未声明 `verified` 的旧规则视同已验证（兼容性处理）。

这条策略定义在 `Kleaner.App/RuleRow.cs`。

## 三关流程

`CONTRIBUTING.md` 定义，字段承载关系如下：

| 关 | 要求 | 落在哪 |
|---|---|---|
| 一、权威来源核实 | 目录用途须有官方出处，或软件自带清理命令 | `safetyNotes`、`rules/docs/safety-notes.md` |
| 二、安全边界定义 | 删什么、留什么、多久恢复；路径收窄到缓存/安装包本身 | `paths` / `exclude` / `keepNewest` / `safetyNotes` |
| 三、真机验证 | 本机实测该目录存在、清理后软件正常；无法验证须诚实标注 | `verified` |

不予合并：项目目录内内容（`node_modules`、`target`、`build`）、用户主动下载的重资源、注册表写操作、常驻被锁实际清不掉的文件。

## 在线更新

`RuleUpdateService.CheckAndUpdateAsync`：下载 → SHA512 校验 → 解析 → 语义校验 → 写入 `%APPDATA%\Kleaner\rules\rules.v1.json`。任一步失败即拒绝应用。

`LoadEffective` 的候选顺序是**本地覆盖优先于内置规则库**。这是排查"改了规则没生效"的第一站。

## 已知问题

- `lockedFilePolicy` 与 `channel` 属于**悬空词**：schema 有声明（前者连 C# 模型字段都没有），代码从不消费。
- `safetyDoc` 被读入但代码不使用——它是给人看的锚点，不是程序逻辑。
- 规则条数随库增长，本文件不固化具体数字。需要时用 `rules/rules.v1.json` 现查。
