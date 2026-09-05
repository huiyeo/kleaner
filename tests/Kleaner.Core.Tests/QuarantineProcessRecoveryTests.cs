using System.Diagnostics;
using System.Runtime.InteropServices;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class QuarantineProcessRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-crash-" + Guid.NewGuid().ToString("N"));

    public QuarantineProcessRecoveryTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("clean-moved")]
    [InlineData("restore-moved")]
    public async Task 进程在移动后退出仍能核对并恢复全部文件(string phase)
    {
        var worker = Path.Combine(AppContext.BaseDirectory, "crash-worker", "Kleaner.CrashWorker.dll");
        Assert.True(File.Exists(worker), "测试构建必须复制独立进程夹具");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet))
            dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { worker, phase, _root }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail("测试子进程未在 30 秒内到达中断点");
        }
        Assert.True(process.ExitCode == 73, $"退出码 {process.ExitCode}；{await stdout}；{await stderr}");

        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var manager = new QuarantineManager(quarantine, history);
        var batch = Assert.Single(manager.ListBatches());
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "source")));
        Assert.Single(batch.Entries, entry => File.Exists(entry.QuarantinedPath));
        Assert.Equal(phase == "clean-moved" ? 1 : 2, batch.Entries.Count);
        Assert.Equal(phase == "clean-moved" ? "pending" : "restoring", batch.Entries[0].State);
        var action = phase == "clean-moved" ? "clean" : "restore";
        Assert.Contains(history.Recent(), entry => entry.Action == action + "-start");
        Assert.DoesNotContain(history.Recent(), entry => entry.Action == action);
        var temporary = Assert.Single(Directory.GetFiles(quarantine, "*.tmp", SearchOption.AllDirectories));
        var evidence = File.ReadAllBytes(temporary);

        var recovered = manager.RestoreBatch(batch.BatchId);

        Assert.Equal(batch.Entries.Count, recovered.RestoredCount);
        Assert.Empty(recovered.Skipped);
        foreach (var file in new[] { "a.txt", "b.txt" })
            Assert.Equal(file + "-content", File.ReadAllText(Path.Combine(_root, "source", file)));
        // 退出绕过 finally 留下临时清单；未知内容必须保留，不得伪报批次收尾成功。
        Assert.False(recovered.IsComplete);
        Assert.NotEmpty(recovered.Failed);
        Assert.Equal(evidence, File.ReadAllBytes(temporary));
        Assert.Empty(Assert.Single(manager.ListBatches()).Entries);
        Assert.Contains(history.Recent(), entry => entry.Action == "restore" && entry.Result == "partial");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
