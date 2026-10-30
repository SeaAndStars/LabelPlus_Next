using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LabelPlus_Next.Views.Windows;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ConfirmDialogViewModel? VM => DataContext as ConfirmDialogViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (VM is not { } vm) return;

        var cancel = this.FindControl<Button>("CancelButton");
        var no = this.FindControl<Button>("NoButton");
        var yes = this.FindControl<Button>("YesButton");
        var initial = vm.YesIsDanger || vm.NoIsDanger
            ? (cancel?.IsVisible == true ? cancel : no)
            : yes;
        initial?.Focus();

        ApplyTitleStyle();
        Dispatcher.UIThread.Post(ApplyTitleStyle, DispatcherPriority.Background);
    }

    /// <summary>Ursa's custom dialog title is a template part; make the action title
    /// visibly larger and bolder without replacing the dialog chrome.</summary>
    private void ApplyTitleStyle()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || VM is not { } vm) return;

        var title = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => string.Equals(t.Text, vm.Title, StringComparison.Ordinal));
        if (title is null) return;

        title.FontSize = 16;
        title.FontWeight = FontWeight.Bold;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            VM?.Cancel();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ApplyVisualState();
    }

    private void ApplyVisualState()
    {
        if (DataContext is not ConfirmDialogViewModel vm) return;
        if (!vm.NoIsDanger)
        {
            var no = this.FindControl<Button>("NoButton");
            no?.Classes.Add("dialog-default");
        }
    }

    private void Yes_OnClick(object? sender, RoutedEventArgs e) => VM?.Confirm();

    private void No_OnClick(object? sender, RoutedEventArgs e) => VM?.Refuse();

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => VM?.Cancel();
}
