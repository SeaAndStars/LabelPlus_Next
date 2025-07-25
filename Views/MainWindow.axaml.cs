using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using LabelPlus_Next.ViewModels;
using static LabelPlus_Next.ViewModels.MainWindowViewModel;    
using LabelPlus_Next.Views.Pages;

namespace LabelPlus_Next.Views;


public partial class MainWindow : Window
{   
    private ComboBox? langComboBox; // 添加空值安全字段

    public MainWindow()
    {
        InitializeComponent();
        // 将初始化逻辑移到构造函数后续处理
        

    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        // 通过安全方式获取控件实例
        langComboBox = this.FindControl<ComboBox>("LangComboBox");
    }

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
            if (sender is not ComboBox comboBox || 
                comboBox.SelectedItem is not string selectedLang)
            {
                return;
            }
        
            I18NExtension.Culture = new CultureInfo(selectedLang);
        }
     
        catch (CultureNotFoundException)
        {
            // 默认回退到系统文化
            I18NExtension.Culture = CultureInfo.CurrentUICulture;
        }// 使用安全类型转换
    }
}