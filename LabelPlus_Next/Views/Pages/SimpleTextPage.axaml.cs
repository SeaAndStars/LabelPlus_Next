using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LabelPlus_Next.Views.Pages;

public partial class SimpleTextPage : UserControl
{
    public SimpleTextPage() { InitializeComponent(); }
    public SimpleTextPage(string text)
    {
        InitializeComponent();
        var tb = this.FindControl<TextBlock>("Text");
        if (tb != null) tb.Text = text;
    }
    private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }
}
