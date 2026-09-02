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
        app.Run(new MainWindow());
    }
}
