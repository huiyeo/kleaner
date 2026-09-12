using Kleaner.App;
using Kleaner.Core;

namespace Kleaner.Core.Tests;

/// <summary>票 08 无障碍修复：行首复选框的勾选（含屏幕阅读器 AXPress 路径）必须同步行选中，
/// 使安全性说明与 AI 解释的作用对象与用户操作的行一致。</summary>
public sealed class MainWindowViewModelTests
{
    private static Rule Rule(string id, string? verified = null) => new Rule(
        Id: id, Name: id, Category: RuleCategory.System, Risk: RiskLevel.Low,
        Paths: new[] { "%TEMP%\\kleaner-vm-test\\" + id + "\\**" }, Exclude: Array.Empty<string>(),
        AgeDays: 14, KeepNewest: null, RequiresElevation: false, Enabled: true,
        SafetyNotes: "视图模型测试规则，说明长度超过二十个字。", Verified: verified);

    [Fact]
    public void 勾选规则行时同步行选中()
    {
        var vm = new MainWindowViewModel();
        var row = new RuleRow(Rule("vm-sync"));
        vm.AddRow(row);

        Assert.Null(vm.SelectedRow);
        row.IsSelected = true;

        Assert.Same(row, vm.SelectedRow);
    }

    [Fact]
    public void 取消勾选同样保持该行为选中()
    {
        var vm = new MainWindowViewModel();
        var row = new RuleRow(Rule("vm-unsync"));
        vm.AddRow(row);
        row.IsSelected = true;

        row.IsSelected = false;

        Assert.Same(row, vm.SelectedRow);
    }

    [Fact]
    public void 加入行时的默认勾选不抢占行选中()
    {
        var vm = new MainWindowViewModel();
        var verified = new RuleRow(Rule("vm-default-checked", verified: "本机实测 2026-09-12（测试）"));

        vm.AddRow(verified);

        Assert.True(verified.IsSelected, "本机实测规则应默认勾选");
        Assert.Null(vm.SelectedRow);
    }
}
