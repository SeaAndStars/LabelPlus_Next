using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.Update.ViewModels;
using LabelPlus_Next.Update.Views;
using LabelPlus_Next.Update.Services;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Threading; // for Dispatcher
using System.Threading.Tasks; // TaskScheduler
using System.IO.Compression; // Zip
using Avalonia.Controls; // UI elements
using Ursa.Controls; // UrsaWindow
using System.Text; // Encoding

namespace LabelPlus_Next.Update;

public class App : Application
{
    private static readonly Logger GLogger = LogManager.GetCurrentClassLogger();
    private static bool _isHandlingFatal;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureLogging();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RegisterGlobalExceptionHandlers();
            // Avalonia 12 disables data-annotations validation by default.
            // Build DI
            var services = new ServiceCollection();
            services.AddSingleton<IMessageService, MessageService>();
            services.AddSingleton<IUpdaterService, UpdaterService>();
            services.AddSingleton<MainWindowViewModel>();
            var provider = services.BuildServiceProvider();

            var vm = provider.GetRequiredService<MainWindowViewModel>();
            var args = desktop.Args ?? Array.Empty<string>();
            string? overrideDir = null;
            int? waitPid = null;
            foreach (var a in args.Select((val, idx) => (val, idx)))
            {
                var s = a.val;
                if (s.StartsWith("--appdir=", StringComparison.OrdinalIgnoreCase)) overrideDir = s.Split('=')[1];
                if (s.StartsWith("--target=", StringComparison.OrdinalIgnoreCase)) overrideDir = s.Split('=')[1];
                if (s.StartsWith("--targetpath=", StringComparison.OrdinalIgnoreCase)) overrideDir = s.Split('=')[1];
                if (s.StartsWith("--waitpid=", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Split('=')[1], out var pEq)) waitPid = pEq;
                if (string.Equals(s, "--waitpid", StringComparison.OrdinalIgnoreCase) && a.idx + 1 < args.Length && int.TryParse(args[a.idx + 1], out var pSp)) waitPid = pSp;
                if (string.Equals(s, "--target", StringComparison.OrdinalIgnoreCase) && a.idx + 1 < args.Length) overrideDir = args[a.idx + 1];
                if (string.Equals(s, "--targetpath", StringComparison.OrdinalIgnoreCase) && a.idx + 1 < args.Length) overrideDir = args[a.idx + 1];
                if (string.Equals(s, "--appdir", StringComparison.OrdinalIgnoreCase) && a.idx + 1 < args.Length) overrideDir = args[a.idx + 1];
            }
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                try { vm.OverrideAppDir(overrideDir); }
                catch (ArgumentException ex)
                {
                    GLogger.Warn(ex, "Invalid override directory specified: {overrideDir}", overrideDir);
                }
                catch (Exception ex)
                {
                    GLogger.Error(ex, "Failed to apply override directory: {overrideDir}", overrideDir);
                    throw;
                }
            }
            if (waitPid is not null)
            {
                try { vm.SetWaitPid(waitPid.Value); }
                catch (ArgumentException ex)
                {
                    GLogger.Warn(ex, "Invalid wait PID specified: {waitPid}", waitPid);
                }
                catch (Exception ex)
                {
                    GLogger.Error(ex, "Failed to set wait PID: {waitPid}", waitPid);
                    throw;
                }
            }
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HandleGlobalException(e.ExceptionObject as Exception ?? new Exception("Unknown domain exception"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            HandleGlobalException(e.Exception);
        };
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            HandleGlobalException(e.Exception);
        };
    }

    private void HandleGlobalException(Exception ex)
    {
        if (_isHandlingFatal) return;
        _isHandlingFatal = true;
        try
        {
            GLogger.Fatal(ex, "Updater 未处理的致命异常");
            var (zipPath, msg) = TryZipLogs();
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var panel = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
                    panel.Children.Add(new TextBlock { Text = "更新程序发生未处理的严重错误: " + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    if (!string.IsNullOrEmpty(msg)) panel.Children.Add(new TextBlock { Text = msg, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    if (!string.IsNullOrEmpty(zipPath))
                    {
                        panel.Children.Add(new TextBlock { Text = "日志已打包:", FontWeight = Avalonia.Media.FontWeight.Bold });
                        panel.Children.Add(new TextBlock { Text = zipPath, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    }
                    panel.Children.Add(new TextBlock { Text = "程序即将关闭。", Margin = new Thickness(0, 8, 0, 0) });
                    var btn = new Button { Content = "关闭" };
                    var btnRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                    btnRow.Children.Add(btn);
                    panel.Children.Add(btnRow);

                    var win = new UrsaWindow
                    {
                        Title = "更新程序错误",
                        Width = 520,
                        Height = 360,
                        CanResize = false,
                        Content = new ScrollViewer { Content = panel }
                    };
                    btn.Click += (_, __) => win.Close();

                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d && d.MainWindow is not null)
                    {
                        await win.ShowDialog(d.MainWindow);
                    }
                    else
                    {
                        win.Show();
                        await Task.Delay(3000);
                    }
                }
                catch (Exception dialogEx)
                {
                    GLogger.Error(dialogEx, "Failed to display fatal error dialog");
                }
                finally
                {
                    Environment.Exit(1);
                }
            });
        }
        catch (Exception outerEx)
        {
            GLogger.Fatal(outerEx, "Failed handling fatal updater exception");
            Environment.Exit(1);
        }
    }

    private static (string? zipPath, string message) TryZipLogs()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var logDir = Path.Combine(baseDir, "logs");
            if (!Directory.Exists(logDir)) return (null, "未找到日志目录。");
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var zipPath = Path.Combine(baseDir, $"logs_{ts}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(logDir, zipPath, CompressionLevel.Optimal, false, Encoding.UTF8);
            return (zipPath, "已打包日志，可将此文件发送给开发者以便排查问题。");
        }
        catch (Exception zex)
        {
            GLogger.Warn(zex, "Updater 打包日志失败");
            return (null, "打包日志失败: " + zex.Message);
        }
    }

    private void ConfigureLogging()
    {
        try
        {
            if (LogManager.Configuration != null) return; // already configured

            var config = new LoggingConfiguration();
            var baseDir = AppContext.BaseDirectory;
            var logDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(logDir);

            var fileLayout = Layout.FromString("${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}");
            var consoleLayout = Layout.FromString("${time} [${level:uppercase=true}] ${message}");

            var fileTarget = new FileTarget("file")
            {
                FileName = Path.Combine(logDir, "update_${shortdate}.log"),
                Layout = fileLayout,
                KeepFileOpen = false
            };
            var consoleTarget = new ConsoleTarget("console") { Layout = consoleLayout };

            var fileAsync = new AsyncTargetWrapper(fileTarget) { QueueLimit = 5000, OverflowAction = AsyncTargetWrapperOverflowAction.Discard, BatchSize = 200 };
            var consoleAsync = new AsyncTargetWrapper(consoleTarget) { QueueLimit = 2000, OverflowAction = AsyncTargetWrapperOverflowAction.Discard, BatchSize = 200 };

            config.AddTarget(fileAsync);
            config.AddTarget(consoleAsync);

            config.AddRule(LogLevel.Info, LogLevel.Fatal, fileAsync);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleAsync);

            LogManager.Configuration = config;
            LogManager.GetCurrentClassLogger().Info("NLog initialized (Update)");
        }
        catch (Exception ex)
        {
            GLogger.Error(ex, "Failed to configure updater logging");
        }
    }
}
