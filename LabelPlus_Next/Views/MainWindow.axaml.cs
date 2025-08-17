using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.CustomControls;
using LabelPlus_Next.Models;
using LabelPlus_Next.Views.Pages;
using Ursa.Controls;
using LabelPlus_Next.Lang;


namespace LabelPlus_Next.Views
{
    public partial class MainWindow : Window
    {


        private ComboBox? langComboBox;
        private PicViewer? picViewerControl;

        public MainWindow()
        {
            InitializeComponent(true, true);
            langComboBox = this.FindControl<ComboBox>("LangComboBox");
            // picViewerControl = this.FindControl<PicViewer>("PicViewerControl");
            this.Focus();
            var imageComboBox = this.FindControl<ComboBox>("ImageFileNamesComboBox");
            if (imageComboBox != null)
                imageComboBox.SelectionChanged += ImageFileNamesComboBox_OnSelectionChanged;
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
                var path = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                                                                           {
                                                                               Title = I18NExtension.Translate(LangKeys.newToolStripMenuItem)
                                                                           });
                Console.WriteLine(path[0].Path.AbsolutePath);
                if (ViewModel != null)
                    ViewModel.NewTranslationPath = path[0].Path.AbsolutePath;
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
                var opentranslationfile = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                                              {
                                                  Title = I18NExtension.Translate(LangKeys.openToolStripMenuItem),
                                                  FileTypeFilter = new List<FilePickerFileType> { FilePickerFileTypes.TextPlain }
                                              });
                Console.WriteLine(opentranslationfile[0].Path.AbsolutePath);
                if (ViewModel != null)
                {
                    ViewModel.OpenTranslationFilePath = opentranslationfile[0].Path.AbsolutePath;
                    await ViewModel.LoadTranslationFile(opentranslationfile[0].Path.AbsolutePath);
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
                if (savefile != null && ViewModel != null)
                {
                    var path = savefile.Path.AbsolutePath;
                    await ViewModel.FileSave(path);
                }
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync(ex.Message);
            }
        }

        private async void ImageFileNamesComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (picViewerControl == null || ViewModel == null) return;
            var selectedFile = ViewModel.SelectedImageFile;
            if (string.IsNullOrEmpty(selectedFile))
            {
                picViewerControl.Source = null;
                return;
            }
            // 加载图片
            if (File.Exists(selectedFile))
            {
                try
                {
                    picViewerControl.Source = new Bitmap(selectedFile);
                }
                catch
                {
                    picViewerControl.Source = null;
                }
            }
            else
            {
                picViewerControl.Source = null;
            }
            // PicViewer 没有 Labels 属性，移除相关赋值
        }

    }
}