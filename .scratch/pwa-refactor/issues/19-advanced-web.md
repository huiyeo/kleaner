# 19 Advanced 三 Tab web（删 WPF 的前置条件）

Type: task
Status: resolved
Blocked by: 14, 15

## Task

Advanced 高级模式进 web（06 定为唯一后补项，同时是删 WPF 的前置条件）：WSL vhdx 指引、系统大件引导、注册表残留只读扫描。SpecialOps「只扫描与指引、不直接改动系统项」原则不变（06 第 4 条）。

## Acceptance

- 三个 Tab 功能与 WPF `AdvancedWindow` 对等（只读扫描 + 指引文案）。
- 无任何新增删除路径；注册表扫描严格只读。
- 删 WPF（21）前本票必须已落地——不许出现 Advanced 两边都没有的功能空窗。

## Comments

- WSL vhdx、系统大件指引与注册表残留三 Tab 已进入 PWA；新增 API 全部只读，直接复用 SpecialOps 检测与指引，不提供执行系统命令或修改注册表的端点。
- 验证：Core 60、WebHost 78 全绿；`node --check` 与差异检查通过。
