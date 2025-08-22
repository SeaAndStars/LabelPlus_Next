using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Pages;

namespace LabelPlus_Next.Views
{
    public partial class MainWindow : Window
    {
        private ContentControl? _navHost;

        public MainWindow()
        {
            InitializeComponent();
            _navHost = this.FindControl<ContentControl>("NavContent");
            SetContent("translate");
        }

        private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // For Ursa NavMenu, items are Controls with Tag
            string? tag = null;
            if (e.AddedItems is { Count: > 0 } && e.AddedItems[0] is Control ctrl)
                tag = ctrl.Tag as string;
            SetContent(tag);
        }

        private void SetContent(string? tag)
        {
            var host = _navHost;
            if (host is null) return;
            switch (tag)
            {
                case "translate":
                    host.Content = new TranslateView { DataContext = new TranslateViewModel() };
                    break;
                case "proof":
                    host.Content = new SimpleTextPage("У��ҳ��");
                    break;
                case "collab":
                    host.Content = new SimpleTextPage("Э��ҳ��");
                    break;
                case "upload":
                    host.Content = new SimpleTextPage("�ϴ�ҳ��");
                    break;
                case "deliver":
                    host.Content = new SimpleTextPage("����ҳ��");
                    break;
                case "settings":
                    host.Content = new SimpleTextPage("����");
                    break;
                default:
                    host.Content = new SimpleTextPage("��ӭ");
                    break;
            }
        }
    }
}