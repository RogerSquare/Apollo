using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

// Adapted from Handoff (github.com/RogerSquare/handoff).
namespace ApolloTray;

/// <summary>Notification-area icon drawn from the Segoe Fluent glyph of whatever is playing.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly WinForms.NotifyIcon _icon = new();
    Icon? _current;
    string _glyph = "";

    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIcon()
    {
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) LeftClick?.Invoke();
            else if (e.Button == WinForms.MouseButtons.Right) RightClick?.Invoke();
        };
        Update("", "ApolloTray");
        _icon.Visible = true;
    }

    public void Update(string glyph, string tooltip)
    {
        // NotifyIcon caps tooltips at 127 chars.
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (glyph == _glyph) return;
        _glyph = glyph;
        var old = _current;
        _current = Draw(glyph);
        _icon.Icon = _current;
        if (old != null) { DestroyIcon(old.Handle); old.Dispose(); }
    }

    static Icon Draw(string glyph)
    {
        // Draw at the tray's exact pixel size for the taskbar's DPI: a downscaled icon goes soft.
        var size = TrayIconSize();
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var color = Native.TaskbarUsesDarkTheme() ? Color.White : Color.Black;
            using var family = new FontFamily(FontFamilyOrFallback());
            // Filling the glyph outline gives clean alpha edges; DrawString's text AA on a transparent
            // bitmap leaves a grey, washed-out glyph. Fluent icon glyphs fill their em square.
            using var path = new GraphicsPath();
            path.AddString(glyph, family, (int)FontStyle.Regular, size, new PointF(0, 0), StringFormat.GenericTypographic);
            var b = path.GetBounds();
            using var m = new Matrix();
            m.Translate((size - b.Width) / 2f - b.X, (size - b.Height) / 2f - b.Y);
            path.Transform(m);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static int TrayIconSize()
    {
        try
        {
            var tray = FindWindow("Shell_TrayWnd", null);
            var dpi = tray != IntPtr.Zero ? GetDpiForWindow(tray) : 96;
            var px = GetSystemMetricsForDpi(49 /*SM_CXSMICON*/, dpi);
            if (px > 0) return px;
        }
        catch { }
        return WinForms.SystemInformation.SmallIconSize.Width;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);

    static string FontFamilyOrFallback()
    {
        using var fonts = new InstalledFontCollection();
        foreach (var f in fonts.Families)
            if (f.Name == "Segoe Fluent Icons") return f.Name;
        return "Segoe MDL2 Assets";
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_current != null) { DestroyIcon(_current.Handle); _current.Dispose(); }
    }
}
