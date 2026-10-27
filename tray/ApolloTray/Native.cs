using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

// Adapted from Handoff (github.com/RogerSquare/handoff).
namespace ApolloTray;

static class Native
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);

    [StructLayout(LayoutKind.Sequential)]
    struct Margins { public int L, R, T, B; }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public static bool AppsUseDarkTheme()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    public static bool TaskbarUsesDarkTheme()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("SystemUsesLightTheme") is not int v || v == 0;
    }

    /// <summary>
    /// Flyout surface tint laid over the acrylic. Raw DWM acrylic lets bright wallpaper wash the
    /// surface out to mid-grey; Windows' own flyouts sit on a near-opaque tint like this one.
    /// </summary>
    public static SolidColorBrush SurfaceTint() => AppsUseDarkTheme()
        ? new SolidColorBrush(Color.FromArgb(0xF2, 0x2C, 0x2C, 0x2C))
        : new SolidColorBrush(Color.FromArgb(0xF2, 0xF3, 0xF3, 0xF3));

    /// <summary>Recessed footer strip: darker than the surface in dark mode, a touch darker in light.</summary>
    public static SolidColorBrush RecessTint() => AppsUseDarkTheme()
        ? new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00))
        : new SolidColorBrush(Color.FromArgb(0x0D, 0x00, 0x00, 0x00));

    /// <summary>Acrylic (transient) backdrop + rounded corners, like Windows 11 flyouts.</summary>
    public static void ApplyAcrylic(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        var src = HwndSource.FromHwnd(hwnd);
        if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;

        int dark = AppsUseDarkTheme() ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = 2;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref m);
        int backdrop = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }

    /// <summary>Dark/light title bar to match the app theme (Fluent supplies the Mica backdrop).</summary>
    public static void ApplyMica(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = AppsUseDarkTheme() ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- per-monitor placement (pixels), for showing the flyout on the streamed display ------

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>
    /// Work area of the display with this GDI name (e.g. \\.\DISPLAY10), else the primary display's.
    /// Queried live: WPF's SystemParameters.WorkArea is cached and primary-only.
    /// </summary>
    public static Rect WorkArea(string? device)
    {
        Rect? match = null, primary = null;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (mon, _, _, _) =>
        {
            var mi = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(mon, ref mi)) return true;
            if ((mi.Flags & MONITORINFOF_PRIMARY) != 0) primary = mi.Work;
            if (device != null && string.Equals(mi.Device, device, StringComparison.OrdinalIgnoreCase)) match = mi.Work;
            return true;
        }, IntPtr.Zero);
        return match ?? primary ?? new Rect { Right = 1920, Bottom = 1080 };
    }

    /// <summary>
    /// Put the window in the bottom-right corner of a work area, in pixels. Moving onto a monitor with
    /// another DPI rescales the window (per-monitor v2), so move there first, then read the real size.
    /// </summary>
    public static void PlaceBottomRight(Window w, Rect work, double marginDip)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        const uint NOSIZE = 0x1, NOZORDER = 0x4, NOACTIVATE = 0x10;
        SetWindowPos(hwnd, IntPtr.Zero, work.Left, work.Top, 0, 0, NOSIZE | NOZORDER | NOACTIVATE);
        w.UpdateLayout();
        var marginPx = (int)Math.Round(marginDip * GetDpiForWindow(hwnd) / 96.0);
        GetWindowRect(hwnd, out var r);
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        SetWindowPos(hwnd, IntPtr.Zero, work.Right - width - marginPx, work.Bottom - height - marginPx, 0, 0, NOSIZE | NOZORDER | NOACTIVATE);
    }
}
