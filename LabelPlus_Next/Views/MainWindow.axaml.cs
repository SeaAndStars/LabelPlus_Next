using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.CustomControls;
using LabelPlus_Next.Lang;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using LabelPlus_Next.Services;
using LabelPlus_Next.Views.Pages; // add for ImageManager

namespace LabelPlus_Next.Views
{
    public partial class MainWindow : Window
    {
        private ComboBox? langComboBox;
        private PicViewer? picViewerControl;
        private DataGrid? labelsGrid;
        private INotifyPropertyChanged? vmNotify;

        public MainWindow()
        {
            InitializeComponent(true, true);
            langComboBox = this.FindControl<ComboBox>("LangComboBox");
            picViewerControl = this.FindControl<PicViewer>("Pic");
            labelsGrid = this.FindControl<DataGrid>("LabelsGrid");
            if (DataContext is MainWindowViewModel mvm)
            {
                mvm.InitializeServices(new AvaloniaFileDialogService(this));
            }
            if (labelsGrid is not null)
            {
                labelsGrid.SelectionChanged += LabelsGridOnSelectionChanged;
            }
            if (picViewerControl is not null)
            {
                picViewerControl.AddLabelRequested += OnAddLabelRequested;
            }

            this.AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel, handledEventsToo: false);

            this.DataContextChanged += OnDataContextChanged;
            if (DataContext is INotifyPropertyChanged npc)
            {
                HookViewModel(npc);
            }

            this.Focus();
        }

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            if (e is null || e.Handled) return;
            ProcessKeyGesture(e);
        }

        private async void FileSetting_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel mvm) return;
            var (groups, notes) = mvm.GetFileSettings();
            var dlg = new LabelPlus_Next.Views.Pages.FileSettings
            {
                DataContext = new FileSettingsViewModel { GroupList = groups, Notes = notes },
                Width = 600,
                Height = 350
            };
            var result = await dlg.ShowDialog<bool?>(this);
            if (result == true)
            {
                if (dlg.DataContext is FileSettingsViewModel fvm)
                {
                    await mvm.SaveFileSettingsAsync(fvm.GroupList, fvm.Notes);
                }
            }
        }

        private void ProcessKeyGesture(KeyEventArgs e)
        {
            var vm = ViewModel;

            // Quick access to viewer
            var pic = picViewerControl ?? this.FindControl<PicViewer>("Pic");

            // Global single key modes
            if (e.KeyModifiers == KeyModifiers.None)
            {
                switch (e.Key)
                {
                    case Key.Q: // 浏览
                        if (pic != null) pic.Mode = ViewerMode.Browse;
                        e.Handled = true;
                        return;
                    case Key.W: // 标注
                        if (pic != null) pic.Mode = ViewerMode.Label;
                        e.Handled = true;
                        return;
                    case Key.E: // 录入
                        if (pic != null) pic.Mode = ViewerMode.Input;
                        e.Handled = true;
                        return;
                    case Key.R: // 审效
                        if (pic != null) pic.Mode = ViewerMode.Check;
                        e.Handled = true;
                        return;
                }
            }

            // Ctrl shortcuts
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                if (e.Key == Key.N) // 新建翻译
                {
                    (vm?.NewTranslationCommandCommand as IAsyncRelayCommand)?.Execute(null);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Q) // 退出应用
                {
                    Close();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Up)
                {
                    MoveSelection(-1);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Down)
                {
                    MoveSelection(1);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Z)
                {
                    (vm?.UndoRemoveLabelCommandCommand as IAsyncRelayCommand)?.Execute(null);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.O)
                {
                    (vm?.OpenTranslationFileCommandCommand as IAsyncRelayCommand)?.Execute(null);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.S)
                {
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                        (vm?.SaveAsCommandCommand as IAsyncRelayCommand)?.Execute(null);
                    else
                        (vm?.SaveCurrentCommandCommand as IAsyncRelayCommand)?.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.Delete)
            {
                (vm?.RemoveLabelCommandCommand as IAsyncRelayCommand)?.Execute(null);
                e.Handled = true;
                return;
            }
        }

        private async void OnAddLabelRequested(object? sender, PicViewer.AddLabelRequestedEventArgs e)
        {
            if (ViewModel is null) return;
            await ViewModel.AddLabelAtAsync((float)e.XPercent, (float)e.YPercent, e.Category);
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (vmNotify is not null)
            {
                vmNotify.PropertyChanged -= VmOnPropertyChanged;
                vmNotify = null;
            }
            if (DataContext is INotifyPropertyChanged npc)
            {
                HookViewModel(npc);
            }
            // Ensure dialog service is available after DataContext changes
            if (DataContext is MainWindowViewModel mvm)
            {
                mvm.InitializeServices(new AvaloniaFileDialogService(this));
            }
        }

        private void HookViewModel(INotifyPropertyChanged npc)
        {
            vmNotify = npc;
            vmNotify.PropertyChanged += VmOnPropertyChanged;
        }

        private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedLabel))
            {
                ScrollSelectedIntoView();
            }
        }

        private void LabelsGridOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            ScrollSelectedIntoView();
        }

        private void ScrollSelectedIntoView()
        {
            if (labelsGrid?.SelectedItem is { } item)
            {
                try { labelsGrid.ScrollIntoView(item, null); } catch { }
            }
        }

        private MainWindowViewModel ViewModel => DataContext as MainWindowViewModel;

        private void LangComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is not ComboBox comboBox || comboBox.SelectedItem is not string selectedLang)
                    return;
                I18NExtension.Culture = new CultureInfo(selectedLang);
            }
            catch (CultureNotFoundException)
            {
                I18NExtension.Culture = CultureInfo.CurrentUICulture;
            }
        }

        private void MoveSelection(int delta)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm == null || vm.CurrentLabels == null || vm.CurrentLabels.Count == 0)
                return;
            var idx = vm.SelectedLabel != null ? vm.CurrentLabels.IndexOf(vm.SelectedLabel) : -1;
            var newIdx = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, vm.CurrentLabels.Count - 1);
            vm.SelectedLabel = vm.CurrentLabels[newIdx];
            vm.CurrentText = vm.SelectedLabel?.Text;
            ScrollSelectedIntoView();
        }

        private void BrowseMode_OnClick(object? sender, RoutedEventArgs e)
        {
            var pic = this.FindControl<PicViewer>("Pic");
            if (pic != null)
                pic.Mode = ViewerMode.Browse;
        }

        private void LabelMode_OnClick(object? sender, RoutedEventArgs e)
        {
            var pic = this.FindControl<PicViewer>("Pic");
            if (pic != null)
                pic.Mode = ViewerMode.Label;
        }

        private void Imagine_manager_OnClick(object? sender, RoutedEventArgs e)
        {
            // Simply open ImageManager window
            var dlg = new ImageManager
            {
                DataContext = new ImageManagerViewModel()
            };
            dlg.Show(this);
        }

        // File menu Exit handler to match Ctrl+Q
        private void ExitMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}