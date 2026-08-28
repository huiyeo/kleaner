namespace Kleaner.SpecialOps;

public sealed record SystemToolItem(string Title, string Command, string Note, bool RequiresAdmin);

/// <summary>系统大件引导操作：只负责展示与"以管理员启动系统自带工具"，Kleaner 自身不直接改动这些系统项。</summary>
public static class SystemToolGuide
{
    public static readonly IReadOnlyList<SystemToolItem> Items = new[]
    {
        new SystemToolItem(
            "关闭休眠（可释放约等于物理内存大小的 hiberfil.sys）",
            "powercfg /h off",
            "重新开启：powercfg /h on。关闭后将无法使用休眠与快速启动。",
            RequiresAdmin: true),
        new SystemToolItem(
            "系统还原点管理",
            "SystemPropertiesProtection.exe",
            "打开系统自带对话框，在「系统保护」页配置或删除还原点；删除不可逆。",
            RequiresAdmin: false),
        new SystemToolItem(
            "WinSxS 组件存储清理",
            "Dism.exe /Online /Cleanup-Image /StartComponentCleanup",
            "系统自带 DISM 清理，耗时较长（10-30 分钟），期间请勿关机。",
            RequiresAdmin: true),
    };
}
