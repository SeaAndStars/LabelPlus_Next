using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LabelPlus_Next.Views.Pages;
using Ursa.Controls;

namespace LabelPlus_Next.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
       
    }





    private void About_OnClick(object? sender, RoutedEventArgs e)
    {
        
    }

    private void View_Help_OnClick(object? sender, RoutedEventArgs e)
    {
        
    }

    private void Imagine_manager_OnClick(object? sender, RoutedEventArgs e)
    {
        var imgPage = new ImageManager();
        imgPage.Show();
    }

    private void Output_OnClick(object? sender, RoutedEventArgs e)
    {
        var imgoutputpage = new ImageOutput();
        imgoutputpage.Show();
    }
}
