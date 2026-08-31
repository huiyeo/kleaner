# 01 ASP.NET Core 托管 PWA 的发布形态

Type: research
Status: resolved

## Question

.NET 10 下 ASP.NET Core 最小 API 自包含单文件发布时：

1. 静态 PWA 资源（wwwroot：index.html、manifest.webmanifest、sw.js）如何随包分发？单文件发布是否会把它们嵌进 exe，还是散文件？
2. `MapStaticAssets()`（.NET 9+）与单文件发布是否兼容？
3. 体积与现有 Velopack 自包含 WPF 包相比变化多少？
4. localhost Kestrel 绑定有什么要点（端口、回环地址）？

产出：推荐的发布形态，供「WebHost 工程结构与 API 契约」与实施工单引用。

## Answer

**结论：推荐「自包含单文件 exe + wwwroot 散文件目录」的发布形态，体积与现状同量级。**

1. **静态资源分发**：单文件发布只把托管程序集（及可选的原生库）打包进 exe；`wwwroot` 下的内容默认**原样复制为 exe 旁边的散文件**，不嵌入。PWA 必需的 `manifest.webmanifest`、`sw.js` 本来就要求是真实的可缓存 URL，散文件形式反而正好满足（service worker 不允许从内嵌资源流里取）。发布产物 = 1 个 exe + 1 个 wwwroot 目录， Velopack 打包时一并收进安装包即可。

2. **`MapStaticAssets()` 兼容性**：兼容。它基于构建期 source generator 生成清单，运行时从程序集目录服务静态文件，支持指纹、ETag、预压缩（gzip/br）。单文件发布下资源位于发布输出目录，清单路径不变。替代旧 `UseStaticFiles()`；SPA 回退仍需显式 `MapFallbackToFile("index.html")`。

3. **体积**：自包含 ASP.NET Core app 单文件约 90–100 MB 量级（含 ASP.NET Core 共享框架裁剪后）；现有 WPF 自包含包约 70–90 MB，且可加 `PublishTrimmed` + `EnableCompressionInSingleFile` 进一步压缩。同一量级，不构成回退理由。注意：裁剪与反射敏感代码需验证（引擎层已尽量 POCO，风险低但须真机回归）。

4. **Kestrel 绑定要点**：
   - 只绑回环：`app.Urls.Add("http://127.0.0.1:<port>")`，绝不可绑 `0.0.0.0` / `*`（删除类 API 暴露到局域网即安全事故）；
   - 端口策略：默认固定端口 + 被占用时回退随机高端口，并把实际端口传给浏览器打开逻辑；具体安全模型（token/Origin 校验）归「进程模型与 API 安全模型」工单；
   - 不需要 HTTPS（localhost 明文 + SW 要求 localhost 属于 secure context，`http://127.0.0.1` 可注册 service worker 与安装 PWA）。

风险提示：真机需验证 Velopack 安装目录只读属性与 SW 写缓存的关系（SW 缓存写在浏览器侧，无冲突；但 `RuleUpdateService` 写用户目录的现状不受影响）。
