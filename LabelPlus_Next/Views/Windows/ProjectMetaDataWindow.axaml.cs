using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.Models;
using LabelPlus_Next.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Windows;

public partial class ProjectMetaDataWindow : Ursa.Controls.UrsaWindow
{
    private TreeDataGrid? _tree;
    private ObservableCollection<Node>? _roots;

    private sealed class Node
    {
        public bool IsFile { get; init; }
        public string? Name { get; init; }
        public int Number { get; init; }
        public bool Include { get; set; }
        public string Status { get; set; } = "立项";
        public int LocalFileCount { get; init; }
        public ObservableCollection<Node> Children { get; } = new();
    }

    public ProjectMetaDataWindow()
    {
        InitializeComponent();
        _tree = this.FindControl<TreeDataGrid>("EpisodeTree");
        this.Opened += (_, __) => BuildTree();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void BuildTree()
    {
        if (_tree is null || DataContext is not UploadViewModel vm) return;
        _roots = new ObservableCollection<Node>(vm.PendingEpisodes.Select(ep =>
        {
            var root = new Node
            {
                IsFile = false,
                Name = $"第 {ep.Number} 话",
                Number = ep.Number,
                Include = ep.Include,
                Status = ep.Status,
                LocalFileCount = ep.LocalFiles.Count
            };
            foreach (var f in ep.LocalFiles)
            {
                root.Children.Add(new Node
                {
                    IsFile = true,
                    Name = System.IO.Path.GetFileName(f),
                    Number = ep.Number,
                    Include = true,
                    Status = string.Empty,
                    LocalFileCount = 0
                });
            }
            return root;
        }));

        var source = new HierarchicalTreeDataGridSource<Node>(_roots)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<Node>(
                    new TemplateColumn<Node>("名称", BuildTextTemplate(n => n.Name ?? string.Empty)),
                    n => n.Children,
                    n => !n.IsFile),
                new TemplateColumn<Node>("上传", BuildCheckTemplate()),
                new TemplateColumn<Node>("话数", BuildTextTemplate(n => n.Number.ToString())),
                new TemplateColumn<Node>("状态", BuildStatusTemplate()),
                new TemplateColumn<Node>("本地文件数", BuildTextTemplate(n => n.LocalFileCount.ToString()))
            }
        };
        _tree.Source = source;
    }

    private static IDataTemplate BuildTextTemplate(System.Func<Node, string> selector)
    {
        return new FuncTreeDataTemplate<Node>((n, _) => new TextBlock { Text = selector(n) }, (o, _) => o is Node);
    }

    private static IDataTemplate BuildCheckTemplate()
    {
        return new FuncTreeDataTemplate<Node>((n, _) =>
        {
            var cb = new CheckBox { IsEnabled = !n.IsFile };
            cb.IsChecked = n.Include;
            cb.Checked += (_, __) => n.Include = true;
            cb.Unchecked += (_, __) => n.Include = false;
            return cb;
        }, (o, _) => o is Node);
    }

    private static IDataTemplate BuildStatusTemplate()
    {
        return new FuncTreeDataTemplate<Node>((n, _) =>
        {
            var box = new ComboBox { IsEnabled = !n.IsFile };
            box.Items = new[] { "立项", "翻译", "校对", "嵌字" };
            box.SelectedItem = n.Status;
            box.SelectionChanged += (_, __) => { if (box.SelectedItem is string s) n.Status = s; };
            return box;
        }, (o, _) => o is Node);
    }

    private async void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not UploadViewModel vm || _roots is null) { Close(false); return; }
        // write back Include/Status to VM.PendingEpisodes
        foreach (var ep in vm.PendingEpisodes)
        {
            var node = _roots.FirstOrDefault(n => !n.IsFile && n.Number == ep.Number);
            if (node is null) continue;
            ep.Include = node.Include;
            ep.Status = node.Status;
        }
        var ok = await vm.UploadPendingAsync();
        await MessageBox.ShowAsync(ok ? "上传完成" : "上传失败", ok ? "成功" : "失败");
        Close(ok);
    }
}