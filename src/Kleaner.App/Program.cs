using System.Windows;
using Velopack;

namespace Kleaner.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack 钩子必须最先运行（处理更新后的重启动与安装器回调）
        VelopackApp.Build().Run();

        // 本地化须在任意窗口/视图模型构造前加载：S.Get 查不到时回退英文键名
        S.Load();

        var app = new App();
        // 加载 App.xaml（BundledTheme + MaterialDesign2.Defaults）；自定义 Main 不会自动执行这一步，
        // 缺了它 Application.Resources 为空，MainWindow 的 StaticResource（如 MaterialDesignRaisedButton）会启动即崩。
        app.InitializeComponent();
        app.Run(new MainWindow());
    }
}
