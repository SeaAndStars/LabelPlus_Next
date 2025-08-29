using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using NLog;
using System.Collections.ObjectModel;
using Ursa.Controls;
using LabelPlus_Next.Models;
using Avalonia.Layout;

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
        DataContextChanged += (_, __) =>
        {
            try
            {
                BuildTree();
            }
            catch (Exception ex) { Logger.Error(ex, "BuildTree failed on DataContextChanged."); }
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
            var episodeDisp = isSp ? "番外" : (isVol ? string.Format("{0:00}(卷)", ep.Number) : string.Format("{0:00}", ep.Number));
            var root = new Node
            {
                IsFile = false,
    Name = title,
                Number = ep.Number,
                Include = ep.Include,
                Status = ep.Status,
    IsSpecial = isSp,
    IsVolume = isVol,
                LocalFileCount = ep.LocalFiles.Count,
                EpisodeDisplay = episodeDisp,
        LocalFileCountDisplay = ep.LocalFiles.Count.ToString(),
        Ref = ep
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
                    LocalFileCount = 0,
                    EpisodeDisplay = episodeDisp,
                    LocalFileCountDisplay = "0"
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
        new TemplateColumn<Node>("话数", BuildEpisodeNumberCellTemplate()),
                new TemplateColumn<Node>("状态", BuildStatusCellTemplate()),
                new TextColumn<Node, string>("本地文件数", x => x.LocalFileCountDisplay)
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
            foreach (var node in _roots.Where(n => !n.IsFile))
            {
                if (node.Ref is null) continue;
                var ep = node.Ref;
                ep.Include = node.Include;
                ep.Status = node.Status;
                ep.Number = node.Number;
                if (node.IsSpecial) ep.IsSpecial = true;
                if (node.IsVolume) ep.IsVolume = true;
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

    private static IDataTemplate BuildEpisodeNumberCellTemplate()
    {
        return new FuncDataTemplate<Node>((n, _) =>
        {
            if (n.IsFile)
            {
                return new TextBlock { Text = string.Empty };
            }
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var tb = new TextBox { Width = 80, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            tb.Text = n.IsSpecial ? (n.Number > 0 ? $"番外{n.Number:00}" : "番外")
                                  : (n.IsVolume ? n.Number.ToString() : n.Number.ToString());
            tb.LostFocus += (_, __) => ApplyNumberEdit(n, tb.Text);
            tb.KeyUp += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) ApplyNumberEdit(n, tb.Text); };
            panel.Children.Add(tb);
            if (n.IsSpecial)
            {
                panel.Children.Add(new TextBlock { Text = "(番外)" });
            }
            else if (n.IsVolume)
            {
                panel.Children.Add(new TextBlock { Text = "(卷)" });
            }
            return panel;
        }, true);
    }

    private static void ApplyNumberEdit(Node n, string? text)
    {
        try
        {
            if (n.IsSpecial)
            {
                var digits = new string((text ?? string.Empty).Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var sp) && sp >= 0) n.Number = sp;
            }
            else
            {
                if (int.TryParse(new string((text ?? string.Empty).Where(char.IsDigit).ToArray()), out var val) && val > 0)
                    n.Number = val;
            }
        }
        catch { }
    }

    private sealed class Node
    {
        public bool IsFile { get; init; }
        public string? Name { get; init; }
    public int Number { get; set; }
    public bool IsVolume { get; init; }
    public bool IsSpecial { get; init; }
        public bool Include { get; set; }
        public string Status { get; set; } = "立项";
        public int LocalFileCount { get; init; }
    public string EpisodeDisplay { get; init; } = string.Empty;
    public string LocalFileCountDisplay { get; init; } = string.Empty;
        public ObservableCollection<Node> Children { get; } = new();
        public EpisodeEntry? Ref { get; init; }
    }
}
