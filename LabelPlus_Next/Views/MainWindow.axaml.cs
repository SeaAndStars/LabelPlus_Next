using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.CustomControls;
using LabelPlus_Next.Views.Pages;
using Ursa.Controls;
using LabelPlus_Next.Lang;
using System.ComponentModel;


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
            if (labelsGrid is not null)
            {
                labelsGrid.SelectionChanged += LabelsGridOnSelectionChanged;
            }
            if (picViewerControl is not null)
            {
                picViewerControl.AddLabelRequested += OnAddLabelRequested;
            }

            this.DataContextChanged += OnDataContextChanged;
            if (DataContext is INotifyPropertyChanged npc)
            {
                HookViewModel(npc);
            }

            this.Focus();
            // Remove manual image loading; VM drives PicImageSource binding
        }

        private async void OnAddLabelRequested(object? sender, PicViewer.AddLabelRequestedEventArgs e)
        {
            if (ViewModel is null) return;
            await ViewModel.AddLabelAtAsync((float)e.XPercent, (float)e.YPercent);
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
                try
                {
                    labelsGrid.ScrollIntoView(item, null);
                }
                catch
                {
                    // Ignore if API changes
                }
            }
        }

        private void Window_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.Z)
            {
                if (ViewModel != null && ViewModel.GetType().GetProperty("UndoRemoveLabelCommand")?.GetValue(ViewModel) is CommunityToolkit.Mvvm.Input.IRelayCommand undoCmd && undoCmd.CanExecute(null))
                {
                    undoCmd.Execute(null);
                }
                e.Handled = true;
            }
        }

        private MainWindowViewModel ViewModel => DataContext as MainWindowViewModel;

        private void Imagine_manager_OnClick(object? sender, RoutedEventArgs e)
        {
            var imgmanagepage = new ImageManager();
            imgmanagepage.Show();
        }

        private void Output_OnClick(object? sender, RoutedEventArgs e)
        {
            var outputpage = new ImageOutput();
            outputpage.Show();
        }

        private void View_Help_OnClick(object? sender, RoutedEventArgs e)
        {
        }

        private void About_OnClick(object? sender, RoutedEventArgs e)
        {
        }

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

        private async void NewTranslation(object? sender, RoutedEventArgs e)
        {
            try
            {
                var paths = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                                                                           {
                                                                               Title = I18NExtension.Translate(LangKeys.newToolStripMenuItem)
                                                                           });
                if (paths == null || paths.Count == 0)
                    return;
                var first = paths[0];
                if (first?.Path is { } p)
                {
                    Console.WriteLine(p.AbsolutePath);
                    if (ViewModel != null)
                        ViewModel.NewTranslationPath = p.AbsolutePath;
                }
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync(ex.Message);
            }
        }

        private async void OpenTranslationFile(object? sender, RoutedEventArgs e)
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                                              {
                                                  Title = I18NExtension.Translate(LangKeys.openToolStripMenuItem),
                                                  FileTypeFilter = new List<FilePickerFileType> { FilePickerFileTypes.TextPlain }
                                              });
                if (files == null || files.Count == 0)
                    return;
                var file = files[0];
                if (file?.Path is { } p)
                {
                    Console.WriteLine(p.AbsolutePath);
                    if (ViewModel != null)
                    {
                        ViewModel.OpenTranslationFilePath = p.AbsolutePath;
                        await ViewModel.LoadTranslationFile(p.AbsolutePath);
                    }
                }
            }
            catch (Exception exception)
            {
                await MessageBox.ShowAsync(exception.Message);
            }
        }

        private async void SaveAsAnotherFile(object? sender, RoutedEventArgs e)
        {
            try
            {
                var fileTypeChoices = new List<FilePickerFileType>
                                          { new("Text") { Patterns = new List<string> { "*.txt" } } };
                var saveasanotherfileHelper = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                                                  {
                                                      Title = I18NExtension.Translate(LangKeys.saveAsDToolStripMenuItem),
                                                      SuggestedFileName = "translation",
                                                      FileTypeChoices = fileTypeChoices,
                                                      DefaultExtension = ".txt"
                                                  });
                if (saveasanotherfileHelper == null)
                    return;
                // Optionally, implement save as logic using ViewModel.FileSave
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync(ex.Message);
            }
        }

        private async void SaveFile(object? sender, RoutedEventArgs e)
        {
            try
            {
                var savefile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                                                                             {
                                                                                 Title = I18NExtension.Translate(LangKeys.saveAsDToolStripMenuItem),
                                                                                 DefaultExtension = ".txt"
                                                                             });
                if (savefile != null && ViewModel != null && savefile.Path is { } p)
                {
                    var path = p.AbsolutePath;
                    await ViewModel.FileSave(path);
                }
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync(ex.Message);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                if (e.Key == Key.Up)
                {
                    MoveSelection(-1);
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    MoveSelection(1);
                    e.Handled = true;
                }
            }
        }

        private void MoveSelection(int delta)
        {
            var vm = DataContext as ViewModels.MainWindowViewModel;
            if (vm == null || vm.CurrentLabels == null || vm.CurrentLabels.Count == 0)
                return;
            var idx = vm.SelectedLabel != null ? vm.CurrentLabels.IndexOf(vm.SelectedLabel) : -1;
            var newIdx = idx < 0 ? 0 : System.Math.Clamp(idx + delta, 0, vm.CurrentLabels.Count - 1);
            vm.SelectedLabel = vm.CurrentLabels[newIdx];
            vm.CurrentText = vm.SelectedLabel?.Text;
            ScrollSelectedIntoView();
        }
    }
}