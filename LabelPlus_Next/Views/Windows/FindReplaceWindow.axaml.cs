using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Windows;

/// <summary>
/// Modeless find/replace tool window. It owns the same TranslateViewModel as
/// the workspace, so search hits still highlight the grid and editor while the
/// window is moved out of the way.
/// </summary>
public partial class FindReplaceWindow : UrsaWindow
{
    private TranslateViewModel? _vm;

    public FindReplaceWindow()
    {
        InitializeComponent();
    }

    public FindReplaceWindow(TranslateViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_vm is not null) _vm.IsSearchOpen = true;
        FocusSearch();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null) _vm.CloseSearchCommand.Execute(null);
        base.OnClosed(e);
    }

    public void FocusSearch()
    {
        var search = this.FindControl<TextBox>("SearchTextBox");
        search?.Focus();
        search?.SelectAll();
    }

    private void SearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0) _vm?.FindPreviousCommand.Execute(null);
                else _vm?.FindNextCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ReplaceTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm?.ReplaceSearchMatchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
