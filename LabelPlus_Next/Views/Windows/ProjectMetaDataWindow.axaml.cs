using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using NLog;
using System.Collections.ObjectModel;
using Ursa.Controls;

namespace LabelPlus_Next.Views.Windows;

public partial class ProjectMetaDataWindow : UrsaWindow
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly TreeDataGrid? _tree;
    private ObservableCollection<Node>? _roots;

    public ProjectMetaDataWindow()
    {
        InitializeComponent();
        _tree = this.FindControl<TreeDataGrid>("EpisodeTree");
        Opened += (_, __) =>
        {
            try
            {
                BuildTree();
                Logger.Info("Meta window opened and tree built.");
            }
            catch (Exception ex) { Logger.Error(ex, "BuildTree failed on open."); }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void BuildTree()
    {
        if (_tree is null || DataContext is not UploadViewModel vm)
        {
            Logger.Warn("BuildTree: Tree or ViewModel missing.");
            return;
        }
        Logger.Debug("Building episode tree: {count}", vm.PendingEpisodes.Count);
    _roots = new ObservableCollection<Node>(vm.PendingEpisodes.Select(ep =>
        {
            var isSp = ep is { IsSpecial: true };
            var isVol = ep is { IsVolume: true };
            var title = isSp ? "番外" : isVol ? $"第 {ep.Number} 卷" : $"第 {ep.Number} 话";
            var root = new Node
            {
                IsFile = false,
        Name = title,
                Number = ep.Number,
                Include = ep.Include,
                Status = ep.Status,
        IsSpecial = isSp,
        IsVolume = isVol,
                LocalFileCount = ep.LocalFiles.Count
            };
            foreach (var f in ep.LocalFiles)
            {
                root.Children.Add(new Node
                {
                    IsFile = true,
                    Name = Path.GetFileName(f),
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
                    new TextColumn<Node, string>("名称", x => x.Name ?? string.Empty),
                    n => n.Children,
                    n => !n.IsFile),
                new CheckBoxColumn<Node>("上传", x => x.Include, (x, v) => x.Include = v),
                new TextColumn<Node, string>("话数", x => x.IsSpecial ? "番外" : (x.IsVolume ? $"{x.Number:00}(卷)" : x.Number.ToString("00"))),
                new TemplateColumn<Node>("状态", BuildStatusCellTemplate()),
                new TextColumn<Node, int>("本地文件数", x => x.LocalFileCount)
            }
        };
        _tree.Source = source;
    }

    private static IDataTemplate BuildStatusCellTemplate()
    {
        return new FuncDataTemplate<Node>((n, _) =>
        {
            var box = new ComboBox { IsEnabled = !n.IsFile };
            box.ItemsSource = new[] { "立项", "翻译", "校对", "嵌字", "发布" };
            box.SelectedItem = n.Status;
            box.SelectionChanged += (_, __) =>
            {
                if (box.SelectedItem is string s) n.Status = s;
            };
            return box;
        }, true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Logger.Info("Meta window cancelled by user.");
        Close(false);
    }

    private async void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not UploadViewModel vm || _roots is null)
            {
                Logger.Warn("Confirm: ViewModel or roots missing.");
                Close(false);
                return;
            }
            if (string.IsNullOrWhiteSpace(vm.PendingProjectName))
            {
                await MessageBox.ShowAsync("请填写项目名", "提示", MessageBoxIcon.Warning);
                return;
            }
            foreach (var ep in vm.PendingEpisodes)
            {
                var node = _roots.FirstOrDefault(n => !n.IsFile && n.Number == ep.Number);
                if (node is null) continue;
                ep.Include = node.Include;
                ep.Status = node.Status;
                // 保持 IsSpecial 标记
                if (node.IsSpecial) ep.IsSpecial = true;
            }
            Logger.Info("Confirm: starting upload for {count} episodes", vm.PendingEpisodes.Count(e1 => e1.Include));
            var ok = await vm.UploadPendingAsync();
            await MessageBox.ShowAsync(ok ? "上传完成" : "上传失败", ok ? "成功" : "失败");
            Logger.Info("Upload result: {result}", ok);
            Close(ok);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Confirm click failed.");
            await MessageBox.ShowAsync(ex.Message, "错误");
        }
    }

    private sealed class Node
    {
        public bool IsFile { get; init; }
        public string? Name { get; init; }
        public int Number { get; init; }
    public bool IsVolume { get; init; }
    public bool IsSpecial { get; init; }
        public bool Include { get; set; }
        public string Status { get; set; } = "立项";
        public int LocalFileCount { get; init; }
        public ObservableCollection<Node> Children { get; } = new();
    }
}
