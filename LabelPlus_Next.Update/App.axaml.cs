using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using LabelPlus_Next.Update.ViewModels;
using LabelPlus_Next.Update.Views;
using System;
using System.IO;
using System.Linq;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace LabelPlus_Next.Update
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

                Layout fileLayout = Layout.FromString("${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}");
                Layout consoleLayout = Layout.FromString("${time} [${level:uppercase=true}] ${message}");

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
}