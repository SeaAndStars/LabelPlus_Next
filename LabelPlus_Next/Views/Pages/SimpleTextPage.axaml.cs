using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LabelPlus_Next.Views.Pages;

public partial class SimpleTextPage : UserControl
{
    public SimpleTextPage() { InitializeComponent(); }
    public SimpleTextPage(string text, string? subtitle = null)
    {
        InitializeComponent();
        var tb = this.FindControl<TextBlock>("Text");
        if (tb != null) tb.Text = text;
        var sub = this.FindControl<TextBlock>("Subtitle");
        if (sub != null && !string.IsNullOrWhiteSpace(subtitle))
        {
            sub.Text = subtitle;
            sub.IsVisible = true;
        }
    }
    private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }
}
