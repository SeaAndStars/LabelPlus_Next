using System;
using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views.Pages;
using Ursa.Controls;

namespace LabelPlus_Next.Views
{
    public partial class MainWindow : UrsaWindow
    {
        private ContentControl? _navHost;
        private NavMenu? _menuMain;
        private NavMenu? _menuFooter;

        public MainWindow()
        {
            InitializeComponent();
            _navHost = this.FindControl<ContentControl>("NavContent");
            _menuMain = this.FindControl<NavMenu>("NavMenuMain");
            _menuFooter = this.FindControl<NavMenu>("NavMenuFooter");

            // Set default page and selection
            SetContent("translate");
            SelectMenuItemByTag(_menuMain, "translate");
            ClearMenuSelection(_menuFooter);
        }

        private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Ignore events that only clear selection (no new item added)
            if (e.AddedItems is not { Count: > 0 }) return;

            // For Ursa NavMenu, items are Controls with Tag
            string? tag = null;
            if (e.AddedItems[0] is Control ctrl)
                tag = ctrl.Tag as string;

            // Keep main/footer selections mutually exclusive and fully clear highlight
            if (sender == _menuMain)
            {
                ClearMenuSelection(_menuFooter);
            }
            else if (sender == _menuFooter)
            {
                ClearMenuSelection(_menuMain);
            }

            SetContent(tag);
        }

        private static void ClearMenuSelection(NavMenu? menu)
        {
            if (menu is null) return;
            menu.SelectedItem = null;
        }

        private static void SelectMenuItemByTag(NavMenu? menu, string tag)
        {
            if (menu?.Items is not IEnumerable items) return;
            var match = items.Cast<object>()
                             .OfType<Control>()
                             .FirstOrDefault(c => string.Equals(c.Tag as string, tag, StringComparison.Ordinal));
            if (match != null)
            {
                menu.SelectedItem = match;
            }
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
                    host.Content = new SimpleTextPage("校对页面");
                    break;
                case "collab":
                    host.Content = new SimpleTextPage("协作页面");
                    break;
                case "upload":
                    host.Content = new SimpleTextPage("上传页面");
                    break;
                case "deliver":
                    host.Content = new SimpleTextPage("交付页面");
                    break;
                case "settings":
                    host.Content = new SettingsPage { DataContext = new SettingsViewModel() };
                    break;
                default:
                    host.Content = new SimpleTextPage("欢迎");
                    break;
            }
        }
    }
}