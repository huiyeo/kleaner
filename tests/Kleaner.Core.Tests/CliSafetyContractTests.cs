using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Kleaner.Core.Tests;

/// <summary>
/// CLI 安全契约（工单 04）：以进程级方式运行 Kleaner.ScanCli 真实入口。
/// 规则、隔离区、历史全部注入临时目录；子进程 stdin 重定向即非交互环境；
/// 测试不触碰真实用户目录、真实隔离区与真实历史文件。
/// </summary>
public sealed class CliSafetyContractTests : IDisposable
{
    private static readonly string CliDll =
        Path.Combine(AppContext.BaseDirectory, "Kleaner.ScanCli.dll");

    private readonly string _root;
    private readonly string _fixtureDir;
    private readonly string _quarantineDir;
    private readonly string _historyFile;
    private readonly string _rulesFile;
    private const string RuleId = "cli-fixture-rule";

    public CliSafetyContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kleaner-cli-tests", Guid.NewGuid().ToString("N"));
        _fixtureDir = Path.Combine(_root, "fixture");
        _quarantineDir = Path.Combine(_root, "quarantine");
        _historyFile = Path.Combine(_root, "history", "history.jsonl");
        Directory.CreateDirectory(_fixtureDir);
        Directory.CreateDirectory(_quarantineDir);

        foreach (var name in new[] { "a.log", "b.log" })
        {
            var p = Path.Combine(_fixtureDir, name);
            File.WriteAllText(p, "x");
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-30));
        }

        _rulesFile = Path.Combine(_root, "rules.json");
        File.WriteAllText(_rulesFile, """
            {
              "schemaVersion": 1,
              "channel": "test",
              "defaults": { "ageDays": 14 },
              "rules": [
                {
                  "id": "cli-fixture-rule",
                  "name": "CLI 测试夹具",
                  "category": "temp",
                  "risk": "low",
                  "paths": ["%KLEANER_CLI_FIXTURE%\\**"],
                  "requiresElevation": false,
                  "safetyNotes": "测试夹具规则：仅指向注入的临时目录，用于验证 CLI 安全契约，不覆盖任何真实用户路径。",
                  "verified": "本机实测"
                }
              ]
            }
            """);
    }

    private (int Code, string StdOut, string StdErr) RunCli(params string[] args)
    {
        Assert.True(File.Exists(CliDll), $"未找到 CLI 产物：{CliDll}（测试工程需引用 Kleaner.ScanCli）");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(CliDll);
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["KLEANER_CLI_FIXTURE"] = _fixtureDir;

        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000))
        {
            p.Kill(true);
            Assert.Fail("CLI 进程执行超时（120 秒）");
        }
        return (p.ExitCode, outTask.Result, errTask.Result);
    }

    private string[] BaseArgs(string subCommand) =>
    [
        subCommand, "--rule", RuleId,
        "--rules", _rulesFile,
        "--quarantine-root", _quarantineDir,
        "--history-path", _historyFile,
    ];

    [Fact]
    public void Scan_只读契约_命中候选但不删除不写历史()
    {
        var (code, stdout, _) = RunCli("scan", "--rules", _rulesFile, "--format", "json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal(2, Directory.EnumerateFiles(_fixtureDir).Count());
        Assert.Empty(Directory.EnumerateFiles(_quarantineDir, "*", SearchOption.AllDirectories));
        Assert.False(File.Exists(_historyFile));
    }

    [Fact]
    public void Clean_无apply_只输出计划退出码0_不删除()
    {
        var (code, stdout, _) = RunCli([.. BaseArgs("clean"), "--format", "json"]);

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("files").GetInt32());
        Assert.Equal(2, Directory.EnumerateFiles(_fixtureDir).Count());
        Assert.False(File.Exists(_historyFile));
    }

    [Fact]
    public void Clean_apply非交互无yes_拒绝执行退出码2()
    {
        var (code, _, stderr) = RunCli([.. BaseArgs("clean"), "--apply"]);

        Assert.Equal(2, code);
        Assert.Contains("--yes", stderr);
        Assert.Equal(2, Directory.EnumerateFiles(_fixtureDir).Count());
        Assert.Empty(Directory.EnumerateFiles(_quarantineDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Clean_未知规则id_退出码1并输出errors()
    {
        var (code, stdout, _) = RunCli("clean", "--rule", "nope", "--rules", _rulesFile, "--format", "json");

        Assert.Equal(1, code);
        using var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.TryGetProperty("errors", out var errors));
        Assert.Contains("nope", errors[0].GetString());
    }

    [Fact]
    public void Clean_apply加yes_移动进注入隔离区并写审计()
    {
        var (code, stdout, _) = RunCli([.. BaseArgs("clean"), "--apply", "--yes", "--format", "json"]);

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("moved").GetInt32());
        Assert.Empty(Directory.EnumerateFiles(_fixtureDir));
        // 隔离区内：2 个被移入文件 + 1 份批次 manifest.json
        var quarantined = Directory.EnumerateFiles(_quarantineDir, "*", SearchOption.AllDirectories).ToList();
        Assert.Equal(2, quarantined.Count(f => !f.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(quarantined, f => f.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(_historyFile));
        Assert.True(new FileInfo(_historyFile).Length > 0, "审计历史应有写入记录");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
