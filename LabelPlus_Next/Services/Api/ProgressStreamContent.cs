using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// HttpContent that streams from a source stream and reports progress.
/// </summary>
internal sealed class ProgressStreamContent : HttpContent
{
    private readonly Stream _source;
    private readonly int _bufferSize;
    private readonly long? _totalLength;
    private readonly Action<long, long?, TimeSpan> _onProgress;

    public ProgressStreamContent(Stream source, int bufferSize, Action<long, long?, TimeSpan> onProgress, long? totalLength = null, string? mediaType = "application/octet-stream")
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bufferSize = Math.Max(8 * 1024, bufferSize);
        _onProgress = onProgress ?? throw new ArgumentNullException(nameof(onProgress));
        _totalLength = totalLength;
        if (!string.IsNullOrEmpty(mediaType))
        {
            Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_totalLength.HasValue)
        {
            length = _totalLength.Value;
            return true;
        }
        length = 0;
        return false;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamCore(stream, CancellationToken.None);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        => SerializeToStreamCore(stream, cancellationToken);

    private async Task SerializeToStreamCore(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[_bufferSize];
        long uploaded = 0;
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        int bytesRead;
        while ((bytesRead = await _source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            uploaded += bytesRead;

            var elapsed = sw.Elapsed;
            if (elapsed - lastReport >= TimeSpan.FromMilliseconds(200))
            {
                _onProgress(uploaded, _totalLength, elapsed);
                lastReport = elapsed;
            }
        }
        _onProgress(uploaded, _totalLength, sw.Elapsed);
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                _source.Dispose();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }
}
