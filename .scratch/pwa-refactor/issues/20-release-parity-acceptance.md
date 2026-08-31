# 20 发布链路落地与对等验收（观察期版本）

Type: task
Status: open
Blocked by: 16, 17, 18, 19

## Task

1. 发布形态落地（01/02）：自包含单文件 exe + wwwroot 散文件，`PublishTrimmed` + `EnableCompressionInSingleFile` 真机回归（裁剪与反射敏感代码验证）；`scripts/release.sh` 改喂 WebHost 产物，`vpk pack` 出安装 / 便携包，快捷方式语义不变。
2. 对等验收：以 `docs/deletion-path.md` 为基准的删除路径逐项对照清单**全量**过一遍（12/13 已各自验收的段落复核汇总）；29 个引擎测试全绿是底线而非充分条件。
3. 发布「带 WPF 的观察期版本」（06：双栈最后一版），收集回归反馈。

## Acceptance

- 对照清单全量打勾记录在本票 Comments；任何一项不过 → 回退修对应票，不带病发布。
- 观察期版本经 Velopack 安装、更新、回退（装回旧版）三路径真机验证。

## Comments
