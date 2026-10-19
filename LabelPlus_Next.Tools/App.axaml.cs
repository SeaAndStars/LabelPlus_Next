using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.Tools.ViewModels;
using LabelPlus_Next.Tools.Views;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using System.IO.Compression;
using Avalonia.Controls;
using Ursa.Controls;
using System.Text; // for Encoding.UTF8 in zip
using System.Threading.Tasks; // Task, TaskScheduler


namespace LabelPlus_Next.Tools;

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
            RegisterGlobalExceptionHandlers(desktop);
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            // Avalonia 12 disables data-annotations validation by default.
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            // Flush any debounced configuration edit when the window/app is closed.
            desktop.ShutdownRequested += (_, _) => viewModel.FlushSettings();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterGlobalExceptionHandlers(IClassicDesktopStyleApplicationLifetime desktop)
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
        if (_isHandlingFatal) return; // prevent recursion
        _isHandlingFatal = true;
        try
        {
            GLogger.Fatal(ex, "未处理的致命异常");
            var (zipPath, msg) = TryZipLogs();
            // show UI dialog on UI thread
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var contentPanel = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
                    contentPanel.Children.Add(new TextBlock { Text = "程序发生未处理的严重错误: " + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    if (!string.IsNullOrEmpty(msg))
                        contentPanel.Children.Add(new TextBlock { Text = msg, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    if (!string.IsNullOrEmpty(zipPath))
                    {
                        contentPanel.Children.Add(new TextBlock { Text = "日志已打包: ", FontWeight = Avalonia.Media.FontWeight.Bold });
                        contentPanel.Children.Add(new TextBlock { Text = zipPath, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    }
                    contentPanel.Children.Add(new TextBlock { Text = "程序即将关闭。", Margin = new Thickness(0, 8, 0, 0) });
                    var btn = new Button { Content = "关闭" };
                    var btnPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                    btnPanel.Children.Add(btn);
                    contentPanel.Children.Add(btnPanel);

                    var win = new UrsaWindow
                    {
                        Title = "发生严重错误",
                        Width = 520,
                        Height = 360,
                        CanResize = false,
                        Content = new ScrollViewer { Content = contentPanel }
                    };
                    btn.Click += (_, __) => win.Close();

                    // Try show centered relative to main window
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
                    GLogger.Error(dialogEx, "显示致命错误弹窗失败");
                }
                finally
                {
                    Environment.Exit(1);
                }
            });
        }
        catch (Exception outerEx)
        {
            GLogger.Fatal(outerEx, "处理致命异常时失败");
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
            GLogger.Warn(zex, "打包日志失败");
            return (null, "打包日志失败: " + zex.Message);
        }
    }

    private void ConfigureLogging()
    {
        try
        {
            if (LogManager.Configuration != null) return;

            var config = new LoggingConfiguration();
            var baseDir = AppContext.BaseDirectory;
            var logDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(logDir);

            var layout = Layout.FromString("${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}");
            var fileTarget = new FileTarget("file")
            {
                FileName = Path.Combine(logDir, "tools_${shortdate}.log"),
                Layout = layout,
                KeepFileOpen = false
            };
            var consoleTarget = new ConsoleTarget("console") { Layout = Layout.FromString("${time} [${level:uppercase=true}] ${message}") };

            var fileAsync = new AsyncTargetWrapper(fileTarget) { QueueLimit = 5000, OverflowAction = AsyncTargetWrapperOverflowAction.Discard, BatchSize = 200 };
            var consoleAsync = new AsyncTargetWrapper(consoleTarget) { QueueLimit = 2000, OverflowAction = AsyncTargetWrapperOverflowAction.Discard, BatchSize = 200 };

            config.AddTarget(fileAsync);
            config.AddTarget(consoleAsync);

            config.AddRule(LogLevel.Info, LogLevel.Fatal, fileAsync);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleAsync);

            LogManager.Configuration = config;
            LogManager.GetCurrentClassLogger().Info("NLog initialized");
        }
        catch
        {
            // ignore
        }
    }
}
