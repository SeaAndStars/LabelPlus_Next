using Avalonia.Controls;
using Avalonia.Threading;
using LabelPlus_Next.ViewModels;
using System.ComponentModel;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Windows;

public partial class MergeConflictWindow : UrsaWindow
{
    public MergeConflictWindow()
    {
        InitializeComponent();
    }

    public Task<string?> ShowAsync(Window owner, string remoteText, string localText, string fileName)
    {
        var tcs = new TaskCompletionSource<string?>();
        var vm = new MergeConflictViewModel(remoteText, localText, fileName);
        vm.RequestClose += (_, result) => { Close(); tcs.TrySetResult(result); };
        DataContext = vm;
        HookViewModel(vm);
        ShowDialog(owner);
        return tcs.Task;
    }

    private void HookViewModel(MergeConflictViewModel vm)
    {
        vm.PropertyChanged += VmOnPropertyChanged;
        // 初次建模后也尝试滚动到第一处冲突
        TrySelectAndScroll(vm);
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MergeConflictViewModel vm)
        {
            if (e.PropertyName is nameof(MergeConflictViewModel.ConflictStatus)
                or nameof(MergeConflictViewModel.MergedText)
                or nameof(MergeConflictViewModel.CurrentStart)
                or nameof(MergeConflictViewModel.CurrentLength))
            {
                TrySelectAndScroll(vm);
            }
        }
    }

    private void TrySelectAndScroll(MergeConflictViewModel vm)
    {
        // 只读模式：通过字符位置粗略滚动到冲突区域（按比例近似）
        var tb = this.FindControl<TextBlock>("MergedTextBlock");
        var sv = this.FindControl<ScrollViewer>("MergedScroll");
        if (tb is null || sv is null) return;
        var text = tb.Text ?? string.Empty;
        var pos = Math.Max(0, Math.Min(vm.CurrentStart + vm.CurrentLength / 2, text.Length));
        double ratio = text.Length > 0 ? (double)pos / text.Length : 0.0;
        // 等布局完成后滚动
        Dispatcher.UIThread.Post(() =>
        {
            var extent = sv.Extent.Height;
            var viewport = sv.Viewport.Height;
            var target = Math.Max(0, extent * ratio - viewport * 0.3);
            sv.Offset = new Avalonia.Vector(sv.Offset.X, target);
        });
    }
}
