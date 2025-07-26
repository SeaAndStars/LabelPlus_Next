using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using LabelPlus_Next.Lang;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Pages;
using Ursa.Controls;

namespace LabelPlus_Next.Views;

public partial class MainWindow : Window
{
    private ComboBox? langComboBox; // 添加空值安全字段

    public MainWindow()
    {
        InitializeComponent();
        langComboBox = this.FindControl<ComboBox>("LangComboBox");
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
}