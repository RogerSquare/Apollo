using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ApolloTray;

/// <summary>
/// Touch mode for small/touch devices (Apollo stores it per device as touch_scale; we apply it, since
/// these are per-user settings and Apollo runs as SYSTEM). While a stream with touch mode runs:
/// the stream's display gets the chosen scaling, the taskbar drops the search box and Task View
/// so pinned apps fit, and the touch keyboard opens on tap even though the PC has a keyboard.
/// Everything is put back when the stream stops. The originals are kept in touch-restore.json
/// first, so a crash or reboot mid-stream is undone on the next start.
/// </summary>
public sealed class TouchMode
{
    static readonly string StatePath = Path.Combine(Log.Dir, "touch-restore.json");
    const string PerMonitorKey = @"Control Panel\Desktop\PerMonitorSettings";
    const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
    const string TabTipKey = @"Software\Microsoft\TabletTip\1.7";

    /// <summary>Windows' scaling steps; a display's "relative" scale indexes this from its recommended step.</summary>
    static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    /// <summary>What we changed and what it was before. Null values = the setting didn't exist.</summary>
    sealed class Saved
    {
        public string Display { get; set; } = "";     // GDI name of the stream's display
        public int Scale { get; set; }                // what we applied
        public int? OriginalRel { get; set; }         // display's relative scale before
        public string? MonitorKey { get; set; }       // its PerMonitorSettings key, for restoring after it's gone
        public int? MonitorKeyDpi { get; set; }
        public int? SearchMode { get; set; }
        public int? TaskView { get; set; }
        public int? TapInvoke { get; set; }
        public int? DesktopAutoInvoke { get; set; }
    }

    Saved? _applied;

    /// <summary>The scales offered in the menu and web UI (0 = off).</summary>
    public static readonly int[] Choices = { 0, 125, 150, 175, 200 };

    public TouchMode()
    {
        // Left over from a crash or reboot mid-stream: undo now, the stream it belonged to is gone.
        try
        {
            if (File.Exists(StatePath))
            {
                _applied = JsonSerializer.Deserialize<Saved>(File.ReadAllText(StatePath));
                Log.Info("Touch mode: restoring settings left from an earlier session");
                Revert();
            }
        }
        catch (Exception ex) { Log.Error("Touch mode: reading saved state", ex); }
    }

    /// <summary>The scale applied right now (0 = touch mode isn't on).</summary>
    public int AppliedScale => _applied?.Scale ?? 0;

    /// <summary>The last scale turned on, for the flyout tile's "on" (default 150%).</summary>
    public static int LastScale
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\ApolloTray");
            return k?.GetValue("TouchScale") is int v && v > 0 ? v : 150;
        }
        private set
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\ApolloTray");
            k.SetValue("TouchScale", value, RegistryValueKind.DWord);
        }
    }

    /// <summary>Bring the settings in line with the status: called after every poll.</summary>
    public void Sync(ApolloStatus s)
    {
        if (s.TouchScale > 0 && s.TouchScale != LastScale) LastScale = s.TouchScale;
        var app = s.App;
        // Only while a device is actually watching (not paused) a virtual display we can scale.
        var want = s.Stream != null && app is { VirtualDisplay: true, DisplayName: not null } ? app.TouchScale : 0;
        var display = want > 0 ? app!.DisplayName! : null;

        if (_applied != null && (want == 0 || _applied.Display != display)) Revert();
        if (want > 0 && _applied == null) Apply(display!, want);
        else if (want > 0 && _applied!.Scale != want) Rescale(want);
    }

    /// <summary>Undo everything (quitting the tray app).</summary>
    public void Shutdown()
    {
        if (_applied != null) Revert();
    }

    void Apply(string display, int scale)
    {
        try
        {
            var saved = new Saved
            {
                Display = display,
                Scale = scale,
                SearchMode = GetDword(SearchKey, "SearchboxTaskbarMode"),
                TaskView = GetDword(AdvancedKey, "ShowTaskViewButton"),
                TapInvoke = GetDword(TabTipKey, "TouchKeyboardTapInvoke"),
                DesktopAutoInvoke = GetDword(TabTipKey, "EnableDesktopModeAutoInvoke"),
            };
            var before = MonitorDpiValues();
            if (Dpi.Get(display) is { } dpi) saved.OriginalRel = dpi.Cur;

            // Save first: if we die after changing anything, the next start puts it back.
            _applied = saved;
            Persist();

            SetScale(display, scale);
            // The display's PerMonitorSettings key is the one the change just wrote to.
            var after = MonitorDpiValues();
            var key = after.Keys.FirstOrDefault(k => !before.TryGetValue(k, out var v) || v != after[k]);
            if (key != null)
            {
                saved.MonitorKey = key;
                saved.MonitorKeyDpi = before.TryGetValue(key, out var v) ? v : null;
            }

            SetDword(SearchKey, "SearchboxTaskbarMode", 1);     // search icon instead of the search box
            SetDword(AdvancedKey, "ShowTaskViewButton", 0);
            SetDword(TabTipKey, "TouchKeyboardTapInvoke", 2);   // "Show the touch keyboard: Always"
            SetDword(TabTipKey, "EnableDesktopModeAutoInvoke", 1);
            Persist();
            BroadcastTaskbarChange();
            Log.Info($"Touch mode on: {display} at {scale}% (was relative {saved.OriginalRel?.ToString() ?? "?"}, key {saved.MonitorKey ?? "?"})");
        }
        catch (Exception ex) { Log.Error("Touch mode: apply", ex); }
    }

    void Rescale(int scale)
    {
        try
        {
            SetScale(_applied!.Display, scale);
            _applied.Scale = scale;
            Persist();
            Log.Info($"Touch mode: {_applied.Display} now {scale}%");
        }
        catch (Exception ex) { Log.Error("Touch mode: rescale", ex); }
    }

    void Revert()
    {
        var s = _applied!;
        try
        {
            // The display is still there (touch mode turned off mid-stream): set it back live.
            // Otherwise the stream ended and took it with it: put Windows' remembered value back.
            if (s.OriginalRel is int rel && Dpi.Get(s.Display) is not null) Dpi.SetRelative(s.Display, rel);
            else if (s.MonitorKey != null) RestoreMonitorDpi(s.MonitorKey, s.MonitorKeyDpi);

            RestoreDword(SearchKey, "SearchboxTaskbarMode", s.SearchMode);
            RestoreDword(AdvancedKey, "ShowTaskViewButton", s.TaskView);
            RestoreDword(TabTipKey, "TouchKeyboardTapInvoke", s.TapInvoke);
            RestoreDword(TabTipKey, "EnableDesktopModeAutoInvoke", s.DesktopAutoInvoke);
            BroadcastTaskbarChange();
            Log.Info($"Touch mode off: restored {s.Display}");
        }
        catch (Exception ex) { Log.Error("Touch mode: revert", ex); }
        _applied = null;
        try { File.Delete(StatePath); } catch { }
    }

    void Persist()
    {
        Directory.CreateDirectory(Log.Dir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(_applied));
    }

    static void SetScale(string display, int scale)
    {
        if (Dpi.Get(display) is not { } dpi) { Log.Warn($"Touch mode: {display} not found for scaling"); return; }
        // Relative values count steps from the display's recommended scale, which is step -Min.
        var index = Array.IndexOf(Steps, scale);
        if (index < 0) index = Array.FindLastIndex(Steps, v => v <= scale);
        var rel = Math.Clamp(index + dpi.Min, dpi.Min, dpi.Max);
        if (rel != dpi.Cur) Dpi.SetRelative(display, rel);
    }

    // ---- registry helpers ------------------------------------------------------------------------

    static Dictionary<string, int?> MonitorDpiValues()
    {
        var map = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.CurrentUser.OpenSubKey(PerMonitorKey);
        if (root == null) return map;
        foreach (var name in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(name);
            map[name] = k?.GetValue("DpiValue") is int v ? v : null;
        }
        return map;
    }

    static void RestoreMonitorDpi(string key, int? dpi)
    {
        using var k = Registry.CurrentUser.CreateSubKey($@"{PerMonitorKey}\{key}");
        if (dpi is int v) k.SetValue("DpiValue", v, RegistryValueKind.DWord);
        else k.DeleteValue("DpiValue", false);
    }

    static int? GetDword(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name) is int v ? v : null;
    }

    static void SetDword(string key, string name, int value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    static void RestoreDword(string key, string name, int? value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        if (value is int v) k.SetValue(name, v, RegistryValueKind.DWord);
        else k.DeleteValue(name, false);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>Explorer re-reads the taskbar settings on this (what the Settings app sends).</summary>
    static void BroadcastTaskbarChange()
    {
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A /*WM_SETTINGCHANGE*/, IntPtr.Zero, "TraySettings", 0x0002 /*SMTO_ABORTIFHUNG*/, 1000, out _);
    }

    // ---- per-display scaling (DisplayConfig's undocumented DPI get/set, as used by Settings) ----

    static class Dpi
    {
        public readonly record struct Scale(int Min, int Cur, int Max);

        [StructLayout(LayoutKind.Sequential)] struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct Header { public int Type; public int Size; public Luid Adapter; public uint Id; }
        [StructLayout(LayoutKind.Sequential)] struct GetScale { public Header H; public int Min, Cur, Max; }
        [StructLayout(LayoutKind.Sequential)] struct SetScale { public Header H; public int Rel; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SourceName { public Header H; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] byte[] pathArray, ref uint modes, [Out] byte[] modeArray, IntPtr topology);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetInfo(ref GetScale p);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetInfo(ref SourceName p);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] static extern int SetInfo(ref SetScale p);

        const uint QDC_ONLY_ACTIVE_PATHS = 2;
        const int PathSize = 72, ModeSize = 64;  // DISPLAYCONFIG_PATH_INFO / _MODE_INFO
        const int GET_SOURCE_NAME = 1, GET_DPI_SCALE = -3, SET_DPI_SCALE = -4;

        /// <summary>The active source (adapter + id) whose GDI name is <paramref name="display"/>.</summary>
        static (Luid, uint)? Find(string display)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0) return null;
                var paths = new byte[np * PathSize];
                var modes = new byte[nm * ModeSize];
                var rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
                if (rc == 122 /*ERROR_INSUFFICIENT_BUFFER: topology changed*/) continue;
                if (rc != 0) return null;
                for (int i = 0; i < np; i++)
                {
                    var o = i * PathSize;  // path starts with sourceInfo { LUID adapterId; UINT32 id; ... }
                    var luid = new Luid { Low = BitConverter.ToUInt32(paths, o), High = BitConverter.ToInt32(paths, o + 4) };
                    var id = BitConverter.ToUInt32(paths, o + 8);
                    var name = new SourceName { H = Hdr(GET_SOURCE_NAME, Marshal.SizeOf<SourceName>(), luid, id) };
                    if (GetInfo(ref name) == 0 && string.Equals(name.Name, display, StringComparison.OrdinalIgnoreCase)) return (luid, id);
                }
                return null;
            }
            return null;
        }

        public static Scale? Get(string display)
        {
            if (Find(display) is not { } src) return null;
            var (luid, id) = src;
            var p = new GetScale { H = Hdr(GET_DPI_SCALE, Marshal.SizeOf<GetScale>(), luid, id) };
            return GetInfo(ref p) == 0 ? new Scale(p.Min, p.Cur, p.Max) : null;
        }

        public static void SetRelative(string display, int rel)
        {
            if (Find(display) is not { } src) return;
            var (luid, id) = src;
            var p = new SetScale { H = Hdr(SET_DPI_SCALE, Marshal.SizeOf<SetScale>(), luid, id), Rel = rel };
            var rc = SetInfo(ref p);
            if (rc != 0) Log.Warn($"Touch mode: setting scale on {display} failed ({rc})");
        }

        static Header Hdr(int type, int size, Luid luid, uint id) => new() { Type = type, Size = size, Adapter = luid, Id = id };
    }
}
