using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
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

namespace LabelPlus_Next.Update;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureLogging();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            DisableAvaloniaDataAnnotationValidation();
            // Build DI
            var services = new ServiceCollection();
            services.AddSingleton<IMessageService, MessageService>();
            services.AddSingleton<IUpdaterService, UpdaterService>();
            services.AddSingleton<MainWindowViewModel>();
            var provider = services.BuildServiceProvider();

            var vm = provider.GetRequiredService<MainWindowViewModel>();
            // Support optional override dir and waitpid via command line
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
            if (!string.IsNullOrWhiteSpace(overrideDir)) { try { vm.OverrideAppDir(overrideDir); } catch { } }
            if (waitPid is not null) { try { vm.SetWaitPid(waitPid.Value); } catch { } }
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
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
        catch
        {
            // ignore setup errors
        }
    }
}
