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

        var app = new App();
        app.Run(new MainWindow());
    }
}
