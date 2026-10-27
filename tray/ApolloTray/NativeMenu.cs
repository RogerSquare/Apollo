using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

// Adapted from Handoff (github.com/RogerSquare/handoff).
namespace ApolloTray;

/// <summary>
/// The real Windows popup menu (what Explorer and other tray apps show), with the documented tray-menu
/// rules so it closes when you click elsewhere: the owner window is made foreground before
/// TrackPopupMenuEx, and a WM_NULL is posted to it afterwards.
/// </summary>
public sealed class NativeMenu : IDisposable
{
    /// <summary>Checked items show Windows' radio dot, or a check mark for on/off items (Radio: false).</summary>
    public sealed record Item(string Text, Action? Action, bool Enabled = true, bool Checked = false, bool Radio = true,
        IReadOnlyList<Item>? Children = null)
    {
        public static readonly Item Separator = new("", null);
    }

    readonly HwndSource _owner;

    public NativeMenu()
    {
        // Invisible, zero-size tool window: a menu needs a real (non message-only) foreground owner.
        _owner = new HwndSource(new HwndSourceParameters("ApolloTrayMenuOwner")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = 0x00000080,         // WS_EX_TOOLWINDOW: no taskbar button
            Width = 0,
            Height = 0,
        });
        EnableDarkMenus();
    }

    /// <summary>Show at the mouse pointer and run the chosen item's action (on the calling thread).</summary>
    public void Show(IReadOnlyList<Item> items)
    {
        var actions = new List<Action?>();
        var menu = Build(items, actions);
        try
        {
            GetCursorPos(out var pt);
            SetForegroundWindow(_owner.Handle);
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, pt.X, pt.Y, _owner.Handle, IntPtr.Zero);
            PostMessage(_owner.Handle, 0 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero);
            if (cmd > 0 && cmd <= actions.Count) actions[cmd - 1]?.Invoke();
        }
        finally { DestroyMenu(menu); } // also destroys submenus
    }

    /// <summary>Command ids are positions in <paramref name="actions"/> + 1, across submenus.</summary>
    static IntPtr Build(IReadOnlyList<Item> items, List<Action?> actions)
    {
        var menu = CreatePopupMenu();
        foreach (var it in items)
        {
            if (it == Item.Separator) { AppendMenu(menu, MF_SEPARATOR, 0, null); continue; }
            var enabled = it.Enabled ? 0u : MF_GRAYED;
            if (it.Children != null)
            {
                AppendMenu(menu, MF_POPUP | enabled, (uint)Build(it.Children, actions), it.Text);
                continue;
            }
            actions.Add(it.Action);
            var id = (uint)actions.Count;
            var check = it.Checked && !it.Radio ? MF_CHECKED : 0u;
            AppendMenu(menu, MF_STRING | check | enabled, id, it.Text);
            if (it.Checked && it.Radio) CheckMenuRadioItem(menu, id, id, id, MF_BYCOMMAND);
        }
        return menu;
    }

    /// <summary>
    /// Native menus are light unless the app opts in to dark mode (uxtheme SetPreferredAppMode, the
    /// undocumented ordinal Explorer and Notepad use). AllowDark follows the Windows app theme.
    /// </summary>
    static void EnableDarkMenus()
    {
        try
        {
            SetPreferredAppMode(1 /*AllowDark*/);
            FlushMenuThemes();
        }
        catch { /* older Windows: light menus, still functional */ }
    }

    public void Dispose() => _owner.Dispose();

    const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800, MF_BYCOMMAND = 0x0;
    const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }

    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool CheckMenuRadioItem(IntPtr menu, uint first, uint last, uint check, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point pt);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();
}
