using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace LabelPlus_Next.Services;

public static class UrlSchemeRegistrar
{
    // Tries to register labelplus:// scheme for the current user. Best-effort; failures are non-fatal.
    public static async Task TryRegisterLabelPlusSchemeAsync(string exePath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || OperatingSystem.IsWindows())
            {
                // Suppress platform analyzer for the guarded Windows-only call.
#pragma warning disable CA1416 // Validate platform compatibility
                await Task.Run(() => RegisterWindowsUserProtocol(exePath)).ConfigureAwait(false);
#pragma warning restore CA1416 // Validate platform compatibility
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await Task.Run(() => RegisterXdgDesktopHandler(exePath)).ConfigureAwait(false);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS requires Info.plist modifications inside app bundle; cannot register from arbitrary exe.
                // No-op here; document in README for packaging step.
            }
        }
        catch
        {
            // swallow; best-effort
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindowsUserProtocol(string exePath)
    {
        // Create HKCU\Software\Classes\labelplus with command
        try
        {
#pragma warning disable CA1416 // Validate platform compatibility - guarded by runtime check in caller
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Classes\\labelplus");
            if (key is null) return;
            key.SetValue(null, "URL:LabelPlus Protocol");
            key.SetValue("URL Protocol", "");
            using var shell = key.CreateSubKey("shell");
            using var open = shell?.CreateSubKey("open");
            using var cmd = open?.CreateSubKey("command");
            if (cmd is not null)
            {
                cmd.SetValue(null, $"\"{exePath}\" \"%1\"");
            }
#pragma warning restore CA1416 // Validate platform compatibility
        }
        catch { }
    }

    private static void RegisterXdgDesktopHandler(string exePath)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            var applications = Path.Combine(home, ".local", "share", "applications");
            Directory.CreateDirectory(applications);
            var desktopFile = Path.Combine(applications, "labelplus.desktop");
            var content = $"[Desktop Entry]\nType=Application\nName=LabelPlus\nExec=\"{exePath}\" %u\nNoDisplay=true\nMimeType=x-scheme-handler/labelplus;\n";
            File.WriteAllText(desktopFile, content);
            // register handler
            try { Process.Start(new ProcessStartInfo("xdg-mime", $"default labelplus.desktop x-scheme-handler/labelplus") { RedirectStandardOutput = true, UseShellExecute = false }); }
            catch { }
        }
        catch { }
    }
}
