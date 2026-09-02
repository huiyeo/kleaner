using System.Text.Json;
using Kleaner.Core;
using Kleaner.Executor;
using Kleaner.SpecialOps;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost;

/// <summary>
/// 组装 WebApplication：安全中间件、冒烟端点、空闲退出服务。
/// 生产（Program）与集成测试共用同一条管线，保证测试测的就是跑的。
/// </summary>
public static class WebHostAppFactory
{
    public static WebApplication Build(KleanerWebHostOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = options.ContentRootPath,
        });

        if (options.UseTestServer)
        {
            builder.WebHost.UseTestServer();
            if (!string.IsNullOrWhiteSpace(options.TestStaticWebRoot))
            {
                builder.WebHost.UseWebRoot(options.TestStaticWebRoot);
            }
        }
        else
        {
            // 安全第一层：只绑 127.0.0.1 回环（工单 03；测试服务器无真实绑定）
            builder.WebHost.ConfigureKestrel(kestrel =>
                kestrel.Listen(System.Net.IPAddress.Loopback, options.Port));
        }

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new LoopbackSecurityOptions
        {
            Port = options.Port,
            Token = options.Token ?? KleanerWebHostOptions.GenerateToken(),
        });
        builder.Services.AddSingleton<IActivityTracker, ActivityTracker>();
        builder.Services.AddSingleton<JobRegistry>();
        builder.Services.AddSingleton<PlanRegistry>();
        builder.Services.AddSingleton<IJobEventBus, JobEventBus>();
        builder.Services.AddSingleton<IScanJobService, ScanJobService>();
        builder.Services.AddSingleton<IToolboxJobService, ToolboxJobService>();

        if (options.EnableIdleExit)
        {
            builder.Services.AddHostedService<IdleExitService>();
        }

        var app = builder.Build();

        app.UseMiddleware<LoopbackSecurityMiddleware>();
        if (options.UseTestServer)
        {
            // TestServer 的入口程序集是 testhost，故无生产静态资源清单；用复制到测试输出的同一 wwwroot。
            app.UseStaticFiles();
        }
        else
        {
            app.MapStaticAssets();
        }

        // 冒烟端点（工单 10）
        app.MapGet("/api/health", () => Results.Json(new { status = "ok" }));
        app.MapPost("/api/ping", () => Results.Json(new { status = "pong" }));

        // 设置：与 GUI / CLI 共用同一份 settings.json 和同样的三字段，不引入第二套配置来源。
        app.MapGet("/api/settings", (KleanerWebHostOptions options) => Results.Json(SettingsStore.Load(options)));
        app.MapPut("/api/settings", (HostSettings settings, KleanerWebHostOptions options) =>
        {
            var normalized = settings.Normalize();
            SettingsStore.Save(options, normalized);
            return Results.Json(normalized);
        });

        // 规则更新：保留 Core 既有 下载 → SHA512 → 语义校验 → 用户覆盖文件 的完整校验链。
        app.MapPost("/api/rules/update", async (KleanerWebHostOptions options) =>
        {
            var settings = SettingsStore.Load(options);
            if (string.IsNullOrWhiteSpace(settings.RuleUpdateUrl) || string.IsNullOrWhiteSpace(settings.RuleUpdateSha512))
            {
                return Results.BadRequest(new { error = "请先在设置中填写规则更新地址和 SHA512" });
            }

            var update = options.RuleUpdateExecutor
                ?? ((url, sha512) => RuleUpdateService.CheckAndUpdateAsync(url, sha512));
            var error = await update(settings.RuleUpdateUrl, settings.RuleUpdateSha512);
            return error is null
                ? Results.Json(new { updated = true })
                : Results.BadRequest(new { error });
        });

        // Job 体系与 SSE 事件流（工单 11）：REST 快照 + 单一多路复用流，重连 = 先快照再增量（工单 07）。
        // scan/plan/confirm 等资源端点留工单 12–14。
        app.MapGet("/api/jobs", (JobRegistry registry) => Results.Json(registry.Snapshot()));

        app.MapGet("/api/jobs/{jobId}", (string jobId, JobRegistry registry) =>
            registry.Get(jobId) is { } job
                ? Results.Json(job.ToSnapshot())
                : Results.NotFound(new { error = "job not found" }));

        // 取消语义（工单 07/11）：202 受理（Running→Cancelling，幂等）；404 未知；409 已终态。
        // 清理类（plan confirm）不进 job 体系，取消端点对它天然不可达（工单 12）。
        app.MapPost("/api/jobs/{jobId}/cancel", (string jobId, JobRegistry registry) => registry.TryCancel(jobId) switch
        {
            JobCancelOutcome.Accepted => Results.Accepted(),
            JobCancelOutcome.NotFound => Results.NotFound(new { error = "job not found" }),
            _ => Results.Conflict(new { error = "job already finished" }),
        });

        // 提权交接：仅在未提权实例、已通过五层 API 防护的前端请求下启动 runas 子进程；
        // 子进程沿用端口和 token，旧实例在响应发出后退出，浏览器以既有 SSE 退避重连。
        app.MapPost("/api/elevate", (KleanerWebHostOptions options, LoopbackSecurityOptions security, IHostApplicationLifetime lifetime) =>
        {
            if (HostRuntime.IsElevated(options))
                return Results.Conflict(new { error = "当前已是管理员权限" });

            var restart = options.ElevationRestart ?? ElevationRestart.Start;
            if (!restart(options.Port, security.Token))
                return Results.Conflict(new { error = "管理员授权已取消或启动失败" });

            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                lifetime.StopApplication();
            });
            return Results.Accepted();
        });

        // 工具箱：只读分析全部进通用 job 体系，可经 /api/jobs/{id}/cancel 取消；不产生隔离区或历史记录。
        app.MapPost("/api/tools/large-files", (LargeFilesRequest request, IToolboxJobService tools) =>
        {
            if (string.IsNullOrWhiteSpace(request.Root) || request.MinBytes <= 0 || request.Top is < 1 or > 200)
                return Results.BadRequest(new { error = "root、minBytes 与 top 参数不合法" });
            var job = tools.Start(request with { Root = request.Root.Trim() });
            return Results.Accepted($"/api/jobs/{job.JobId}", new { jobId = job.JobId });
        });

        app.MapPost("/api/tools/duplicates", (DuplicatesRequest request, IToolboxJobService tools) =>
        {
            if (string.IsNullOrWhiteSpace(request.Root) || request.MinBytesPerFile <= 0)
                return Results.BadRequest(new { error = "root 与 minBytesPerFile 参数不合法" });
            var job = tools.Start(request with { Root = request.Root.Trim() });
            return Results.Accepted($"/api/jobs/{job.JobId}", new { jobId = job.JobId });
        });

        app.MapPost("/api/tools/usage", (UsageRequest request, IToolboxJobService tools) =>
        {
            if (string.IsNullOrWhiteSpace(request.Root))
                return Results.BadRequest(new { error = "root 参数不合法" });
            var job = tools.Start(request with { Root = request.Root.Trim() });
            return Results.Accepted($"/api/jobs/{job.JobId}", new { jobId = job.JobId });
        });

        // 系统大件只提供原有的系统工具指引；WebHost 不执行命令，也不把这些项目纳入清理规则。
        app.MapGet("/api/tools/system-guide", () => Results.Json(SystemToolGuide.Items));

        // 单一多路复用 SSE：fetch 流式（原生 EventSource 无法带 token 头，工单 07），
        // 过既有 token/Origin 中间件；断连只结束本连接、不取消任何 job（工单 07），无 Last-Event-ID 回放。
        app.MapGet("/api/events", async (HttpContext context, IJobEventBus bus) =>
        {
            var response = context.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";

            using var subscription = bus.Subscribe();

            // 连接确认事件：客户端读到它即知道流已建立（token 已过中间件校验）
            await response.WriteAsync(
                $"event: connected\ndata: {{\"serverTime\":\"{DateTimeOffset.UtcNow:o}\"}}\n\n",
                context.RequestAborted);
            await response.Body.FlushAsync(context.RequestAborted);

            try
            {
                while (await subscription.Reader.WaitToReadAsync(context.RequestAborted))
                {
                    while (subscription.Reader.TryRead(out var evt))
                    {
                        var payload = JsonSerializer.Serialize(evt.Data, evt.Data.GetType());
                        await response.WriteAsync(
                            $"event: {evt.Event}\ndata: {payload}\n\n", context.RequestAborted);
                    }

                    await response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                // 断连（关标签/网络断）：任务照跑（工单 07），仅结束本连接
            }
        });

        // ── 清理主链路（工单 12）：scan job → plan（dry-run）→ confirm（执行）──────────────
        // REST 资源风（工单 04）；删除闸映射为计划资源的确认状态（工单 03 第 5 层）。
        // confirm 不进 job 取消体系——确认后无取消端点可达，一次 confirm = 一个批次。

        // 扫描：加载生效规则集（用户覆盖 → 内置）起后台 job（走工单 11）。
        // 同一时间只允许一个扫描在跑（对齐 GUI「扫描中不可再扫」语义）。
        app.MapPost("/api/scan", (KleanerWebHostOptions options, IScanJobService scans, JobRegistry jobs) =>
        {
            RuleSet set;
            try
            {
                set = HostRuntime.ResolveRuleSet(options);
            }
            catch (Exception ex)
            {
                return Results.Problem($"规则加载失败：{ex.Message}");
            }

            if (jobs.Snapshot().Any(j => j.Kind == "scan" && j.Status is "Running" or "Cancelling"))
            {
                return Results.Conflict(new { error = "已有扫描在进行" });
            }

            var job = scans.Start(set);
            return Results.Accepted($"/api/jobs/{job.JobId}", new { jobId = job.JobId });
        });

        // 计划（dry-run）：勾选 id + 已完成的扫描结果 → plan + 一次性 confirmToken。
        // 未知规则 id / 空勾选集一律 400（不复刻 CLI「--rule 缺省静默成功」的坑）。
        app.MapPost("/api/plans", (PlanRequest request, JobRegistry jobs, PlanRegistry plans, KleanerWebHostOptions options) =>
        {
            var job = jobs.Get(request.JobId);
            if (job is null)
            {
                return Results.NotFound(new { error = "job not found" });
            }

            if (job.Kind != "scan" || job.Status != JobStatus.Completed || job.Result is not ScanResultEnvelope scan)
            {
                return Results.Conflict(new { error = "扫描 job 未完成或不可用，无法生成计划" });
            }

            CleanPlan plan;
            try
            {
                plan = CleanPlanService.Build(request.RuleIds, scan, HostRuntime.IsElevated(options));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var record = plans.Create(plan);
            return Results.Json(record.ToView(includeToken: true), statusCode: StatusCodes.Status201Created);
        });

        // 计划查询：confirmToken 不回显（只在创建响应中出现一次）。
        app.MapGet("/api/plans/{planId}", (string planId, PlanRegistry plans) =>
            plans.Get(planId) is { } record
                ? Results.Json(record.ToView(includeToken: false))
                : Results.NotFound(new { error = "plan not found" }));

        // 确认执行（删除闸）：凭据校验 → 烧毁 → QuarantineManager.Execute（移入隔离区 + 落 clean 历史）。
        // 需提权的计划在本进程内拒绝执行（409）；前端走工单 03 的提权重启 + 重连约定后重新出计划。
        app.MapPost("/api/plans/{planId}/confirm", async (string planId, ConfirmRequest request, PlanRegistry plans, KleanerWebHostOptions options) =>
        {
            var record = plans.Get(planId);
            if (record is null)
            {
                return Results.NotFound(new { error = "plan not found" });
            }

            var outcome = record.TryConsume(request.ConfirmToken ?? string.Empty);
            switch (outcome)
            {
                case ConfirmOutcome.BadToken:
                    return Results.Json(new { error = "confirmToken 无效或缺失" }, statusCode: StatusCodes.Status403Forbidden);
                case ConfirmOutcome.AlreadyConfirmed:
                    return Results.Conflict(new { error = "计划已确认执行过，confirmToken 一次性" });
            }

            if (record.Plan.NeedsElevation)
            {
                return Results.Conflict(new { error = "计划包含需要管理员权限的规则，请以管理员身份重启后再试" });
            }

            var executor = options.CleanExecutor ?? (items => ExecuteQuarantine(items, options));
            ExecutionReport report;
            try
            {
                report = await Task.Run(() => executor(record.Plan.Resolved));
            }
            catch (Exception ex)
            {
                // token 已烧毁：执行失败也禁止凭据重放，前端重新出计划（安全优先于便利）
                return Results.Problem($"执行失败：{ex.Message}");
            }

            return Results.Json(report);
        });

        // ── 隔离区 / 历史 / 启动项（工单 13）：删除路径对照清单相关资源 ────────────────────
        // 写路径只经 QuarantineManager / StartupManager（四道保险；WebHost 不新增任何 File.Delete 直调）。
        // 删除批次 / 清空的 UI 二次确认是前端产品语义（工单 17）；服务端凭据闸（03 第 5 层）只覆盖 clean apply。

        // 批次列表：manifest 逐个解析，缺失/损坏的批次被 ListBatches 静默跳过（GUI 同语义）。
        app.MapGet("/api/quarantine/batches", (KleanerWebHostOptions options) =>
            Results.Json(HostRuntime.ResolveQuarantine(options)
                .ListBatches().Select(QuarantineBatchView.From).ToList()));

        // 整批还原：manifest 缺失 → 404；损坏 → 明确错误而非异常外溢（deletion-path.md 记录的 RestoreBatch 无 try-catch 坑）。
        // 原路径有同名 → {原路径}.restore-{batchId}，绝不覆盖（QuarantineManager 语义）。
        app.MapPost("/api/quarantine/batches/{batchId}/restore", (string batchId, KleanerWebHostOptions options) =>
        {
            var manager = HostRuntime.ResolveQuarantine(options);
            if (GuardBatchId(batchId, manager.Root) is { } guard)
            {
                return guard;
            }

            if (!File.Exists(Path.Combine(manager.Root, batchId, "manifest.json")))
            {
                return Results.NotFound(new { error = "批次不存在或 manifest 缺失" });
            }

            try
            {
                var restored = manager.RestoreBatch(batchId);
                return Results.Json(new { batchId, restored });
            }
            catch (JsonException ex)
            {
                return Results.Problem($"批次 manifest 损坏，无法还原：{ex.Message}");
            }
            catch (Exception ex)
            {
                return Results.Problem($"还原失败：{ex.Message}");
            }
        });

        // 删除单批：永久删除路径（隔离区唯一真实删除出口 TryDeleteDir，仅经 QuarantineManager）。
        app.MapDelete("/api/quarantine/batches/{batchId}", (string batchId, KleanerWebHostOptions options) =>
        {
            var manager = HostRuntime.ResolveQuarantine(options);
            if (GuardBatchId(batchId, manager.Root) is { } guard)
            {
                return guard;
            }

            if (!Directory.Exists(Path.Combine(manager.Root, batchId)))
            {
                return Results.NotFound(new { error = "批次不存在" });
            }

            manager.DeleteBatch(batchId);
            return Results.NoContent();
        });

        // 清空 7 天前批次：仅显式触发（手动清空策略，不自动删）；固定 7 天与 GUI 对等。
        app.MapPost("/api/quarantine/purge", (KleanerWebHostOptions options) =>
        {
            var purged = HostRuntime.ResolveQuarantine(options).PurgeOlderThan(TimeSpan.FromDays(7));
            return Results.Json(new { purged });
        });

        // 历史只读：最近 limit 条（新的在前），单行损坏已被 HistoryManager.Recent 容忍。
        app.MapGet("/api/history", (int? limit, KleanerWebHostOptions options) =>
            Results.Json(HostRuntime.ResolveHistory(options).Recent(Math.Clamp(limit ?? 200, 1, 1000))));

        // 启动项：启用 + 已禁用备份统一呈现（StartupWindow.Reload 同语义）。
        app.MapGet("/api/startup", (KleanerWebHostOptions options) =>
        {
            var manager = HostRuntime.ResolveStartup(options);
            return Results.Json(new
            {
                enabled = manager.Enumerate().Select(StartupItemView.From).ToList(),
                disabled = manager.ListDisabled().Select(DisabledStartupView.From).ToList(),
                elevated = HostRuntime.IsElevated(options),
            });
        });

        // 禁用：服务端按 id 重新枚举定位目标（不接受客户端传入的目标位置）；HKLM 项经 StartupManager 内部
        // reg.exe + runas 提权（UAC 取消或失败抛异常 → 409，备份记录已回滚，语义不变）。startup-disable 历史由 manager 落。
        app.MapPost("/api/startup/disable", (StartupIdRequest request, KleanerWebHostOptions options) =>
        {
            if (string.IsNullOrWhiteSpace(request.Id))
            {
                return Results.BadRequest(new { error = "缺少启动项 id" });
            }

            var manager = HostRuntime.ResolveStartup(options);
            var item = manager.Enumerate().FirstOrDefault(i => i.Id == request.Id);
            if (item is null)
            {
                return Results.NotFound(new { error = "启动项不存在或已被禁用" });
            }

            try
            {
                manager.Disable(item);
                return Results.Json(new { id = item.Id, disabled = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"禁用失败：{ex.Message}");
            }
        });

        // 还原：404 无备份记录；目标位置被占用等 → 409（保留备份不覆盖）。startup-restore 历史由 manager 落。
        app.MapPost("/api/startup/restore", (StartupIdRequest request, KleanerWebHostOptions options) =>
        {
            if (string.IsNullOrWhiteSpace(request.Id))
            {
                return Results.BadRequest(new { error = "缺少启动项 id" });
            }

            var manager = HostRuntime.ResolveStartup(options);
            try
            {
                manager.Restore(request.Id);
                return Results.Json(new { id = request.Id, restored = true });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"还原失败：{ex.Message}");
            }
        });

        // PWA 壳：未知的前端路由回到 index.html；API 路由已全部在此前注册，仍受安全中间件保护。
        app.MapFallbackToFile("index.html");

        return app;
    }

    /// <summary>batchId 路径安全闸：阻断 .. / 分隔符 / 盘符逃出隔离区根（端点直收客户端字符串，Root 之上的目录边界必须自守）。</summary>
    private static IResult? GuardBatchId(string batchId, string root)
    {
        var full = Path.GetFullPath(Path.Combine(root, batchId));
        if (batchId.Contains(':') ||
            (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest(new { error = "batchId 不合法" });
        }

        return null;
    }

    /// <summary>真实执行路径：全仓唯一出口 QuarantineManager.Execute（File.Move 移入隔离区）+ HistoryManager 落 clean 历史。</summary>
    private static ExecutionReport ExecuteQuarantine(IReadOnlyList<PlanResolvedItem> items, KleanerWebHostOptions options) =>
        new QuarantineManager(HostRuntime.ResolveQuarantineRoot(options), new HistoryManager())
            .Execute(items.SelectMany(item => item.Files.Select(file => (item.RuleId, file))));
}
