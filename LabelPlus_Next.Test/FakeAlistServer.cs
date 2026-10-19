using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LabelPlus_Next.Test;

/// <summary>Minimal in-process AList-compatible HTTP server for VM integration tests.</summary>
public sealed class FakeAlistServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    public ConcurrentQueue<(string Method, string Path)> Requests { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public string BaseUrl { get; }

    public FakeAlistServer()
    {
        Files["/project.json"] = Encoding.UTF8.GetBytes("{\"projects\":{}}");
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(ctx), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";
        Requests.Enqueue((req.HttpMethod, path + (string.IsNullOrEmpty(req.Url?.Query) ? "" : req.Url.Query)));
        try
        {
            if (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(ctx, 200, "{\"code\":200,\"data\":{\"token\":\"fake-token\"}}");
            }
            else if (path.Equals("/api/fs/get", StringComparison.OrdinalIgnoreCase))
            {
                var body = await ReadBodyAsync(req);
                var requested = ReadPath(body);
                if (Files.TryGetValue(requested, out var fileBytes))
                {
                    var raw = BaseUrl + "/raw?path=" + Uri.EscapeDataString(requested);
                    await WriteJsonAsync(ctx, 200, "{\"code\":200,\"data\":{\"name\":\"" + Path.GetFileName(requested) + "\",\"is_dir\":false,\"raw_url\":\"" + raw + "\",\"size\":" + fileBytes.Length + "}}");
                }
                else
                {
                    await WriteJsonAsync(ctx, 404, "{\"code\":404,\"message\":\"not found\"}");
                }
            }
            else if (path.Equals("/raw", StringComparison.OrdinalIgnoreCase))
            {
                var query = req.Url?.Query ?? "";
                var marker = "path=";
                var idx = query.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                var requested = idx >= 0 ? Uri.UnescapeDataString(query[(idx + marker.Length)..].Split('&')[0]) : "/";
                if (Files.TryGetValue(requested, out var rawBytes))
                    await WriteBytesAsync(ctx, 200, rawBytes, "application/json");
                else
                    await WriteBytesAsync(ctx, 404, Array.Empty<byte>());
            }
            else if (path.Equals("/api/fs/list", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(ctx, 200, "{\"code\":200,\"data\":{\"content\":[],\"total\":0,\"write\":true}}");
            }
            else if (path.Equals("/api/fs/search", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(ctx, 200, "{\"code\":200,\"data\":{\"content\":[],\"total\":0}}");
            }
            else if (path.Equals("/api/fs/put", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("/api/fs/mkdir", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("/api/fs/copy", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("/api/fs/move", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("/api/fs/remove", StringComparison.OrdinalIgnoreCase))
            {
                // Drain any body so clients can finish writing.
                await ReadBodyAsync(req);
                await WriteJsonAsync(ctx, 200, "{\"code\":200}");
            }
            else
            {
                await WriteJsonAsync(ctx, 200, "{\"code\":200}");
            }
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { }
        }
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
    {
        if (!req.HasEntityBody) return string.Empty;
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static string ReadPath(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            return doc.RootElement.TryGetProperty("path", out var p) ? p.GetString() ?? "/" : "/";
        }
        catch { return "/"; }
    }

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int status, string json)
        => await WriteBytesAsync(ctx, status, Encoding.UTF8.GetBytes(json), "application/json");

    private static async Task WriteBytesAsync(HttpListenerContext ctx, int status, byte[] bytes, string contentType = "application/octet-stream")
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        if (bytes.Length > 0) await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
