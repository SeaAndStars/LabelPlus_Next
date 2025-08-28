using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.ViewModels;
using LabelPlus_Next.Views;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System.Diagnostics.CodeAnalysis;

namespace LabelPlus_Next;

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
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
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

            // File targets per level
            FileTarget mkFile(string name, string file) => new(name) { FileName = Layout.FromString(Path.Combine(logDir, file)), Layout = fileLayout };
            var traceFile = mkFile("log_trace", "${shortdate}_trace.log");
            var debugFile = mkFile("log_debug", "${shortdate}_debug.log");
            var infoFile = mkFile("log_info", "${shortdate}_info.log");
            var warnFile = mkFile("log_warn", "${shortdate}_warn.log");
            var errorFile = mkFile("log_error", "${shortdate}_error.log");
            var fatalFile = mkFile("log_fatal", "${shortdate}_fatal.log");

            // Console and debug targets
            var consoleTarget = new ConsoleTarget("console") { Layout = consoleLayout }; // colored console optional
            var debugTarget = new DebuggerTarget("debug") { Layout = consoleLayout };

            // Wrap with async for high performance
            AsyncTargetWrapper wrap(Target t) => new(t) { QueueLimit = 10000, OverflowAction = AsyncTargetWrapperOverflowAction.Discard, BatchSize = 200, TimeToSleepBetweenBatches = 0 };

            var traceAsync = wrap(traceFile);
            var debugAsync = wrap(debugFile);
            var infoAsync = wrap(infoFile);
            var warnAsync = wrap(warnFile);
            var errorAsync = wrap(errorFile);
            var fatalAsync = wrap(fatalFile);
            var consoleAsync = wrap(consoleTarget);
            var debugWinAsync = wrap(debugTarget);

            config.AddTarget(traceAsync);
            config.AddTarget(debugAsync);
            config.AddTarget(infoAsync);
            config.AddTarget(warnAsync);
            config.AddTarget(errorAsync);
            config.AddTarget(fatalAsync);
            config.AddTarget(consoleAsync);
            config.AddTarget(debugWinAsync);

            // Rules
            config.AddRule(LogLevel.Trace, LogLevel.Trace, traceAsync);
            config.AddRule(LogLevel.Debug, LogLevel.Debug, debugAsync);
            config.AddRule(LogLevel.Info, LogLevel.Info, infoAsync);
            config.AddRule(LogLevel.Warn, LogLevel.Warn, warnAsync);
            config.AddRule(LogLevel.Error, LogLevel.Error, errorAsync);
            config.AddRule(LogLevel.Fatal, LogLevel.Fatal, fatalAsync);

            // Also send everything >= Info to console/debug window
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleAsync);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, debugWinAsync);

            LogManager.Configuration = config;
            LogManager.GetCurrentClassLogger().Info("NLog initialized. Logs at {dir}", logDir);
        }
        catch
        {
            // ignore logging setup errors
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Accessing Avalonia BindingPlugins.DataValidators only to remove DataAnnotationsValidationPlugin; safe for trimming.")]
    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
