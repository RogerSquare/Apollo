using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ApolloTray;

public partial class App : Application
{
    Mutex? _single;
    ApolloApi _api = null!;
    FlyoutWindow _flyout = null!;
    TrayIcon _tray = null!;
    NativeMenu? _menu;
    DispatcherTimer _poll = null!;
    ApolloStatus _status = ApolloStatus.Offline;
    bool _polling;

    const string ShowEvent = @"Local\ApolloTray.Show";
    const string MenuEvent = @"Local\ApolloTray.Menu";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "ApolloTray";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var wantMenu = Array.IndexOf(e.Args, "--menu") >= 0;
        _single = new Mutex(true, @"Local\ApolloTray.SingleInstance", out var first);
        if (!first)
        {
            // Launching again (Start menu, shortcut) pops the running instance's flyout instead.
            try { EventWaitHandle.OpenExisting(wantMenu ? MenuEvent : ShowEvent).Set(); } catch { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) => { Log.Error("Unhandled", ex.Exception); ex.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("Unobserved task", ex.Exception); ex.SetObserved(); };
        Log.Info($"Apollo tray {typeof(App).Assembly.GetName().Version} starting");

        // Installed copies start with Windows; dev builds under bin\ never touch the Run key.
        if (!IsDevBuild && !StartsWithWindows) SetStartWithWindows(true);

        _api = new ApolloApi();
        _flyout = new FlyoutWindow(_api);
        _flyout.RefreshRequested += () => _ = PollAsync();

        _tray = new TrayIcon();
        _tray.LeftClick += () => Dispatcher.Invoke(_flyout.Toggle);
        _tray.RightClick += () => Dispatcher.Invoke(ShowMenu);
        ListenForSignal(ShowEvent, () => _flyout.Toggle());
        ListenForSignal(MenuEvent, ShowMenu);

        _poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = PollAsync(), Dispatcher);
        UpdateTray();
        _ = PollAsync();
    }

    async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var next = await _api.GetStatusAsync();
            if (next.Reachable != _status.Reachable) Log.Info(next.Reachable ? "Apollo reachable" : "Apollo not reachable");
            if ((next.Stream?.Name) != (_status.Stream?.Name)) Log.Info(next.Stream != null ? $"Streaming to {next.Stream.Name}" : "Not streaming");
            _status = next;
            _flyout.SetStatus(next);
            UpdateTray();
        }
        finally { _polling = false; }
    }

    void UpdateTray()
    {
        var s = _status;
        var glyph = !s.Reachable ? "" : s.Stream != null ? "" : "";
        var tip = !s.Reachable ? "Apollo · not running"
            : s.Stream != null ? $"Apollo · streaming to {s.Stream.Name}{(s.Display == "exclusive" ? " · monitor off" : "")}{(s.MuteDesk ? " · desk muted" : "")}"
            : s.App != null ? $"Apollo · {s.App.Name} paused"
            : "Apollo · ready";
        _tray.Update(glyph, tip);
    }

    void ShowMenu()
    {
        var s = _status;
        var items = new List<NativeMenu.Item>
        {
            new("Desk monitor on", () => _ = Toggle(display: s.Display == "exclusive" ? "primary" : "exclusive"),
                Enabled: s.Reachable, Checked: s.Display != "exclusive", Radio: false),
            new("Desk audio on", () => _ = Toggle(mute: !s.MuteDesk),
                Enabled: s.Reachable, Checked: !s.MuteDesk, Radio: false),
            NativeMenu.Item.Separator,
        };
        if (s.Live) items.Add(new("Disconnect", () => _ = EndAsync()));
        items.Add(new("Open Apollo settings", () => Process.Start(new ProcessStartInfo(ApolloApi.WebUi) { UseShellExecute = true })));
        if (!IsDevBuild) items.Add(new("Start with Windows", () => SetStartWithWindows(!StartsWithWindows), Checked: StartsWithWindows, Radio: false));
        items.Add(NativeMenu.Item.Separator);
        items.Add(new("Quit", Quit));
        (_menu ??= new NativeMenu()).Show(items);
    }

    async Task Toggle(string? display = null, bool? mute = null)
    {
        var ok = await _api.SetPolicyAsync(_status.Live, display, mute);
        Log.Info($"Menu {(_status.Live ? "live" : "default")} display={display ?? "-"} mute={mute?.ToString() ?? "-"}: {(ok ? "ok" : "FAILED")}");
        await PollAsync();
    }

    async Task EndAsync()
    {
        Log.Info($"Menu end stream: {(await _api.EndStreamAsync() ? "ok" : "FAILED")}");
        await PollAsync();
    }

    void ListenForSignal(string name, Action act)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        new Thread(() =>
        {
            while (ev.WaitOne()) Dispatcher.Invoke(act);
        }) { IsBackground = true, Name = name }.Start();
    }

    static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ApolloTray.exe");
    static bool IsDevBuild => ExePath.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

    static bool StartsWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunValue) is string v && v.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    static void SetStartWithWindows(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(RunValue, $"\"{ExePath}\"");
            else k.DeleteValue(RunValue, false);
            Log.Info($"Start with Windows: {on}");
        }
        catch (Exception ex) { Log.Error("Start with Windows", ex); }
    }

    void Quit()
    {
        Log.Info("Quitting");
        _poll.Stop();
        _tray.Dispose();
        _menu?.Dispose();
        _api.Dispose();
        Shutdown();
    }
}
