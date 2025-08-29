using Avalonia.Controls;
using Avalonia.Threading;
using LabelPlus_Next.ViewModels;
using System.ComponentModel;

namespace LabelPlus_Next.Views.Windows;

public partial class MergeConflictWindow : Window
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
        // 确保控件已经加载
        if (this.FindControl<TextBox>("MergedTextBox") is { } tb)
        {
            var start = Math.Max(0, Math.Min(vm.CurrentStart, (tb.Text ?? string.Empty).Length));
            var length = Math.Max(0, Math.Min(vm.CurrentLength, Math.Max(0, (tb.Text ?? string.Empty).Length - start)));
            tb.SelectionStart = start;
            tb.SelectionEnd = start + length;
            // 延迟调度，等待布局完成后再滚动
            Dispatcher.UIThread.Post(() => tb.CaretIndex = tb.SelectionEnd);
        }
    }
}
