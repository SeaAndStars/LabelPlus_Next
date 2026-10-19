using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Styling;

namespace LabelPlus_Next.Shared;

/// <summary>
/// Small Windows helpers shared by the app and the updater.
/// </summary>
internal static class WindowChrome
{
    private const int DwmwaBorderColor = 34;

    // COLORREF (0x00BBGGRR) values that match the two app backgrounds. A theme
    // coloured border is used instead of "none" so that even if DWM ignores the
    // border-colour attribute the remaining 1px edge blends into the app.
    private const uint DarkBorderColor = 0x001A1717;  // #17171A
    private const uint LightBorderColor = 0x00FAF8F7; // #F7F8FA

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    /// <summary>
    /// Removes the 1px system-drawn border that Windows paints around custom-chrome
    /// windows in dark mode (it appears as a white edge).
    ///
    /// Two mechanisms are used because DWMWA_BORDER_COLOR is only honoured on
    /// Windows 11 and DWM can repaint the frame when the window is activated:
    ///   1. extend the client area so the non-client frame is not painted;
    ///   2. clear the DWM border colour whenever the window is activated.
    /// </summary>
    public static void RemoveSystemBorder(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // Let the client area cover the old non-client frame. The app draws its
            // own title bar, so no native caption is needed.
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaTitleBarHeightHint = 0;
        }
        catch
        {
            // Some window backends do not allow changing this after creation.
        }

        void Apply()
        {
            try
            {
                var handle = window.TryGetPlatformHandle();
                if (handle is null || handle.Handle == IntPtr.Zero) return;

                var dark = window.ActualThemeVariant == ThemeVariant.Dark;
                var color = dark ? DarkBorderColor : LightBorderColor;
                _ = DwmSetWindowAttribute(handle.Handle, DwmwaBorderColor, ref color, sizeof(uint));
            }
            catch
            {
                // Best effort: unsupported on older Windows builds.
            }
        }

        if (window.IsVisible) Apply();
        window.Opened += (_, _) => Apply();
        // DWM may recreate the frame on activation / theme changes.
        window.Activated += (_, _) => Apply();
        window.ActualThemeVariantChanged += (_, _) => Apply();
    }
}
