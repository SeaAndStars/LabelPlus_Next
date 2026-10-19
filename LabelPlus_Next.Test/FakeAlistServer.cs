using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LabelPlus_Next.Test;

/// <summary>
/// Minimal in-process HTTP/1.1 server speaking the small AList subset used by
/// the view models. Uses TcpListener instead of HttpListener so it behaves the
/// same on Linux, Windows and macOS runners.
/// </summary>
public sealed class FakeAlistServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    public ConcurrentQueue<(string Method, string Path)> Requests { get; } = new();
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public string BaseUrl { get; }

    public FakeAlistServer()
    {
        Files["/project.json"] = Encoding.UTF8.GetBytes("{\"projects\":{}}");
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(client), CancellationToken.None);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var headerBytes = await ReadHeadersAsync(stream, _cts.Token);
                if (headerBytes is null) return;
                var headerText = Encoding.UTF8.GetString(headerBytes);
                var lines = headerText.Split("\r\n", StringSplitOptions.None);
                if (lines.Length == 0) return;
                var first = lines[0].Split(' ');
                if (first.Length < 2) return;
                var method = first[0];
                var target = first[1];
                var queryIndex = target.IndexOf('?');
                var path = queryIndex >= 0 ? target[..queryIndex] : target;
                var query = queryIndex >= 0 ? target[queryIndex..] : string.Empty;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 1; i < lines.Length; i++)
                {
                    var idx = lines[i].IndexOf(':');
                    if (idx > 0) headers[lines[i][..idx].Trim()] = lines[i][(idx + 1)..].Trim();
                }
                var contentLength = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var n) ? n : 0;
                if (headers.TryGetValue("Expect", out var expect) && expect.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
                {
                    var cont = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                    await stream.WriteAsync(cont, _cts.Token);
                }
                var body = await ReadExactAsync(stream, contentLength, _cts.Token);
                Requests.Enqueue((method, path + query));
                var (status, contentType, response) = Route(method, path, query, body);
                await WriteResponseAsync(stream, status, contentType, response, _cts.Token);
            }
            catch
            {
                // Client disconnected or malformed request; nothing to report.
            }
        }
    }

    private static async Task<byte[]?> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>(512);
        var tail = 0;
        while (buffer.Count < 65536)
        {
            var one = new byte[1];
            var read = await stream.ReadAsync(one, ct);
            if (read == 0) return null;
            buffer.Add(one[0]);
            tail = one[0] == (byte)'\n' ? tail + 1 : (one[0] == (byte)'\r' ? tail : 0);
            if (tail == 2 && buffer.Count >= 4)
            {
                var span = buffer.ToArray();
                var text = Encoding.UTF8.GetString(span);
                if (text.EndsWith("\r\n\r\n", StringComparison.Ordinal)) return span;
            }
        }
        return null;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken ct)
    {
        var result = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset, length - offset), ct);
            if (read == 0) break;
            offset += read;
        }
        return offset == length ? result : result[..offset];
    }

    private (int Status, string ContentType, byte[] Body) Route(string method, string path, string query, byte[] body)
    {
        if (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
            return Json(200, "{\"code\":200,\"data\":{\"token\":\"fake-token\"}}");

        if (path.Equals("/api/fs/get", StringComparison.OrdinalIgnoreCase))
        {
            var requested = ReadPath(body);
            if (Files.TryGetValue(requested, out var fileBytes))
            {
                var raw = BaseUrl + "/raw?path=" + Uri.EscapeDataString(requested);
                return Json(200, "{\"code\":200,\"data\":{\"name\":\"" + Path.GetFileName(requested) + "\",\"is_dir\":false,\"raw_url\":\"" + raw + "\",\"size\":" + fileBytes.Length + "}}");
            }
            return Json(404, "{\"code\":404,\"message\":\"not found\"}");
        }

        if (path.Equals("/raw", StringComparison.OrdinalIgnoreCase))
        {
            var requested = QueryValue(query, "path") ?? "/";
            return Files.TryGetValue(requested, out var rawBytes)
                ? (200, "application/json", rawBytes)
                : (404, "application/octet-stream", Array.Empty<byte>());
        }

        if (path.Equals("/api/fs/list", StringComparison.OrdinalIgnoreCase))
            return Json(200, "{\"code\":200,\"data\":{\"content\":[],\"total\":0,\"write\":true}}");

        if (path.Equals("/api/fs/search", StringComparison.OrdinalIgnoreCase))
            return Json(200, "{\"code\":200,\"data\":{\"content\":[],\"total\":0}}");

        if (path.Equals("/api/fs/put", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/fs/mkdir", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/fs/copy", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/fs/move", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/fs/remove", StringComparison.OrdinalIgnoreCase))
            return Json(200, "{\"code\":200}");

        return Json(200, "{\"code\":200}");
    }

    private static (int, string, byte[]) Json(int status, string json)
        => (status, "application/json", Encoding.UTF8.GetBytes(json));

    private static string ReadPath(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body.Length == 0 ? "{}" : Encoding.UTF8.GetString(body));
            return doc.RootElement.TryGetProperty("path", out var p) ? p.GetString() ?? "/" : "/";
        }
        catch { return "/"; }
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var k = idx >= 0 ? pair[..idx] : pair;
            if (!k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return idx >= 0 ? Uri.UnescapeDataString(pair[(idx + 1)..]) : string.Empty;
        }
        return null;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string contentType, byte[] body, CancellationToken ct)
    {
        var reason = status switch { 200 => "OK", 404 => "Not Found", _ => "OK" };
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, ct);
        if (body.Length > 0) await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
