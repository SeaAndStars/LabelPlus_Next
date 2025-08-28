using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;
using Ursa.Controls;
using NLog;
using System;

namespace LabelPlus_Next.Views.Windows;

public partial class ProjectMetaDataWindow : UrsaWindow
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private TreeDataGrid? _tree;
    private ObservableCollection<Node>? _roots;

    private sealed class Node
    {
        public bool IsFile { get; init; }
        public string? Name { get; init; }
        public int Number { get; init; }
        public bool Include { get; set; }
        public string Status { get; set; } = "����";
        public int LocalFileCount { get; init; }
        public ObservableCollection<Node> Children { get; } = new();
    }

    public ProjectMetaDataWindow()
    {
        InitializeComponent();
        _tree = this.FindControl<TreeDataGrid>("EpisodeTree");
        this.Opened += (_, __) =>
        {
            try { BuildTree(); Logger.Info("Meta window opened and tree built."); }
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
            var root = new Node
            {
                IsFile = false,
                Name = $"�� {ep.Number} ��",
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
                    new TextColumn<Node, string>("����", x => x.Name ?? string.Empty),
                    n => n.Children,
                    n => !n.IsFile),
                new CheckBoxColumn<Node>("�ϴ�", x => x.Include, (x, v) => x.Include = v),
                new TextColumn<Node, int>("����", x => x.Number),
                new TemplateColumn<Node>("״̬", BuildStatusCellTemplate()),
                new TextColumn<Node, int>("�����ļ���", x => x.LocalFileCount)
            }
        };
        _tree.Source = source;
    }

    private static IDataTemplate BuildStatusCellTemplate()
    {
        return new FuncDataTemplate<Node>((n, _) =>
        {
            var box = new ComboBox { IsEnabled = !n.IsFile };
            box.ItemsSource = new[] { "����", "����", "У��", "Ƕ��", "����" };
            box.SelectedItem = n.Status;
            box.SelectionChanged += (_, __) => { if (box.SelectedItem is string s) n.Status = s; };
            return box;
        }, true);
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Logger.Info("Meta window cancelled by user.");
        Close(false);
    }

    private async void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not UploadViewModel vm || _roots is null)
            {
                Logger.Warn("Confirm: ViewModel or roots missing.");
                Close(false);
                return;
            }
            foreach (var ep in vm.PendingEpisodes)
            {
                var node = _roots.FirstOrDefault(n => !n.IsFile && n.Number == ep.Number);
                if (node is null) continue;
                ep.Include = node.Include;
                ep.Status = node.Status;
            }
            Logger.Info("Confirm: starting upload for {count} episodes", vm.PendingEpisodes.Count(e1 => e1.Include));
            var ok = await vm.UploadPendingAsync();
            await MessageBox.ShowAsync(ok ? "�ϴ����" : "�ϴ�ʧ��", ok ? "�ɹ�" : "ʧ��");
            Logger.Info("Upload result: {result}", ok);
            Close(ok);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Confirm click failed.");
            await MessageBox.ShowAsync(ex.Message, "����");
        }
    }
}