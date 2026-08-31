using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Kleaner.WebHost;

/// <summary>安全中间件的期望值（由宿主启动流程在绑定端口、生成 token 后填好）。</summary>
public sealed record LoopbackSecurityOptions
{
    public required int Port { get; init; }

    public required string Token { get; init; }

    /// <summary>Host 头必须恰为 127.0.0.1:&lt;port&gt;（防 DNS rebinding，工单 03 第 2 层）。</summary>
    public string RequiredHost => $"127.0.0.1:{Port}";

    /// <summary>非 GET/HEAD 请求 Origin 必须等于它（防跨站 CSRF，工单 03 第 3 层）。</summary>
    public string AllowedOrigin => $"http://127.0.0.1:{Port}";
}

/// <summary>
/// 服务端五层防护中的中间三层（工单 03；回环绑定是部署形态、删除闸在工单 12）：
/// 1. Host 校验：恰为 127.0.0.1:&lt;port&gt;，否则 400；
/// 2. Origin 校验：非 GET/HEAD 必须带 Origin 且等于本机页面地址，否则 403；
/// 3. 启动 token：/api/* 必须带 X-Kleaner-Token 且与本进程 token 相符，否则 401。
/// 顺序固定：Host → Origin → Token。校验通过后仍走统一的活跃度跟踪。
/// </summary>
internal sealed class LoopbackSecurityMiddleware
{
    private const string TokenHeaderName = "X-Kleaner-Token";

    private readonly RequestDelegate _next;
    private readonly LoopbackSecurityOptions _options;
    private readonly byte[] _expectedTokenBytes;

    public LoopbackSecurityMiddleware(RequestDelegate next, LoopbackSecurityOptions options)
    {
        _next = next;
        _options = options;
        _expectedTokenBytes = Encoding.UTF8.GetBytes(options.Token);
    }

    public async Task InvokeAsync(HttpContext context, IActivityTracker tracker)
    {
        // 任何进来的动静都算活跃——即使是被拒的探测请求
        tracker.Touch();

        if (!IsHostAllowed(context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!IsOriginAllowed(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!IsTokenValid(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        tracker.RequestStarted();
        try
        {
            await _next(context);
        }
        finally
        {
            tracker.RequestEnded();
        }
    }

    private bool IsHostAllowed(HostString host) =>
        string.Equals(host.Value, _options.RequiredHost, StringComparison.OrdinalIgnoreCase);

    private bool IsOriginAllowed(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return true;
        }

        var origin = request.Headers.Origin.ToString();
        return string.Equals(origin, _options.AllowedOrigin, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTokenValid(HttpRequest request)
    {
        // 静态 PWA 壳不要求 token——token 经启动 URL 注入后由前端随 API 请求携带（工单 03 第 4 层）
        if (!request.Path.StartsWithSegments("/api"))
        {
            return true;
        }

        var provided = request.Headers[TokenHeaderName].ToString();
        if (provided.Length == 0)
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(providedBytes, _expectedTokenBytes);
    }
}
