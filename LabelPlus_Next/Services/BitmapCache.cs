using Avalonia.Media.Imaging;
using NLog;

namespace LabelPlus_Next.Services;

/// <summary>
/// Small LRU cache of decoded page bitmaps. Decoding runs on the thread pool so switching pages never blocks the UI,
/// the next page can be prefetched, and evicted bitmaps are disposed so large scans don't pile up in native memory.
/// </summary>
public sealed class BitmapCache : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly int _capacity;
    private readonly LinkedList<(string Path, Task<Bitmap?> Task)> _lru = new();
    private readonly Dictionary<string, LinkedListNode<(string Path, Task<Bitmap?> Task)>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private string? _pinned;    // most recently requested page
    private string? _displayed; // page currently bound to the view (may differ while the next one decodes)

    public BitmapCache(int capacity = 3) => _capacity = Math.Max(2, capacity);

    /// <summary>Returns the bitmap for <paramref name="path"/> and pins it so it is never disposed while displayed.</summary>
    public Task<Bitmap?> GetAsync(string path)
    {
        lock (_gate)
        {
            _pinned = path;
            return GetOrAddLocked(path);
        }
    }

    /// <summary>Marks the bitmap that is actually on screen so eviction never disposes it.</summary>
    public void MarkDisplayed(string? path)
    {
        lock (_gate)
        {
            _displayed = path;
            EvictLocked();
        }
    }

    public void Prefetch(string path)
    {
        lock (_gate)
        {
            if (_map.ContainsKey(path)) return;
            // Prefetched entries are inserted as least-recently-used so they never push out the displayed page
            _ = GetOrAddLocked(path, asOldest: true);
        }
    }

    private Task<Bitmap?> GetOrAddLocked(string path, bool asOldest = false)
    {
        if (_map.TryGetValue(path, out var node))
        {
            if (!asOldest)
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            return node.Value.Task;
        }

        var task = Task.Run(() => Decode(path));
        node = asOldest ? _lru.AddLast((path, task)) : _lru.AddFirst((path, task));
        _map[path] = node;
        EvictLocked();
        return task;
    }

    private void EvictLocked()
    {
        var cur = _lru.Last;
        while (_lru.Count > _capacity && cur is not null)
        {
            var prev = cur.Previous;
            if (!string.Equals(cur.Value.Path, _pinned, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(cur.Value.Path, _displayed, StringComparison.OrdinalIgnoreCase))
            {
                _lru.Remove(cur);
                _map.Remove(cur.Value.Path);
                DisposeWhenDone(cur.Value.Task);
            }
            cur = prev;
        }
    }

    private static Bitmap? Decode(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            return new Bitmap(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Logger.Warn(ex, "Failed to decode bitmap {path}", path);
            return null;
        }
    }

    private static void DisposeWhenDone(Task<Bitmap?> task)
        => task.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result?.Dispose(); }, TaskScheduler.Default);

    /// <summary>Disposes every cached bitmap; only call once nothing is bound to the view.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var n in _lru) DisposeWhenDone(n.Task);
            _lru.Clear();
            _map.Clear();
            _pinned = null;
            _displayed = null;
        }
    }

    public void Dispose() => Clear();
}
