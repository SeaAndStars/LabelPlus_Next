using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelPlus_Next.Lang;
using LabelPlus_Next.Models;
using LabelPlus_Next.Services;
using System.Text;

namespace LabelPlus_Next.ViewModels;

/// <summary>
/// Find/replace and quick-input support for the translation workspace.
/// Split out of TranslateViewModel to keep the core file focused on file
/// lifecycle and editing.
/// </summary>
public partial class TranslateViewModel
{
    private sealed record SearchMatch(string ImageFile, LabelItem Label, int Offset, int Length);

    private readonly List<SearchMatch> _searchMatches = new();
    private int _searchMatchIndex = -1;

    [ObservableProperty] private bool isSearchOpen;
    [ObservableProperty] private string? searchText;
    [ObservableProperty] private string? replaceText;
    [ObservableProperty] private bool searchMatchCase;
    [ObservableProperty] private bool searchWrapAround = true;
    [ObservableProperty] private string? searchStatus;
    [ObservableProperty] private int searchMatchCount;
    [ObservableProperty] private int searchPreviewStart = -1;
    [ObservableProperty] private int searchPreviewEnd = -1;

    /// <summary>Raised when the editor should select the current search hit.</summary>
    public event EventHandler? SearchPreviewRequested;

    /// <summary>Payloads for the context/Alt+A quick input list (chars from the original LabelPlus).</summary>
    public IReadOnlyList<QuickInputItem> QuickInputItems { get; } = new[]
    {
        new QuickInputItem("啊", "A"),
        new QuickInputItem("嗯", "E"),
        new QuickInputItem("呜", "W"),
        new QuickInputItem("唔", "W"),
        new QuickInputItem("咕", "G"),
        new QuickInputItem("咿", "Y"),
        new QuickInputItem("呀", "Y"),
        new QuickInputItem("啾", "J"),
        new QuickInputItem("噗", "P"),
        new QuickInputItem("♥", "1"),
        new QuickInputItem("♡", "2"),
        new QuickInputItem("♪", "3")
    };

    partial void OnSearchTextChanged(string? value) => RefreshSearchMatches();

    partial void OnSearchMatchCaseChanged(bool value) => RefreshSearchMatches();

    private void RefreshSearchMatches()
    {
        ClearSearchMarks();
        _searchMatches.Clear();
        _searchMatchIndex = -1;
        SearchMatchCount = 0;
        SearchPreviewStart = -1;
        SearchPreviewEnd = -1;

        var find = SearchText;
        if (string.IsNullOrEmpty(find))
        {
            SearchStatus = null;
            SearchPreviewRequested?.Invoke(this, EventArgs.Empty); return;
        }

        var comparison = SearchMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        IReadOnlyList<string> files = ImageFileNames.Count > 0
            ? ImageFileNames.ToList()
            : _labelManager.StoreManager.Store.Keys.ToList();

        foreach (var file in files)
        {
            if (!_labelManager.StoreManager.Store.TryGetValue(file, out var labels)) continue;
            foreach (var label in labels)
            {
                var text = label.Text;
                if (string.IsNullOrEmpty(text)) continue;

                var offset = text.IndexOf(find, comparison);
                while (offset >= 0)
                {
                    _searchMatches.Add(new SearchMatch(file, label, offset, find.Length));
                    offset = text.IndexOf(find, offset + find.Length, comparison);
                }
            }
        }

        foreach (var match in _searchMatches) match.Label.IsSearchMatch = true;
        SearchMatchCount = _searchMatches.Count;

        if (SearchMatchCount == 0)
        {
            SearchStatus = UiText.T(LangKeys.findReplaceNoResult, "没有匹配项");
            SearchPreviewRequested?.Invoke(this, EventArgs.Empty); return;
        }

        ShowSearchMatch(0, wrap: false);
    }

    private void ClearSearchMarks()
    {
        foreach (var match in _searchMatches) match.Label.IsSearchMatch = false;
    }

    private void ShowSearchMatch(int index, bool wrap)
    {
        if (_searchMatches.Count == 0) return;

        if (index < 0) index = wrap && SearchWrapAround ? _searchMatches.Count - 1 : 0;
        else if (index >= _searchMatches.Count) index = wrap && SearchWrapAround ? 0 : _searchMatches.Count - 1;

        _searchMatchIndex = index;
        var match = _searchMatches[index];

        if (!string.Equals(SelectedImageFile, match.ImageFile, StringComparison.Ordinal))
            SelectedImageFile = match.ImageFile;

        SelectedLabel = match.Label;
        CurrentText = match.Label.Text;
        SearchPreviewStart = match.Offset;
        SearchPreviewEnd = match.Offset + match.Length;
        SearchStatus = UiText.F(LangKeys.findReplaceMatchPosition, "第 {0} / {1} 处", index + 1, SearchMatchCount);
        SearchPreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void FindNext()
    {
        if (string.IsNullOrEmpty(SearchText) || _searchMatches.Count == 0) return;
        ShowSearchMatch(_searchMatchIndex + 1, wrap: true);
    }

    [RelayCommand]
    private void FindPrevious()
    {
        if (string.IsNullOrEmpty(SearchText) || _searchMatches.Count == 0) return;
        ShowSearchMatch(_searchMatchIndex - 1, wrap: true);
    }

    [RelayCommand]
    private void ReplaceSearchMatch()
    {
        if (_searchMatches.Count == 0 || _searchMatchIndex < 0 || _searchMatchIndex >= _searchMatches.Count) return;

        var match = _searchMatches[_searchMatchIndex];
        var text = match.Label.Text ?? string.Empty;
        if (match.Offset < 0 || match.Offset + match.Length > text.Length) return;

        var replacement = ReplaceText ?? string.Empty;
        match.Label.Text = text[..match.Offset] + replacement + text[(match.Offset + match.Length)..];
        if (ReferenceEquals(match.Label, SelectedLabel)) CurrentText = match.Label.Text;
        _labelManager.StoreManager.TouchDirty();

        var next = _searchMatchIndex;
        RefreshSearchMatches();
        if (_searchMatches.Count > 0)
            ShowSearchMatch(Math.Min(next, _searchMatches.Count - 1), wrap: false);

        SearchStatus = UiText.F(LangKeys.findReplaceReplacedCount, "已替换 {0} 处", 1);
    }

    [RelayCommand]
    private void ReplaceAllSearchMatches()
    {
        var find = SearchText;
        if (string.IsNullOrEmpty(find)) return;

        var replacement = ReplaceText ?? string.Empty;
        var total = 0;
        IReadOnlyList<string> files = ImageFileNames.Count > 0
            ? ImageFileNames.ToList()
            : _labelManager.StoreManager.Store.Keys.ToList();

        foreach (var file in files)
        {
            if (!_labelManager.StoreManager.Store.TryGetValue(file, out var labels)) continue;
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label.Text)) continue;
                var (updated, count) = ReplaceAllInText(label.Text, find, replacement, SearchMatchCase);
                if (count == 0) continue;
                label.Text = updated;
                total += count;
            }
        }

        if (total > 0)
        {
            _labelManager.StoreManager.TouchDirty();
            if (SelectedLabel is not null) CurrentText = SelectedLabel.Text;
        }

        RefreshSearchMatches();
        SearchStatus = total > 0
            ? UiText.F(LangKeys.findReplaceReplacedCount, "已替换 {0} 处", total)
            : UiText.T(LangKeys.findReplaceNoResult, "没有匹配项");
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        SearchText = null;
        SearchStatus = null;
        SearchMatchCount = 0;
        ClearSearchMarks();
        _searchMatches.Clear();
        _searchMatchIndex = -1;
        SearchPreviewStart = -1;
        SearchPreviewEnd = -1;
        SearchPreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private static (string Text, int Count) ReplaceAllInText(string text, string find, string replacement, bool matchCase)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var builder = new StringBuilder(text.Length);
        var start = 0;
        var count = 0;

        while (true)
        {
            var index = text.IndexOf(find, start, comparison);
            if (index < 0) break;
            builder.Append(text, start, index - start);
            builder.Append(replacement);
            start = index + find.Length;
            count++;
        }

        if (count == 0) return (text, 0);
        builder.Append(text, start, text.Length - start);
        return (builder.ToString(), count);
    }
}

/// <summary>One entry in the quick-input popup.</summary>
public sealed record QuickInputItem(string Text, string Key);
