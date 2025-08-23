using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.Tools.ViewModels;
using LabelPlus_Next.Tools.Views;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Layouts;
using NLog.Targets.Wrappers;
using System;
using System.IO;
using System.Linq;

namespace LabelPlus_Next.Tools
{
    public partial class App : Application
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
                    DataContext = new MainWindowViewModel(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

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

        private void ConfigureLogging()
        {
            try
            {
                if (LogManager.Configuration != null) return;

                var config = new LoggingConfiguration();
                var baseDir = AppContext.BaseDirectory;
                var logDir = Path.Combine(baseDir, "logs");
                Directory.CreateDirectory(logDir);

                Layout layout = Layout.FromString("${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}");
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
}