using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;

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
    // 结构化冲突视图
    public enum ChoiceOption { None, Remote, Local }
    public partial class ConflictItem : ObservableObject
    {
        [ObservableProperty] private int index;
        [ObservableProperty] private string remote = string.Empty;
        [ObservableProperty] private string local = string.Empty;
        [ObservableProperty] private ChoiceOption choice;
    public MergeConflictViewModel? Owner { get; set; }
    [RelayCommand] private void ChooseRemoteSelf() { Choice = ChoiceOption.Remote; Owner?.OnItemChoiceChanged(this); }
    [RelayCommand] private void ChooseLocalSelf() { Choice = ChoiceOption.Local; Owner?.OnItemChoiceChanged(this); }
    public string Summary => (Remote ?? string.Empty).Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
    public int RemoteLines => string.IsNullOrEmpty(Remote) ? 0 : Remote.Split('\n').Length;
    public int LocalLines => string.IsNullOrEmpty(Local) ? 0 : Local.Split('\n').Length;
    }
    [ObservableProperty] private ObservableCollection<ConflictItem> conflicts = new();
    [ObservableProperty] private int selectedConflictIndex;
    [ObservableProperty] private ConflictItem? selectedConflictItem;

    partial void OnSelectedConflictIndexChanged(int value)
    {
        if (value >= 0)
        {
            _currentConflict = value + 1; // 与列表同步（列表0基，状态1基）
            UpdateConflictsStatus();
            if (value < Conflicts.Count) SelectedConflictItem = Conflicts[value];
        }
    }

    private const string StartMarker = "<<<<<<< REMOTE";
    private const string MidMarker = "=======";
    private const string EndMarker = ">>>>>>> LOCAL";
    private int _currentConflict = 0; // 1-based
    private int _totalConflicts = 0;

    private readonly List<DiffSegment> _segments = new();
    private class DiffSegment
    {
        public bool IsConflict { get; init; }
        public string? Text { get; init; } // for non-conflict
        public string? Remote { get; init; } // for conflict
        public string? Local { get; init; } // for conflict
    }

    public event EventHandler<string?>? RequestClose;

    public MergeConflictViewModel() { }
    public MergeConflictViewModel(string remote, string local, string fileName)
    {
        this.fileName = fileName;
        remoteText = remote;
        localText = local;
        BuildDiff();
        // 如果没有检测到任何冲突但文本不同，强制生成一个整体冲突块，避免“看不到冲突”的困惑
        if (_totalConflicts == 0 && !string.Equals(NormalizeNewLines(remoteText), NormalizeNewLines(localText), StringComparison.Ordinal))
        {
            var force = $"{StartMarker}\n{NormalizeNewLines(remoteText)}\n{MidMarker}\n{NormalizeNewLines(localText)}\n{EndMarker}\n";
            MergedText = force;
            UpdateConflictsStatus();
        }
    }

    // 为兼容窗口选择与滚动，保留字符范围（基于合并文本中的标记计算）
    [ObservableProperty] private int currentStart;
    [ObservableProperty] private int currentLength;

    [RelayCommand]
    private void UseRemote()
    {
        foreach (var c in Conflicts) c.Choice = ChoiceOption.Remote;
        RebuildMergedFromSegments();
    }

    [RelayCommand]
    private void UseLocal()
    {
        foreach (var c in Conflicts) c.Choice = ChoiceOption.Local;
        RebuildMergedFromSegments();
    }

    [RelayCommand]
    private void SaveMerged()
    {
        RebuildMergedFromSegments();
        RequestClose?.Invoke(this, MergedText);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, null);

    [RelayCommand]
    private void BuildDiff()
    {
        BuildSegments(RemoteText ?? string.Empty, LocalText ?? string.Empty);
        BuildConflictItems();
        RebuildMergedFromSegments();
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void NextConflict()
    {
        if (_totalConflicts == 0) return;
        _currentConflict = Math.Min(_currentConflict + 1, _totalConflicts);
        SelectedConflictIndex = Math.Max(0, _currentConflict - 1);
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void PrevConflict()
    {
        if (_totalConflicts == 0) return;
        _currentConflict = Math.Max(_currentConflict - 1, 1);
        SelectedConflictIndex = Math.Max(0, _currentConflict - 1);
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void AcceptAllRemote()
    {
        foreach (var c in Conflicts) c.Choice = ChoiceOption.Remote;
        RebuildMergedFromSegments();
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void AcceptAllLocal()
    {
        foreach (var c in Conflicts) c.Choice = ChoiceOption.Local;
        RebuildMergedFromSegments();
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void AcceptCurrentRemote() => AcceptCurrent(true);

    [RelayCommand]
    private void AcceptCurrentLocal() => AcceptCurrent(false);

    private void AcceptCurrent(bool keepRemote)
    {
        if (_totalConflicts == 0 || _currentConflict < 1) return;
        var idx = Math.Max(0, _currentConflict - 1);
        if (idx >= Conflicts.Count) return;
        Conflicts[idx].Choice = keepRemote ? ChoiceOption.Remote : ChoiceOption.Local;
        RebuildMergedFromSegments();
        UpdateConflictsStatus();
    }

    private void UpdateConflictsStatus()
    {
        _totalConflicts = Conflicts.Count;
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
            _currentConflict = Math.Clamp(_currentConflict, 1, _totalConflicts);
            var chosen = Conflicts.Count(c => c.Choice != ChoiceOption.None);
            ConflictStatus = $"冲突 {_currentConflict}/{_totalConflicts}，已选择 {chosen}/{_totalConflicts}";
            var (start, length) = GetConflictRange(MergedText, _currentConflict);
            CurrentStart = start;
            CurrentLength = length;
        }
    }

    private static string NormalizeNewLines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    private void BuildSegments(string remote, string local)
    {
        _segments.Clear();
        var a = NormalizeNewLines(remote).Split('\n');
        var b = NormalizeNewLines(local).Split('\n');
        int n = a.Length, m = b.Length;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        int ia = 0, ib = 0;
        while (ia < n && ib < m)
        {
            if (a[ia] == b[ib])
            {
                _segments.Add(new DiffSegment { IsConflict = false, Text = a[ia] + "\n" });
                ia++; ib++;
            }
            else
            {
                int sa = ia, sbj = ib;
                while (ia < n && ib < m && a[ia] != b[ib])
                {
                    if (lcs[ia + 1, ib] >= lcs[ia, ib + 1]) ia++; else ib++;
                }
                var remoteChunk = string.Join('\n', a[sa..ia]);
                var localChunk = string.Join('\n', b[sbj..ib]);
                if (remoteChunk.Length > 0 || localChunk.Length > 0)
                {
                    if (!string.IsNullOrEmpty(remoteChunk)) remoteChunk += "\n";
                    if (!string.IsNullOrEmpty(localChunk)) localChunk += "\n";
                    _segments.Add(new DiffSegment { IsConflict = true, Remote = remoteChunk, Local = localChunk });
                }
            }
        }
        if (ia < n || ib < m)
        {
            var remoteTail = string.Join('\n', a[ia..n]);
            var localTail = string.Join('\n', b[ib..m]);
            if (remoteTail.Length > 0 || localTail.Length > 0)
            {
                if (!string.IsNullOrEmpty(remoteTail)) remoteTail += "\n";
                if (!string.IsNullOrEmpty(localTail)) localTail += "\n";
                _segments.Add(new DiffSegment { IsConflict = true, Remote = remoteTail, Local = localTail });
            }
        }
    }

    private void BuildConflictItems()
    {
        Conflicts.Clear();
        int idx = 1;
        foreach (var s in _segments)
        {
            if (!s.IsConflict) continue;
            Conflicts.Add(new ConflictItem { Index = idx++, Remote = s.Remote ?? string.Empty, Local = s.Local ?? string.Empty, Choice = ChoiceOption.None, Owner = this });
        }
        _totalConflicts = Conflicts.Count;
        _currentConflict = _totalConflicts > 0 ? 1 : 0;
        SelectedConflictIndex = _currentConflict > 0 ? 0 : -1;
    }

    internal void OnItemChoiceChanged(ConflictItem _)
    {
        RebuildMergedFromSegments();
        UpdateConflictsStatus();
    }

    private void RebuildMergedFromSegments()
    {
        var sb = new System.Text.StringBuilder();
        int conflictIdx = 0;
        foreach (var s in _segments)
        {
            if (!s.IsConflict)
            {
                sb.Append(s.Text);
            }
            else
            {
                var item = conflictIdx < Conflicts.Count ? Conflicts[conflictIdx] : null;
                conflictIdx++;
                if (item is null || item.Choice == ChoiceOption.None)
                {
                    sb.AppendLine(StartMarker);
                    if (!string.IsNullOrEmpty(s.Remote)) sb.Append(s.Remote);
                    sb.AppendLine(MidMarker);
                    if (!string.IsNullOrEmpty(s.Local)) sb.Append(s.Local);
                    sb.AppendLine(EndMarker);
                }
                else
                {
                    sb.Append(item.Choice == ChoiceOption.Remote ? s.Remote : s.Local);
                }
            }
        }
        MergedText = sb.ToString();
    }

    [RelayCommand]
    private void ChooseRemote(ConflictItem item)
    {
        if (item is null) return;
        item.Choice = ChoiceOption.Remote;
        RebuildMergedFromSegments();
        UpdateConflictsStatus();
    }

    [RelayCommand]
    private void ChooseLocal(ConflictItem item)
    {
        if (item is null) return;
        item.Choice = ChoiceOption.Local;
        RebuildMergedFromSegments();
        UpdateConflictsStatus();
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
