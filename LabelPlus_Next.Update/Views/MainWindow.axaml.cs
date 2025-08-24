using Avalonia.Controls;
using System;
using System.Diagnostics;
using LabelPlus_Next.Update.ViewModels;
using Ursa.Controls;

namespace LabelPlus_Next.Update.Views
{
    public partial class MainWindow : UrsaWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private async void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var args = Environment.GetCommandLineArgs();
                int waitPid = 0;
                string? targetDir = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--waitpid" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) waitPid = p;
                    if (args[i] == "--target" && i + 1 < args.Length) targetDir = args[i + 1];
                    if (args[i] == "--targetpath" && i + 1 < args.Length) targetDir = args[i + 1];
                }
                if (waitPid > 0)
                {
                    try { var proc = Process.GetProcessById(waitPid); proc.WaitForExit(); }
                    catch { }
                }
                if (!string.IsNullOrWhiteSpace(targetDir)) vm.OverrideAppDir(targetDir);
                await vm.RunUpdateAsyncPublic();
            }
        }
    }
}