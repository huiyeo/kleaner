using System.Net;
using System.Net.Sockets;

namespace Kleaner.WebHost;

internal static class PortPicker
{
    /// <summary>
    /// 首选端口能绑就用，被外部进程占用则回退随机高端口（工单 03 决策）。
    /// 探测与 Kestrel 实际绑定之间存在极小竞态窗口，绑定失败属可接受的极端情形。
    /// </summary>
    public static int PickFreePort(int preferred)
    {
        if (CanBind(preferred))
        {
            return preferred;
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool CanBind(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
