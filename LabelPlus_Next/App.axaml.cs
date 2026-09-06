using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
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
using Microsoft.Extensions.DependencyInjection;
using LabelPlus_Next.Services;
using Avalonia.Threading;
using System.IO.Compression;
using Ursa.Controls;
using UrsaWindowNotificationManager = Ursa.Controls.WindowNotificationManager;
using UrsaNotification = Ursa.Controls.Notification;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics; // added for opening file explorer
using System.Runtime.InteropServices; // for MiniDump & runtime info
using System.IO;
using System.Linq;
using System.Management; // added for detailed WMI queries
using System.Reflection;
using System.Globalization;
using System.Runtime; // for GCSettings
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using LabelPlus_Next.DeeplinkClients;

namespace LabelPlus_Next;

public class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;
    private static readonly Logger GLogger = LogManager.GetCurrentClassLogger();
    private bool _startupUpdateHookRegistered;
    private bool _startupUpdateTriggered;
    // Mark field as used via conditional attribute-like helper to silence CS0414 in DEBUG builds
#if DEBUG
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Compiler", "CS0414", Justification = "Used in release build for global exception state")]
#endif
#pragma warning disable CS0414
    private static bool _isHandlingFatal; // release: used by global exception handling
#pragma warning restore CS0414
#pragma warning disable CS0169, CS0649, CS0414
    private static readonly bool __suppressFatalFlagWarning = _isHandlingFatal;
#pragma warning restore CS0169, CS0649, CS0414

    public static bool IsTestMode { get; set; }
    public static Action<int> ExitAction { get; set; } = code =>
    {
        try { LogManager.Flush(TimeSpan.FromSeconds(2)); LogManager.Shutdown(); }
        catch (Exception flushEx) { GLogger.Warn(flushEx, "Failed to flush logs before exit"); }
        Environment.Exit(code);
    };

    public static void ResetGlobalExceptionTestState()
    {
        _isHandlingFatal = false;
        IsTestMode = false;
        ExitAction = code =>
        {
            try { LogManager.Flush(TimeSpan.FromSeconds(2)); LogManager.Shutdown(); }
            catch (Exception flushEx) { GLogger.Warn(flushEx, "Failed to flush logs before exit (test reset)"); }
            Environment.Exit(code);
        };
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureLogging();
        if (Services is null)
        {
            var sc = new ServiceCollection();
            sc.AddSingleton<ITopLevelProvider, TopLevelProvider>();
            sc.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
            sc.AddSingleton<ISettingsService, JsonSettingsService>();
            sc.AddSingleton<IUpdateService, WebDavUpdateService>();
            sc.AddSingleton<SettingsViewModel>();
            sc.AddTransient<MainWindowViewModel>();
            sc.AddTransient<TranslateViewModel>();
            sc.AddTransient<TeamWorkViewModel>();
            sc.AddTransient<UploadViewModel>();
            sc.AddTransient<ImageOutputViewModel>();
            sc.AddSingleton<MainProjectsViewModel>();
            Services = sc.BuildServiceProvider();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if !DEBUG
            RegisterGlobalExceptionHandlers(desktop);
#else
            GLogger.Info("[DEBUG] Global exception handlers not registered (debug build). Exceptions will bubble to debugger.");
#endif
            DisableAvaloniaDataAnnotationValidation();
            // Start IPC server early so forwarded activations from secondary processes can be received quickly
            _ = Task.Run(async () => await StartIpcServerAsync().ConfigureAwait(false));
            // Best-effort: try register labelplus scheme for current user in background
            _ = Task.Run(async () => await UrlSchemeRegistrar.TryRegisterLabelPlusSchemeAsync(Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory).ConfigureAwait(false));
            // Flush any previously queued deeplink acks (background, non-blocking)
            _ = Task.Run(async () => await DeeplinkAckHelper.FlushQueuedAcksAsync().ConfigureAwait(false));

            // Inspect command line early: if launched via labelplus://open?noProject=1 then open MainWindow directly
            var args = Environment.GetCommandLineArgs();
            var uriArg = args.FirstOrDefault(a => a.StartsWith("labelplus://", StringComparison.OrdinalIgnoreCase));
            var skipProjects = false;
            if (!string.IsNullOrEmpty(uriArg))
            {
                try
                {
                    var u = new Uri(uriArg);
                    var q = u.Query ?? string.Empty; // includes leading '?'
                    if (!string.IsNullOrEmpty(q) && (q.IndexOf("noProject=1", StringComparison.OrdinalIgnoreCase) >= 0 || q.IndexOf("noProject=true", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        skipProjects = true;
                    }
                }
                catch { /* ignore malformed uri */ }
            }

            if (skipProjects)
            {
                // Open main window immediately and allow protocol handler to populate VM
                var main = new MainWindow { DataContext = Services.GetRequiredService<MainWindowViewModel>() };
                desktop.MainWindow = main;
                RegisterStartupUpdateCheck(desktop);
                if (!string.IsNullOrEmpty(uriArg)) _ = ProcessProtocolActivationAsync(uriArg);
            }
            else
            {
                desktop.MainWindow = new MainProjects();
                RegisterStartupUpdateCheck(desktop);
                if (!string.IsNullOrEmpty(uriArg)) _ = ProcessProtocolActivationAsync(uriArg);
            }
            // Start an IPC server to receive protocol activations forwarded from secondary processes
            _ = Task.Run(() => StartIpcServerAsync()).ConfigureAwait(false);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
#if !DEBUG
            RegisterGlobalExceptionHandlers();
#else
            GLogger.Info("[DEBUG] Global exception handlers not registered (debug build). Exceptions will bubble to debugger.");
#endif
            singleViewPlatform.MainView = new MainProjects();
            _ = RunStartupUpdateCheckAsync(null);
        }

        base.OnFrameworkInitializationCompleted();
    }

#if !DEBUG
    private void RegisterGlobalExceptionHandlers(IClassicDesktopStyleApplicationLifetime? lifetime = null)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HandleGlobalException(e.ExceptionObject as Exception ?? new Exception("Unknown domain exception"), e.IsTerminating, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) => { e.SetObserved(); HandleGlobalException(e.Exception, false, "TaskScheduler"); };
        Dispatcher.UIThread.UnhandledException += (_, e) => { e.Handled = true; HandleGlobalException(e.Exception, false, "UI"); };
    }
#else
    // Stub in debug – no-op so developer can see raw exceptions
    private void RegisterGlobalExceptionHandlers(IClassicDesktopStyleApplicationLifetime? lifetime = null) { }
#endif

#if !DEBUG
    internal void HandleGlobalException(Exception ex, bool isTerminating = false, string origin = "Unknown")
    {
        if (_isHandlingFatal && isTerminating) return;
        _isHandlingFatal = true;
        var dump = ex.ToString();
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "minimal_fatal.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Origin={origin} Terminating={isTerminating}\n{dump}\n\n");
        }
        catch (IOException ioEx) { GLogger.Warn(ioEx, "Write minimal_fatal.log IO failed"); }
        catch (UnauthorizedAccessException uaEx) { GLogger.Warn(uaEx, "Write minimal_fatal.log unauthorized"); }
        try { GLogger.Fatal(ex, "未处理异常 Origin={origin} Terminating={isTerminating}"); }
        catch (Exception logEx) { GLogger.Warn(logEx, "Logging fatal exception failed"); }
        // Prepare report zip immediately
        string? zipPath = null;
        try { zipPath = PrepareLogReport(dump, origin); }
        catch (Exception prepEx) { GLogger.Warn(prepEx, "PrepareLogReport failed"); }
        if (isTerminating || IsTestMode || Environment.GetEnvironmentVariable("LP_CRASH_NO_UI") == "1")
        {
            try { LogManager.Flush(TimeSpan.FromSeconds(2)); LogManager.Shutdown(); }
            catch (Exception flushEx) { GLogger.Warn(flushEx, "Flush/Shutdown after fatal failed"); }
            ExitAction(1); return;
        }
        if (Dispatcher.UIThread.CheckAccess()) ShowCrashWindow(dump, origin, zipPath);
        else _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            try { ShowCrashWindow(dump, origin, zipPath); }
            catch (Exception uiEx) {
                try { LogManager.Flush(TimeSpan.FromSeconds(2)); LogManager.Shutdown(); }
                catch (Exception flushEx) { GLogger.Warn(flushEx, "Flush/Shutdown inside UI exception failed"); }
                GLogger.Warn(uiEx, "ShowCrashWindow failed");
                ExitAction(1);
            }
        }, DispatcherPriority.Send);
    }
#else
    // In debug we do not intercept; provide stub to satisfy references (if any)
    internal void HandleGlobalException(Exception ex, bool isTerminating = false, string origin = "Unknown") => throw ex;
#endif

#if !DEBUG
    private string PrepareLogReport(string dump, string origin)
    {
        var baseDir = AppContext.BaseDirectory;
        var logsDir = Path.Combine(baseDir, "logs");
        var reportDir = Path.Combine(baseDir, "log_report");
        Directory.CreateDirectory(reportDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(reportDir, $"log_report_{stamp}.zip");
        string? tempMiniDump = null;
        try { tempMiniDump = TryCreateMiniDump(Path.Combine(reportDir, $"process_{stamp}.dmp")); }
        catch (Exception mdEx) { GLogger.Warn(mdEx, "MiniDump creation failed"); }
        var sysInfo = CollectSystemInfo();
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            if (Directory.Exists(logsDir))
            {
                foreach (var f in Directory.GetFiles(logsDir))
                {
                    try { zip.CreateEntryFromFile(f, "logs/" + Path.GetFileName(f)); }
                    catch (IOException ioEx) { GLogger.Warn(ioEx, "Add log file to zip failed: {file}", f); }
                    catch (UnauthorizedAccessException uaEx) { GLogger.Warn(uaEx, "Access denied adding log file: {file}", f); }
                }
            }
            var minimalFatal = Path.Combine(baseDir, "minimal_fatal.log");
            if (File.Exists(minimalFatal))
            {
                try { zip.CreateEntryFromFile(minimalFatal, "minimal_fatal.log"); }
                catch (Exception mfEx) { GLogger.Warn(mfEx, "Add minimal_fatal.log to zip failed"); }
            }
            try
            {
                var dumpEntry = zip.CreateEntry("exception_dump.txt");
                using var sw = new StreamWriter(dumpEntry.Open(), Encoding.UTF8);
                sw.WriteLine($"Origin: {origin}");
                sw.WriteLine(dump);
            }
            catch (Exception dumpEx) { GLogger.Warn(dumpEx, "Write exception_dump.txt failed"); }
            try
            {
                var sysEntry = zip.CreateEntry("system_info.txt");
                using var sw = new StreamWriter(sysEntry.Open(), Encoding.UTF8);
                sw.Write(sysInfo);
            }
            catch (Exception sysEx) { GLogger.Warn(sysEx, "Write system_info.txt failed"); }
            if (!string.IsNullOrEmpty(tempMiniDump) && File.Exists(tempMiniDump))
            {
                try { zip.CreateEntryFromFile(tempMiniDump, Path.GetFileName(tempMiniDump)); }
                catch (Exception dmpEx) { GLogger.Warn(dmpEx, "Add mini dump failed"); }
            }
        }
        try { if (!string.IsNullOrEmpty(tempMiniDump) && File.Exists(tempMiniDump)) File.Delete(tempMiniDump); }
        catch (IOException ioEx) { GLogger.Warn(ioEx, "Cleanup temp mini dump failed"); }
        return zipPath;
    }

    private static string CollectSystemInfo()
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine("==== SYSTEM / ENVIRONMENT ====");
            sb.AppendLine($"UTC Time: {DateTime.UtcNow:O}");
            sb.AppendLine($"Local Time: {DateTime.Now:O}");
            sb.AppendLine($"OS Version: {Environment.OSVersion}");
            sb.AppendLine($"OS Description: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"Process Architecture: {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"OS Architecture: {RuntimeInformation.OSArchitecture}");
            sb.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"Machine Name: {Environment.MachineName}");
            sb.AppendLine($"User: {Environment.UserName}");
            sb.AppendLine($"Current Directory: {Environment.CurrentDirectory}");
            sb.AppendLine($"Command Line: {Environment.CommandLine}");
            sb.AppendLine($"Processor Count (logical): {Environment.ProcessorCount}");
            sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
            sb.AppendLine($"Current Culture: {CultureInfo.CurrentCulture} / UI: {CultureInfo.CurrentUICulture}");
            sb.AppendLine();

            sb.AppendLine("==== RUNTIME ====");
            try
            {
                try
                {
                    var gcLat = GCSettings.LatencyMode;
                    sb.AppendLine($"GC Latency Mode: {gcLat}");
                    sb.AppendLine($"Server GC: {System.Runtime.GCSettings.IsServerGC}");
                    sb.AppendLine($"LOH Compaction Mode: {GCSettings.LargeObjectHeapCompactionMode}");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: failed to read GC settings");
                }
                sb.AppendLine("Loaded Assemblies:");
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
                {
                    try
                    {
                        var name = asm.GetName();
                        sb.AppendLine($"  - {name.Name} {name.Version}");
                    }
                    catch (Exception ex)
                    {
                        GLogger.Debug(ex, "CollectSystemInfo: failed to read assembly info for {Assembly}", asm.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                GLogger.Debug(ex, "CollectSystemInfo: failed to enumerate runtime assemblies");
            }
            sb.AppendLine();

            sb.AppendLine("==== HARDWARE ====");
            if (OperatingSystem.IsWindows())
            {
                // Extended OS / system details (localized name, manufacturer, model, BIOS, baseboard, security)
                try
                {
                    using var osExt = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
                    foreach (var o in osExt.Get())
                    {
                        sb.AppendLine($"Windows Caption: {o["Caption"]} Version={o["Version"]} Build={o["BuildNumber"]} Arch={o["OSArchitecture"]}");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_OperatingSystem query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: Windows OS extended info retrieval failed");
                }
                try
                {
                    using var csSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, SystemType, TotalPhysicalMemory FROM Win32_ComputerSystem");
                    foreach (var cs in csSearcher.Get())
                    {
                        double totalPhys = 0;
                        try { totalPhys = Convert.ToDouble(cs["TotalPhysicalMemory"]) / 1024 / 1024 / 1024; }
                        catch (Exception convEx) { GLogger.Debug(convEx, "CollectSystemInfo: failed to convert total physical memory"); }
                        sb.AppendLine($"System: Manufacturer={cs["Manufacturer"]} Model={cs["Model"]} Type={cs["SystemType"]} InstalledRAM={totalPhys:F1}GB");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_ComputerSystem query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: computer system info retrieval failed");
                }
                try
                {
                    using var biosSearcher = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion, Version, ReleaseDate FROM Win32_BIOS");
                    foreach (var b in biosSearcher.Get())
                    {
                        sb.AppendLine($"BIOS: Mfg={b["Manufacturer"]} SMBIOS={b["SMBIOSBIOSVersion"]} Ver={b["Version"]} Release={b["ReleaseDate"]}");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_BIOS query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: BIOS info retrieval failed");
                }
                try
                {
                    using var boardSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard");
                    foreach (var bb in boardSearcher.Get())
                    {
                        sb.AppendLine($"BaseBoard: Mfg={bb["Manufacturer"]} Product={bb["Product"]} Ver={bb["Version"]} Serial={bb["SerialNumber"]}");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_BaseBoard query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: baseboard info retrieval failed");
                }
                // Secure Boot
                try
                {
                    var scope = new ManagementScope(@"\\\\.\\root\\Microsoft\\Windows\\SecureBoot");
                    scope.Connect();
                    using var secureSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT SecureBootEnabled FROM MS_SecureBoot"));
                    foreach (var s in secureSearcher.Get()) sb.AppendLine($"SecureBootEnabled: {s["SecureBootEnabled"]}");
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: SecureBoot query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: secure boot info retrieval failed");
                }
                // Device Guard / VBS status
                try
                {
                    var scope = new ManagementScope(@"\\\\.\\root\\Microsoft\\Windows\\DeviceGuard");
                    scope.Connect();
                    using var dgSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_DeviceGuard"));
                    foreach (var dg in dgSearcher.Get())
                    {
                        sb.AppendLine($"DeviceGuard: VBS={dg["VirtualizationBasedSecurityStatus"]} ServicesConfigured={string.Join(',', (ushort[]) (dg["SecurityServicesConfigured"] ?? Array.Empty<ushort>()))} ServicesRunning={string.Join(',', (ushort[]) (dg["SecurityServicesRunning"] ?? Array.Empty<ushort>()))}");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: DeviceGuard query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: device guard info retrieval failed");
                }

                // CPU, Memory, OS build, GPU, Disk, Network via WMI
                try
                {
                    using var cpuSearcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                    foreach (var cpu in cpuSearcher.Get())
                    {
                        sb.AppendLine($"CPU: {cpu["Name"]} Cores={cpu["NumberOfCores"]} Logical={cpu["NumberOfLogicalProcessors"]} MaxMHz={cpu["MaxClockSpeed"]}");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_Processor query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: CPU info retrieval failed");
                }
                try
                {
                    using var memSearcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (var os in memSearcher.Get())
                    {
                        double totalMB = Convert.ToDouble(os["TotalVisibleMemorySize"]) / 1024d;
                        double freeMB = Convert.ToDouble(os["FreePhysicalMemory"]) / 1024d;
                        sb.AppendLine($"Memory: Total={totalMB:F0}MB Free={freeMB:F0}MB");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_OperatingSystem memory query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: memory info retrieval failed");
                }
                try
                {
                    using var gpuSearcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
                    foreach (var g in gpuSearcher.Get())
                    {
                        long ram = 0;
                        try { ram = Convert.ToInt64(g["AdapterRAM"]); }
                        catch (Exception convEx) { GLogger.Debug(convEx, "CollectSystemInfo: GPU RAM conversion failed"); }
                        sb.AppendLine($"GPU: {g["Name"]} Driver={g["DriverVersion"]} VRAM={(ram/1024/1024)}MB");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_VideoController query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: GPU info retrieval failed");
                }
                try
                {
                    using var diskSearcher = new ManagementObjectSearcher("SELECT Model, SerialNumber, Size, FirmwareRevision, InterfaceType FROM Win32_DiskDrive");
                    foreach (var d in diskSearcher.Get())
                    {
                        long size = 0;
                        try { size = Convert.ToInt64(d["Size"]); }
                        catch (Exception convEx) { GLogger.Debug(convEx, "CollectSystemInfo: disk size conversion failed"); }
                        sb.AppendLine($"Disk: Model={d["Model"]} Serial={d["SerialNumber"]} Size={(size/1024/1024/1024)}GB FW={d["FirmwareRevision"]} Iface={d["InterfaceType"]}");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_DiskDrive query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: disk info retrieval failed");
                }
                try
                {
                    using var netSearcher = new ManagementObjectSearcher("SELECT Name, MACAddress, Speed FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True AND MACAddress IS NOT NULL");
                    foreach (var n in netSearcher.Get())
                    {
                        long sp = 0;
                        try { sp = Convert.ToInt64(n["Speed"]); }
                        catch (Exception convEx) { GLogger.Debug(convEx, "CollectSystemInfo: NIC speed conversion failed"); }
                        sb.AppendLine($"NIC: {n["Name"]} MAC={n["MACAddress"]} Speed={(sp/1_000_000)}Mbps");
                    }
                }
                catch (ManagementException mex)
                {
                    GLogger.Debug(mex, "CollectSystemInfo: Win32_NetworkAdapter query failed");
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: network adapter info retrieval failed");
                }
                // Route table (Windows)
                try
                {
                    sb.AppendLine("-- Route Table (route print -4) --");
                    var route4 = RunProcessCapture("cmd.exe", "/c route print -4");
                    sb.AppendLine(LimitLines(route4, 150));
                    sb.AppendLine("-- Route Table (route print -6) --");
                    var route6 = RunProcessCapture("cmd.exe", "/c route print -6");
                    sb.AppendLine(LimitLines(route6, 80));
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: route print capture failed");
                }
                // IP config (basic)
                try
                {
                    sb.AppendLine("-- ipconfig /all (truncated) --");
                    var ipcfg = RunProcessCapture("cmd.exe", "/c ipconfig /all");
                    sb.AppendLine(LimitLines(ipcfg, 250));
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: ipconfig capture failed");
                }
                // Logical drives
                try
                {
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    {
                        sb.AppendLine($"LogicalDrive {drive.Name} Format={drive.DriveFormat} Total={drive.TotalSize/1024/1024/1024}GB Free={drive.AvailableFreeSpace/1024/1024/1024}GB");
                    }
                }
                catch (Exception ex)
                {
                    GLogger.Debug(ex, "CollectSystemInfo: drive enumeration failed");
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                AppendCmd(sb, "uname -a", "Linux Kernel");
                AppendCmd(sb, "cat /etc/os-release", "OS Release");
                AppendCmd(sb, "lscpu", "CPU Info", 200);
                AppendCmd(sb, "lsblk -o NAME,MODEL,SERIAL,SIZE,MOUNTPOINT -dn", "Block Devices");
                AppendCmd(sb, "lspci | grep -i vga", "GPU");
                AppendCmd(sb, "free -m", "Memory");
                AppendCmd(sb, "df -h", "Filesystem");
                AppendCmd(sb, "ip addr", "IP Addresses", 300);
                AppendCmd(sb, "ip route", "Routing Table", 200);
            }
            else if (OperatingSystem.IsMacOS())
            {
                AppendCmd(sb, "system_profiler SPSoftwareDataType | head -n 30", "Software");
                AppendCmd(sb, "system_profiler SPHardwareDataType | head -n 30", "Hardware");
                AppendCmd(sb, "system_profiler SPDisplaysDataType", "Displays");
                AppendCmd(sb, "system_profiler SPNVMeDataType SPATADataType", "Storage");
                AppendCmd(sb, "sysctl -a | grep machdep.cpu.brand_string", "CPU Brand");
                AppendCmd(sb, "df -h", "Filesystem");
                AppendCmd(sb, "ifconfig", "Interfaces", 300);
                AppendCmd(sb, "netstat -rn", "Routing Table", 200);
            }
            // Cross-platform managed network interface enumeration
            try
            {
                sb.AppendLine("==== NETWORK INTERFACES (.NET) ====");
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().OrderBy(n => n.Name))
                {
                    var props = ni.GetIPProperties();
                    var unicast = string.Join(',', props.UnicastAddresses.Select(a => a.Address));
                    var gateways = string.Join(',', props.GatewayAddresses.Select(g => g.Address));
                    sb.AppendLine($"IF: {ni.Name} Type={ni.NetworkInterfaceType} Status={ni.OperationalStatus} MAC={ni.GetPhysicalAddress()} SpeedMbps={(ni.Speed/1_000_000)}");
                    if (!string.IsNullOrEmpty(unicast)) sb.AppendLine("   IPs=" + unicast);
                    if (!string.IsNullOrEmpty(gateways)) sb.AppendLine("   GW=" + gateways);
                    var dns = string.Join(',', props.DnsAddresses.Select(d => d.ToString()));
                    if (!string.IsNullOrEmpty(dns)) sb.AppendLine("   DNS=" + dns);
                }
            }
            catch (Exception ex)
            {
                GLogger.Debug(ex, "CollectSystemInfo: network interfaces enumeration failed");
            }
            sb.AppendLine();

            sb.AppendLine("==== MEMORY (PROCESS) ====");
            try
            {
                var proc = Process.GetCurrentProcess();
                sb.AppendLine($"WorkingSet: {proc.WorkingSet64 / 1024 / 1024} MB");
                sb.AppendLine($"PrivateMemory: {proc.PrivateMemorySize64 / 1024 / 1024} MB");
                sb.AppendLine($"PagedMemory: {proc.PagedMemorySize64 / 1024 / 1024} MB");
                sb.AppendLine($"GC Total Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
                for (int gen = 0; gen <= GC.MaxGeneration; gen++) sb.AppendLine($"Gen {gen} Collections: {GC.CollectionCount(gen)}");
            }
            catch (Exception ex)
            {
                GLogger.Debug(ex, "CollectSystemInfo: process memory info retrieval failed");
            }
            sb.AppendLine();

            sb.AppendLine("==== ENVIRONMENT VARIABLES (SAFE SUBSET) ====");
            try
            {
                var whitelistKeys = new[] { "PATH", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "USERNAME", "USER", "SHELL" };
                foreach (var k in whitelistKeys)
                {
                    var v = Environment.GetEnvironmentVariable(k);
                    if (!string.IsNullOrWhiteSpace(v)) sb.AppendLine(k + "=" + v);
                }
                // Include DOTNET_ variables
                foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
                {
                    var key = kv.Key?.ToString();
                    if (key != null && key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine(key + "=" + kv.Value);
                }
            }
            catch (Exception ex)
            {
                GLogger.Debug(ex, "CollectSystemInfo: environment variable capture failed");
            }
        }
        catch (Exception ex)
        {
            GLogger.Warn(ex, "CollectSystemInfo root failure part");
            throw; // bubble – critical for report integrity
        }
        return sb.ToString();
    }

    private static void AppendCmd(StringBuilder sb, string cmd, string title, int maxLines = 500)
    {
        try
        {
            sb.AppendLine($"-- {title} ({cmd}) --");
            var output = RunCmdCapture("/usr/bin/env", "bash -c '" + cmd.Replace("'", "'\\''") + "'");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n');
                for (int i = 0; i < lines.Length && i < maxLines; i++) sb.AppendLine(lines[i]);
                if (lines.Length > maxLines) sb.AppendLine($"... (truncated {lines.Length - maxLines} lines) ...");
            }
        }
        catch (Exception ex)
        {
            GLogger.Warn(ex, "AppendCmd failed: {cmd}", cmd);
        }
        sb.AppendLine();
    }

    private static string RunProcessCapture(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            try { p.WaitForExit(4000); }
            catch (Exception waitEx) { GLogger.Warn(waitEx, "Process wait timeout or error {file} {args}", file, args); }
            if (string.IsNullOrWhiteSpace(output)) output = p.StandardError.ReadToEnd();
            return output;
        }
        catch (Exception ex)
        {
            GLogger.Warn(ex, "RunProcessCapture failed: {file} {args}", file, args);
            throw;
        }
    }

    private static string LimitLines(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var lines = text.Split('\n');
        if (lines.Length <= max) return text.TrimEnd();
        var sb2 = new StringBuilder();
        for (int i = 0; i < max; i++) sb2.AppendLine(lines[i]);
        sb2.AppendLine($"... (truncated {lines.Length - max} lines) ...");
        return sb2.ToString();
    }

    // MiniDump support (Windows only) – best effort
    [Flags]
    private enum MINIDUMP_TYPE : uint
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithDataSegs = 0x00000001,
        MiniDumpWithFullMemory = 0x00000002,
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpWithThreadInfo = 0x00001000,
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(IntPtr hProcess, int processId, IntPtr hFile, MINIDUMP_TYPE dumpType, IntPtr expParam, IntPtr userStreamParam, IntPtr callbackParam);

    private static string? TryCreateMiniDump(string targetPath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var proc = Process.GetCurrentProcess();
            var ok = MiniDumpWriteDump(proc.Handle, proc.Id, fs.SafeFileHandle.DangerousGetHandle(),
                MINIDUMP_TYPE.MiniDumpWithHandleData | MINIDUMP_TYPE.MiniDumpWithThreadInfo, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return ok ? targetPath : null;
        }
        catch (DllNotFoundException dllEx) { GLogger.Warn(dllEx, "dbghelp.dll not found"); return null; }
        catch (IOException ioEx) { GLogger.Warn(ioEx, "MiniDump IO error"); return null; }
        catch (Exception ex) { GLogger.Warn(ex, "MiniDump creation unexpected error"); return null; }
    }
#endif

    private void ConfigureLogging()
    {
        try
        {
            if (LogManager.Configuration != null) return;
            var config = new LoggingConfiguration();
            var baseDir = AppContext.BaseDirectory;
            var logDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(logDir);
            var fileLayout = Layout.FromString("${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}");
            var consoleLayout = Layout.FromString("${time} [${level:uppercase=true}] ${message}");
            FileTarget mkFile(string name, string file) => new(name) { FileName = Layout.FromString(Path.Combine(logDir, file)), Layout = fileLayout };
            var traceFile = mkFile("log_trace", "${shortdate}_trace.log");
            var debugFile = mkFile("log_debug", "${shortdate}_debug.log");
            var infoFile = mkFile("log_info", "${shortdate}_info.log");
            var warnFile = mkFile("log_warn", "${shortdate}_warn.log");
            var errorFile = mkFile("log_error", "${shortdate}_error.log");
            var fatalFile = mkFile("log_fatal", "${shortdate}_fatal.log");
            var consoleTarget = new ConsoleTarget("console") { Layout = consoleLayout };
            var debugTarget = new DebuggerTarget("debug") { Layout = consoleLayout };
            AsyncTargetWrapper wrap(Target t) => new(t) { QueueLimit = 10000, OverflowAction = AsyncTargetWrapperOverflowAction.Discard, BatchSize = 200, TimeToSleepBetweenBatches = 0 };
            var traceAsync = wrap(traceFile); var debugAsync = wrap(debugFile); var infoAsync = wrap(infoFile); var warnAsync = wrap(warnFile); var errorAsync = wrap(errorFile); var consoleAsync = wrap(consoleTarget); var debugWinAsync = wrap(debugTarget);
            config.AddTarget(traceAsync); config.AddTarget(debugAsync); config.AddTarget(infoAsync); config.AddTarget(warnAsync); config.AddTarget(errorAsync); config.AddTarget(fatalFile); config.AddTarget(consoleAsync); config.AddTarget(debugWinAsync);
            config.AddRule(LogLevel.Trace, LogLevel.Trace, traceAsync); config.AddRule(LogLevel.Debug, LogLevel.Debug, debugAsync); config.AddRule(LogLevel.Info, LogLevel.Info, infoAsync); config.AddRule(LogLevel.Warn, LogLevel.Warn, warnAsync); config.AddRule(LogLevel.Error, LogLevel.Error, errorAsync); config.AddRule(LogLevel.Fatal, LogLevel.Fatal, fatalFile); config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleAsync); config.AddRule(LogLevel.Debug, LogLevel.Fatal, debugWinAsync);
            LogManager.Configuration = config;
            LogManager.GetCurrentClassLogger().Info("NLog initialized. Logs at {dir}", logDir);
        }
        catch (IOException ioEx) { GLogger.Error(ioEx, "ConfigureLogging IO failure"); throw; }
        catch (UnauthorizedAccessException uaEx) { GLogger.Error(uaEx, "ConfigureLogging unauthorized"); throw; }
        catch (Exception ex) { GLogger.Error(ex, "ConfigureLogging unexpected failure"); throw; }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Accessing Avalonia BindingPlugins.DataValidators only to remove DataAnnotationsValidationPlugin; safe for trimming.")]
    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in dataValidationPluginsToRemove) BindingPlugins.DataValidators.Remove(plugin);
    }

    // Add definition for ShowCrashWindow when not DEBUG to satisfy calls with zipPath parameter
#if !DEBUG
    private void ShowCrashWindow(string dump, string origin, string? zipPath)
    {
        // Attempt to open file explorer on the zip file immediately
        try
        {
            if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zipPath}\"") { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("xdg-open", Path.GetDirectoryName(zipPath)!) { UseShellExecute = true });
                }
            }
        }
        catch (Exception openEx) { GLogger.Warn(openEx, "Open explorer for crash zip failed"); }

        var lifetime = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "程序发生未处理错误，已生成日志压缩包。", FontWeight = Avalonia.Media.FontWeight.Bold });
        if (!string.IsNullOrEmpty(zipPath)) panel.Children.Add(new TextBlock { Text = $"报告文件: {zipPath}", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "应用将退出。请将该压缩包反馈给开发者。", FontSize = 12 });
        var detailsBox = new TextBox { Text = dump, IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 240, FontFamily = new Avalonia.Media.FontFamily("Consolas,Monospace") };
        panel.Children.Add(new ScrollViewer { Content = detailsBox, Height = 260 });
        var btnExit = new Button { Content = "退出", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Width = 120 };
        btnExit.Click += (_, __) => { try { LogManager.Flush(TimeSpan.FromSeconds(2)); LogManager.Shutdown(); } catch (Exception flushEx) { GLogger.Warn(flushEx, "Flush on exit button failed"); } ExitAction(1); };
        panel.Children.Add(btnExit);
        var win = new UrsaWindow { Title = "发生错误", Width = 780, Height = 520, CanResize = true, Content = panel };
        win.Closed += (_, __) => { try { LogManager.Flush(TimeSpan.FromSeconds(2)); LogManager.Shutdown(); } catch (Exception flushEx) { GLogger.Warn(flushEx, "Flush on crash window close failed"); } ExitAction(1); };
        try { lifetime?.MainWindow?.Hide(); } catch (Exception hideEx) { GLogger.Warn(hideEx, "Hide main window failed"); }
        if (lifetime?.MainWindow is not null)
        {
            try { win.ShowDialog(lifetime.MainWindow); }
            catch (Exception dialogEx)
            {
                GLogger.Warn(dialogEx, "ShowCrashWindow dialog presentation failed; using non-modal window");
                win.Show();
            }
        }
        else win.Show();
    }
#endif

    private static string RunCmdCapture(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            try { p.WaitForExit(3000); }
            catch (Exception waitEx) { GLogger.Warn(waitEx, "RunCmdCapture wait failed {file} {args}", fileName, arguments); }
            return output;
        }
        catch (FileNotFoundException fnf) { GLogger.Warn(fnf, "Command not found {file}", fileName); throw; }
        catch (Exception ex) { GLogger.Warn(ex, "RunCmdCapture failed {file} {args}", fileName, arguments); throw; }
    }

    private void RegisterStartupUpdateCheck(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_startupUpdateHookRegistered) return;
        _startupUpdateHookRegistered = true;

        if (desktop.MainWindow is null)
        {
            _ = RunStartupUpdateCheckAsync(null);
            return;
        }

        desktop.MainWindow.Opened += OnStartupWindowOpened;
    }

    private void OnStartupWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not Window host) return;
        host.Opened -= OnStartupWindowOpened;
        _ = RunStartupUpdateCheckAsync(host);
    }

    private async Task RunStartupUpdateCheckAsync(Window? host)
    {
        if (_startupUpdateTriggered) return;
        _startupUpdateTriggered = true;

        await Task.Yield();

        UrsaWindowNotificationManager? manager = null;
        if (host is not null)
        {
            manager = UrsaWindowNotificationManager.TryGetNotificationManager(host, out var existing) && existing is not null
                ? existing
                : new UrsaWindowNotificationManager(host) { Position = NotificationPosition.TopRight };
        }

        try
        {
            var settingsVm = Services.GetRequiredService<SettingsViewModel>();
            await settingsVm.LoadAsync();
            await settingsVm.CheckAndUpdateOnStartupAsync();
            var message = string.IsNullOrWhiteSpace(settingsVm.Status) ? "更新检查完成" : settingsVm.Status;
            if (manager is UrsaWindowNotificationManager mgr)
            {
                await Dispatcher.UIThread.InvokeAsync(() => mgr.Show(new UrsaNotification("更新", message), showIcon: true, showClose: true, type: NotificationType.Information, classes: ["Light"]));
            }
            else
            {
                GLogger.Info("Startup update check: {message}", message);
            }
        }
        catch (HttpRequestException ex)
        {
            await ShowUpdateErrorAsync(manager, ex.Message).ConfigureAwait(false);
            GLogger.Warn(ex, "Startup update check failed due to network error");
        }
        catch (JsonException ex)
        {
            await ShowUpdateErrorAsync(manager, ex.Message).ConfigureAwait(false);
            GLogger.Warn(ex, "Startup update check failed while parsing configuration");
        }
        catch (IOException ex)
        {
            await ShowUpdateErrorAsync(manager, ex.Message).ConfigureAwait(false);
            GLogger.Warn(ex, "Startup update check failed accessing configuration files");
        }
        catch (UnauthorizedAccessException ex)
        {
            await ShowUpdateErrorAsync(manager, ex.Message).ConfigureAwait(false);
            GLogger.Warn(ex, "Startup update check blocked by insufficient permissions");
        }
        catch (Exception ex)
        {
            await ShowUpdateErrorAsync(manager, ex.Message).ConfigureAwait(false);
            GLogger.Error(ex, "Unexpected failure during startup update check");
        }
    }

    private async Task HandleProtocolActivationAsync(string uriArg)
    {
        try
        {
            await Task.Yield();
            var uri = new Uri(uriArg);
            // Load settings to obtain optional deeplink callback URL
            var settingsVmOnStart = Services.GetService<SettingsViewModel>();
            string? callbackUrl = null;
            if (settingsVmOnStart is not null)
            {
                try { await settingsVmOnStart.LoadAsync(); callbackUrl = settingsVmOnStart.DeeplinkCallbackUrl; } catch { }
            }
            // Open/activate main window and pass uri query to it
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existing = MainWindow.Instance;
                MainWindow main;
                if (existing is null)
                {
                    main = new MainWindow { DataContext = Services.GetRequiredService<MainWindowViewModel>() };
                    main.Show();
                }
                else
                {
                    main = existing;
                    if (!main.IsVisible) main.Show();
                }
                // Pass the raw URI to viewmodel if supported
                if (main.DataContext is MainWindowViewModel vm)
                {
                    vm.InitialUri = uri.ToString();
                }
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                if (lifetime != null) lifetime.MainWindow = main;
                Services.GetRequiredService<ITopLevelProvider>().TopLevel = main;
            });

            // If deeplink contains a token and a callback URL is configured, attempt to POST ack in background
            try
            {
                var token = GetQueryValue(uri, "token");
                if (!string.IsNullOrEmpty(token) && !string.IsNullOrWhiteSpace(callbackUrl))
                {
                    try
                    {
                        // Fire-and-forget the ack send without creating an extra Task.Run wrapper.
                        // ContinueWith is used to capture/log faults and non-delivery without
                        // blocking startup (avoids awaiting the long-running retry loop).
                        bool allowFallback = false;
                        try { allowFallback = settingsVmOnStart?.AllowLocalHttpFallback ?? false; } catch { }
                        string? authHeader = null;
                        try { authHeader = settingsVmOnStart?.DeeplinkCallbackAuth; } catch { }

                        // Prefer ackSecret when present in query string
                        var ackSecret = GetQueryValue(uri, "ackSecret");
                        var isAckSecret = !string.IsNullOrEmpty(ackSecret);
                        var keyToSend = isAckSecret ? ackSecret! : token;
                        var ackTask = DeeplinkAckHelper.SendAckWithRetryAsync(callbackUrl!, keyToSend!, allowFallback, authHeader, isAckSecret);
                        _ = ackTask.ContinueWith(t =>
                        {
                            try
                            {
                                if (t.IsFaulted)
                                {
                                    GLogger.Warn(t.Exception, "Background deeplink ack task failed for {url}", callbackUrl);
                                }
                                else if (t.IsCompletedSuccessfully && t.Result == false)
                                {
                                    GLogger.Warn("Deeplink ack not delivered immediately; queued for later: url={url}", callbackUrl);
                                }
                            }
                            catch (Exception ex)
                            {
                                GLogger.Warn(ex, "Exception in deeplink ack continuation for {url}", callbackUrl);
                            }
                        }, TaskScheduler.Default);
                    }
                    catch (Exception ex)
                    {
                        GLogger.Warn(ex, "Deeplink ack scheduling failed for {uri}", uriArg);
                    }
                }
            }
            catch (Exception ex)
            {
                GLogger.Warn(ex, "Deeplink ack scheduling failed for {uri}", uriArg);
            }
        }
        catch (Exception ex)
        {
            GLogger.Warn(ex, "Protocol activation failed: {uri}", uriArg);
        }
    }

    // Public wrapper used by IPC to route activations into the running instance
    private async Task ProcessProtocolActivationAsync(string uriArg)
    {
        // Reuse existing handler which already sets up UI and posts ack
        await HandleProtocolActivationAsync(uriArg).ConfigureAwait(false);
    }

    private const string IpcPipeName = "labelplus_ipc_pipe_v1";

    private async Task StartIpcServerAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows: Named pipe server
                while (true)
                {
                    try
                    {
                        using var server = new System.IO.Pipes.NamedPipeServerStream(IpcPipeName, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Message, System.IO.Pipes.PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync().ConfigureAwait(false);
                        using var sr = new StreamReader(server, Encoding.UTF8);
                        var line = await sr.ReadLineAsync().ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var trimmed = line.Trim();
                            GLogger.Info("IPC: received forwarded URI: {uri}", trimmed);
                            // Try to send ack immediately (fast path) before routing to UI
                            _ = TryImmediateAckForIpcAsync(trimmed);
                            _ = Dispatcher.UIThread.InvokeAsync(() => _ = ProcessProtocolActivationAsync(trimmed));
                        }
                    }
                    catch (Exception ex)
                    {
                        GLogger.Debug(ex, "IPC server error (named pipe)");
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                // Non-windows: use a loopback TCP listener on a localhost-only port as a simple IPC fallback
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                listener.Server.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                listener.Start();
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                GLogger.Info("IPC TCP fallback listening on port {port}", port);
                while (true)
                {
                    try
                    {
                        var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var ns = client.GetStream();
                                using var sr = new StreamReader(ns, Encoding.UTF8);
                                var content = await sr.ReadToEndAsync().ConfigureAwait(false);
                                if (!string.IsNullOrWhiteSpace(content))
                                {
                                    // Try fast-path ack send when receiving IPC content
                                    _ = TryImmediateAckForIpcAsync(content);
                                    await Dispatcher.UIThread.InvokeAsync(() => _ = ProcessProtocolActivationAsync(content)).ConfigureAwait(false);
                                }
                                client.Close();
                            }
                            catch (Exception ex)
                            {
                                GLogger.Debug(ex, "IPC TCP handler error");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        GLogger.Debug(ex, "IPC server error (tcp)");
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GLogger.Warn(ex, "Failed to start IPC server for protocol activation forwarding");
        }
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        if (uri is null) return null;
        var q = uri.Query;
        if (string.IsNullOrEmpty(q)) return null;
        var trimmed = q.TrimStart('?');
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in pairs)
        {
            var idx = p.IndexOf('=');
            if (idx < 0) continue;
            var k = Uri.UnescapeDataString(p[..idx]);
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = Uri.UnescapeDataString(p[(idx + 1)..]);
            return v;
        }
        return null;
    }

    // Fast-path: when IPC receives a forwarded deeplink while app is running, attempt to send ack immediately
    private async Task TryImmediateAckForIpcAsync(string uriArg)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uriArg)) return;
            Uri uri;
            try { uri = new Uri(uriArg); } catch { return; }

            var token = GetQueryValue(uri, "token");
            var ackSecret = GetQueryValue(uri, "ackSecret");
            if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(ackSecret)) return;

            // Load settings to obtain optional deeplink callback URL and auth
            var settingsVm = Services.GetService<SettingsViewModel>();
            string? callbackUrl = null;
            bool allowFallback = false;
            string? authHeader = null;
            try
            {
                if (settingsVm is not null)
                {
                    await settingsVm.LoadAsync();
                    callbackUrl = settingsVm.DeeplinkCallbackUrl;
                    allowFallback = settingsVm.AllowLocalHttpFallback;
                    authHeader = settingsVm.DeeplinkCallbackAuth;
                }
            }
            catch (Exception ex)
            {
                GLogger.Debug(ex, "TryImmediateAckForIpcAsync: failed to load settings");
            }

            if (string.IsNullOrWhiteSpace(callbackUrl)) return;

            var isAckSecret = !string.IsNullOrWhiteSpace(ackSecret);
            var key = isAckSecret ? ackSecret! : token!;

            // Fire-and-forget but attach continuation for logging
            var t = DeeplinkAckHelper.SendAckWithRetryAsync(callbackUrl, key, allowFallback, authHeader, isAckSecret);
            _ = t.ContinueWith(tt =>
            {
                try
                {
                    if (tt.IsFaulted) GLogger.Warn(tt.Exception, "Immediate ack task failed for {url}", callbackUrl);
                    else if (tt.IsCompletedSuccessfully && tt.Result == false) GLogger.Info("Immediate ack not delivered; queued for later: {url}", callbackUrl);
                }
                catch (Exception ex) { GLogger.Warn(ex, "Immediate ack continuation error"); }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            GLogger.Debug(ex, "TryImmediateAckForIpcAsync unexpected");
        }
    }

    private static async Task TrySendDeeplinkAckAsync(string callbackUrl, string token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            // Manually construct small JSON payload to avoid System.Text.Json trimming/AOT issues
            static string JsonEscapeForSimple(string? s)
            {
                if (s is null) return string.Empty;
                return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\b", "\\b").Replace("\f", "\\f").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
            }
            var payload = "{\"token\":\"" + JsonEscapeForSimple(token) + "\"}";
            int attempts = 0;
            var backoff = TimeSpan.FromMilliseconds(500);
            while (attempts < 4)
            {
                attempts++;
                try
                {
                    using var c = new StringContent(payload, Encoding.UTF8, "application/json");
                    var resp = await client.PostAsync(callbackUrl, c).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        GLogger.Info("Deeplink ack successful to {url}", callbackUrl);
                        return;
                    }
                    else if ((int)resp.StatusCode >= 400 && (int)resp.StatusCode < 500)
                    {
                        GLogger.Warn("Deeplink ack returned client error {code} to {url}", resp.StatusCode, callbackUrl);
                        return; // do not retry client errors
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    GLogger.Warn(ex, "Deeplink ack attempt {n} failed, will retry", attempts);
                }

                await Task.Delay(backoff).ConfigureAwait(false);
                backoff = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * 2);
            }

            // If we get here, all attempts failed — queue for later retry
            try
            {
                var queueFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "labelplus", "pending_ack.jsonl");
                var dir = Path.GetDirectoryName(queueFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var line = "{\"token\":\"" + JsonEscapeForSimple(token) + "\",\"ts\":\"" + DateTimeOffset.UtcNow.ToString("o") + "\",\"url\":\"" + JsonEscapeForSimple(callbackUrl) + "\"}";
                await File.AppendAllTextAsync(queueFile, line + Environment.NewLine).ConfigureAwait(false);
                GLogger.Info("Deeplink ack queued for later delivery");
            }
            catch (Exception ex)
            {
                GLogger.Warn(ex, "Failed to queue deeplink ack to local file");
            }
        }
        catch (Exception ex)
        {
            GLogger.Warn(ex, "Unexpected failure in TrySendDeeplinkAckAsync");
        }
    }

    private static async Task ShowUpdateErrorAsync(UrsaWindowNotificationManager? manager, string message)
    {
        if (manager is not UrsaWindowNotificationManager mgr) return;
        await Dispatcher.UIThread.InvokeAsync(() => mgr.Show(new UrsaNotification("更新检查失败", message), showIcon: true, showClose: true, type: NotificationType.Warning, classes: ["Light"]));
    }
}
