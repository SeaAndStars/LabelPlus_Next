using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LabelPlus_Next.ViewModels;

public partial class MergeConflictViewModel : ObservableObject
{
    [ObservableProperty] private string title = "保存冲突合并";
    [ObservableProperty] private string fileName = "translation.txt";
    [ObservableProperty] private string remoteLabel = "远端";
    [ObservableProperty] private string localLabel = "本地";
    [ObservableProperty] private string remoteText = string.Empty;
    [ObservableProperty] private string localText = string.Empty;
    [ObservableProperty] private string mergedText = string.Empty;
    [ObservableProperty] private string conflictStatus = string.Empty;
    // 当前冲突在合并文本中的字符起始与长度，用于视图选择与滚动
    [ObservableProperty] private int currentStart;
    [ObservableProperty] private int currentLength;

    private const string StartMarker = "<<<<<<< REMOTE";
    private const string MidMarker = "=======";
    private const string EndMarker = ">>>>>>> LOCAL";
    private int _currentConflict = 0;
    private int _totalConflicts = 0;

    public event EventHandler<string?>? RequestClose;

    public MergeConflictViewModel() { }
    public MergeConflictViewModel(string remote, string local, string fileName)
    {
        this.fileName = fileName;
        remoteText = remote;
        localText = local;
        BuildDiff();
    }

    [RelayCommand]
    private void UseRemote() => MergedText = RemoteText;

    [RelayCommand]
    private void UseLocal() => MergedText = LocalText;

    [RelayCommand]
    private void SaveMerged() => RequestClose?.Invoke(this, MergedText);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, null);

    [RelayCommand]
    private void BuildDiff()
    {
        var merged = BuildMergedWithMarkers(RemoteText ?? string.Empty, LocalText ?? string.Empty);
        MergedText = merged;
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void NextConflict()
    {
        if (_totalConflicts == 0) return;
        _currentConflict = Math.Min(_currentConflict + 1, _totalConflicts);
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void PrevConflict()
    {
        if (_totalConflicts == 0) return;
        _currentConflict = Math.Max(_currentConflict - 1, 1);
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void AcceptAllRemote()
    {
        MergedText = ApplyAll(MergedText, keepRemote: true);
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void AcceptAllLocal()
    {
        MergedText = ApplyAll(MergedText, keepRemote: false);
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void AcceptCurrentRemote() => AcceptCurrent(true);

    [RelayCommand]
    private void AcceptCurrentLocal() => AcceptCurrent(false);

    private void AcceptCurrent(bool keepRemote)
    {
        if (_totalConflicts == 0 || _currentConflict < 1) return;
        MergedText = ApplyNth(MergedText, _currentConflict, keepRemote);
        // After resolving current, conflict count might reduce; keep index within range
        var (cnt, _) = CountConflicts(MergedText);
        _totalConflicts = cnt;
        if (_totalConflicts == 0) _currentConflict = 0; else _currentConflict = Math.Min(_currentConflict, _totalConflicts);
        UpdateConflictsStatus();
    }

    private void UpdateConflictsStatus()
    {
        var (cnt, _) = CountConflicts(MergedText);
        _totalConflicts = cnt;
        if (_totalConflicts == 0)
        {
            _currentConflict = 0;
            ConflictStatus = "无冲突";
            CurrentStart = 0;
            CurrentLength = 0;
        }
        else
        {
            if (_currentConflict < 1) _currentConflict = 1;
            ConflictStatus = $"冲突 {_currentConflict}/{_totalConflicts}";
            // 计算当前冲突的选择范围
            var (start, length) = GetConflictRange(MergedText, _currentConflict);
            CurrentStart = start;
            CurrentLength = length;
        }
    }

    private static string NormalizeNewLines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string BuildMergedWithMarkers(string remote, string local)
    {
        var a = NormalizeNewLines(remote).Split('\n');
        var b = NormalizeNewLines(local).Split('\n');
        int n = a.Length, m = b.Length;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var sb = new System.Text.StringBuilder();
        int ia = 0, ib = 0;
        while (ia < n && ib < m)
        {
            if (a[ia] == b[ib])
            {
                sb.Append(a[ia]).Append('\n');
                ia++; ib++;
            }
            else
            {
                // gather differing block
                int sa = ia, sbj = ib;
                while (ia < n && ib < m && a[ia] != b[ib])
                {
                    // advance the side with larger lcs ahead
                    if (lcs[ia + 1, ib] >= lcs[ia, ib + 1]) ia++; else ib++;
                }
                var remoteChunk = string.Join('\n', a[sa..ia]);
                var localChunk = string.Join('\n', b[sbj..ib]);
                if (remoteChunk.Length > 0 || localChunk.Length > 0)
                {
                    sb.AppendLine(StartMarker);
                    if (remoteChunk.Length > 0) sb.Append(remoteChunk).Append('\n');
                    sb.AppendLine(MidMarker);
                    if (localChunk.Length > 0) sb.Append(localChunk).Append('\n');
                    sb.AppendLine(EndMarker);
                }
            }
        }
        // tail
        if (ia < n || ib < m)
        {
            var remoteTail = string.Join('\n', a[ia..n]);
            var localTail = string.Join('\n', b[ib..m]);
            if (remoteTail.Length > 0 || localTail.Length > 0)
            {
                sb.AppendLine(StartMarker);
                if (remoteTail.Length > 0) sb.Append(remoteTail).Append('\n');
                sb.AppendLine(MidMarker);
                if (localTail.Length > 0) sb.Append(localTail).Append('\n');
                sb.AppendLine(EndMarker);
            }
        }
        return sb.ToString();
    }

    private static (int count, List<int> positions) CountConflicts(string text)
    {
        var pos = new List<int>();
        int idx = 0;
        while (true)
        {
            var i = text.IndexOf(StartMarker, idx, StringComparison.Ordinal);
            if (i < 0) break;
            pos.Add(i);
            idx = i + StartMarker.Length;
        }
        return (pos.Count, pos);
    }

    private static string ApplyAll(string text, bool keepRemote)
    {
        int idx = 0;
        while (true)
        {
            var i = text.IndexOf(StartMarker, idx, StringComparison.Ordinal);
            if (i < 0) break;
            var (j, k) = FindMarkers(text, i);
            if (j < 0 || k < 0) break;
            var (remoteChunk, localChunk) = ExtractChunks(text, i, j, k);
            var replacement = keepRemote ? remoteChunk : localChunk;
            text = text.Substring(0, i) + replacement + text.Substring(k + EndMarker.Length);
            idx = i + replacement.Length;
        }
        return text;
    }

    private static string ApplyNth(string text, int nth, bool keepRemote)
    {
        int idx = 0;
        int seen = 0;
        while (true)
        {
            var i = text.IndexOf(StartMarker, idx, StringComparison.Ordinal);
            if (i < 0) break;
            seen++;
            var (j, k) = FindMarkers(text, i);
            if (j < 0 || k < 0) break;
            if (seen == nth)
            {
                var (remoteChunk, localChunk) = ExtractChunks(text, i, j, k);
                var replacement = keepRemote ? remoteChunk : localChunk;
                return text.Substring(0, i) + replacement + text.Substring(k + EndMarker.Length);
            }
            idx = k + EndMarker.Length;
        }
        return text;
    }

    private static (int midPos, int endPos) FindMarkers(string text, int startPos)
    {
        var j = text.IndexOf('\n' + MidMarker + '\n', startPos, StringComparison.Ordinal);
        if (j < 0) j = text.IndexOf(MidMarker, startPos, StringComparison.Ordinal);
        var k = text.IndexOf('\n' + EndMarker, j + 1, StringComparison.Ordinal);
        if (k < 0) k = text.IndexOf(EndMarker, j + 1, StringComparison.Ordinal);
        return (j, k);
    }

    private static (string remoteChunk, string localChunk) ExtractChunks(string text, int startPos, int midPos, int endPos)
    {
        var remStart = startPos + StartMarker.Length;
        // skip newline if exists
        if (remStart < text.Length && text[remStart] == '\n') remStart++;
        var remoteChunk = text.Substring(remStart, Math.Max(0, midPos - remStart));
        var locStart = midPos + MidMarker.Length;
        if (locStart < text.Length && text[locStart] == '\n') locStart++;
        var localChunk = text.Substring(locStart, Math.Max(0, endPos - locStart));
        return (remoteChunk, localChunk);
    }

    private static (int start, int length) GetConflictRange(string text, int nth)
    {
        int idx = 0;
        int seen = 0;
        while (true)
        {
            var i = text.IndexOf(StartMarker, idx, StringComparison.Ordinal);
            if (i < 0) break;
            seen++;
            var (j, k) = FindMarkers(text, i);
            if (j < 0 || k < 0) break;
            if (seen == nth)
            {
                var end = k + EndMarker.Length;
                var len = Math.Max(0, end - i);
                return (i, len);
            }
            idx = k + EndMarker.Length;
        }
        return (0, 0);
    }
}
