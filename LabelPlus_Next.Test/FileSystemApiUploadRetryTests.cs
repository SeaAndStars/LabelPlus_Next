using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelPlus_Next.Services.Api;

namespace LabelPlus_Next.Test;

/// <summary>
/// Uploads must survive a reset/5xx mid-body (the tools log showed SocketException 10054
/// after ~10s of streaming a 95 MB package) and must not retry permanent rejections
/// such as 413 Request Entity Too Large.
/// </summary>
[TestClass]
public sealed class FileSystemApiUploadRetryTests
{
    [TestMethod]
    public async Task PutMany_RetriesTransientServerError_ThenSucceeds()
    {
        await using var server = new FakeAlistServer(attempt =>
            attempt == 1 ? new PutBehavior(HttpStatusCode.ServiceUnavailable) : new PutBehavior(HttpStatusCode.OK));

        var api = new FileSystemApi(server.BaseUrl);
        var results = await api.PutManyAsync(
            "Bearer test",
            new[] { new FileUploadItem { FilePath = "/test/retry.bin", Content = new byte[1024 * 1024] } },
            progress: null,
            maxConcurrency: 1);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(200, results[0].Code, results[0].Message);
        Assert.AreEqual(2, server.PutAttempts, "the 503 should have been retried exactly once");
    }

    [TestMethod]
    public async Task PutMany_RetriesDroppedConnection_ThenSucceeds()
    {
        await using var server = new FakeAlistServer(attempt =>
            attempt == 1 ? new PutBehavior(HttpStatusCode.OK, Abort: true) : new PutBehavior(HttpStatusCode.OK));

        var api = new FileSystemApi(server.BaseUrl);
        var results = await api.PutManyAsync(
            "Bearer test",
            new[] { new FileUploadItem { FilePath = "/test/reset.bin", Content = new byte[4 * 1024 * 1024] } },
            progress: null,
            maxConcurrency: 1);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(200, results[0].Code, results[0].Message);
        Assert.AreEqual(2, server.PutAttempts, "the reset connection should have been retried exactly once");
    }

    [TestMethod]
    public async Task PutMany_DoesNotRetry413()
    {
        await using var server = new FakeAlistServer(_ => new PutBehavior(HttpStatusCode.RequestEntityTooLarge));

        var api = new FileSystemApi(server.BaseUrl);
        var results = await api.PutManyAsync(
            "Bearer test",
            new[] { new FileUploadItem { FilePath = "/test/too-large.bin", Content = new byte[1024] } },
            progress: null,
            maxConcurrency: 1);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(413, results[0].Code);
        Assert.AreEqual(1, server.PutAttempts, "413 is permanent and must not be retried");
    }

    [TestMethod]
    public async Task PutMany_ReturnsPerFileFailure_InsteadOfThrowing()
    {
        // Every attempt is reset, so the upload must give up with code 0 but still
        // return a result for the caller (and not kill sibling files in the batch).
        await using var server = new FakeAlistServer(_ => new PutBehavior(HttpStatusCode.OK, Abort: true));

        var api = new FileSystemApi(server.BaseUrl);
        var results = await api.PutManyAsync(
            "Bearer test",
            new[] { new FileUploadItem { FilePath = "/test/always-reset.bin", Content = new byte[512 * 1024] } },
            progress: null,
            maxConcurrency: 1);

        Assert.AreEqual(1, results.Count);
        Assert.AreNotEqual(200, results[0].Code, "an aborted upload must never surface as success");
        Assert.IsFalse(string.IsNullOrWhiteSpace(results[0].Message));
        Assert.AreEqual(3, server.PutAttempts, "all three attempts should have been made");
    }

    private sealed record PutBehavior(HttpStatusCode Status, bool Abort = false);

    private sealed class FakeAlistServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Func<int, PutBehavior> _behavior;
        private readonly Task _loop;
        private int _putAttempts;

        public FakeAlistServer(Func<int, PutBehavior> behavior)
        {
            _behavior = behavior;
            var port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _loop = Task.Run(RunAsync);
        }

        public string BaseUrl { get; }

        public int PutAttempts => Volatile.Read(ref _putAttempts);

        private async Task RunAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    break;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath;
                if (string.Equals(path, "/api/fs/get", StringComparison.Ordinal))
                {
                    // Not found: the upload path then skips the backup download.
                    await WriteJsonAsync(context, HttpStatusCode.OK, """{"code":404,"message":"not found","data":null}""");
                    return;
                }

                if (!string.Equals(path, "/api/fs/put", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, HttpStatusCode.NotFound, """{"code":404,"message":"not found"}""");
                    return;
                }

                var attempt = Interlocked.Increment(ref _putAttempts);
                var behavior = _behavior(attempt);
                if (behavior.Abort)
                {
                    // Read a little of the body, then reset the connection while the
                    // client is still streaming. This mirrors the 10054 in the logs.
                    var buffer = new byte[64 * 1024];
                    try { await context.Request.InputStream.ReadExactlyAsync(buffer); }
                    catch (Exception) { /* client may already be gone */ }
                    context.Response.Abort();
                    return;
                }

                await context.Request.InputStream.CopyToAsync(Stream.Null);
                var body = behavior.Status == HttpStatusCode.OK
                    ? """{"code":200,"message":"success","data":null}"""
                    : $$"""{"code":{{(int)behavior.Status}},"message":"{{behavior.Status}}"}""";
                await WriteJsonAsync(context, behavior.Status, body);
            }
            catch (Exception)
            {
                // A failed fake response is reported by the client assertion; keep the loop alive.
            }
        }

        private static async Task WriteJsonAsync(HttpListenerContext context, HttpStatusCode status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
            try { await _loop; }
            catch (Exception) { /* shutting down */ }
            _cts.Dispose();
        }
    }
}
