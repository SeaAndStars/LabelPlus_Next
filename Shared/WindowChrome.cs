using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace LabelPlus_Next.Shared;

/// <summary>
/// Small Windows helpers shared by the app and the updater.
/// </summary>
internal static class WindowChrome
{
    private const int DwmwaBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    /// <summary>
    /// Removes the 1px system-drawn border that Windows 11 paints around windows in
    /// dark mode (it appears as a white edge). The native resize border remains.
    /// Older Windows versions do not support DWMWA_BORDER_COLOR; the call is ignored.
    /// </summary>
    public static void RemoveSystemBorder(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        void Apply()
        {
            try
            {
                var handle = window.TryGetPlatformHandle();
                if (handle is null || handle.Handle == IntPtr.Zero) return;

                var color = DwmColorNone;
                _ = DwmSetWindowAttribute(handle.Handle, DwmwaBorderColor, ref color, sizeof(uint));
            }
            catch
            {
                // Best effort: unsupported on older Windows builds.
            }
        }

        if (window.IsVisible) Apply();
        window.Opened += (_, _) => Apply();
    }
}
