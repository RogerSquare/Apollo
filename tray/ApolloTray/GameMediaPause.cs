using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Media.Control;

namespace ApolloTray;

/// <summary>
/// Pauses whatever is playing (a video in the browser, Spotify, ...) when a Steam game starts.
/// Steam publishes the running game in HKCU\Software\Valve\Steam\RunningAppID; players are reached
/// through Windows' media controls, the same sessions the volume flyout shows.
/// </summary>
sealed class GameMediaPause
{
    const string SteamKey = @"Software\Valve\Steam";
    const string SettingsKey = @"Software\ApolloTray";

    // -1 until the first check, so a game already running when the tray starts doesn't count.
    int _lastApp = -1;

    /// <summary>Called from the poll timer: cheap, just a registry read.</summary>
    public void Check()
    {
        var app = RunningAppId();
        if (_lastApp >= 0 && app != 0 && app != _lastApp && Enabled)
        {
            Log.Info($"Steam game {app} started");
            _ = PauseAllAsync();
        }
        _lastApp = app;
    }

    static int RunningAppId()
    {
        using var k = Registry.CurrentUser.OpenSubKey(SteamKey);
        return k?.GetValue("RunningAppID") is int v ? v : 0;
    }

    public static async Task PauseAllAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = manager.GetSessions();
            if (sessions.Count == 0) Log.Info("No media sessions to pause");
            foreach (var session in sessions)
            {
                var status = session.GetPlaybackInfo()?.PlaybackStatus;
                if (status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    Log.Info($"Skipped {session.SourceAppUserModelId}: {status}");
                    continue;
                }
                var ok = await session.TryPauseAsync();
                Log.Info($"Paused {session.SourceAppUserModelId}: {(ok ? "ok" : "FAILED")}");
            }
        }
        catch (Exception ex) { Log.Error("Pause media", ex); }
    }

    /// <summary>Pause media when a game starts (default on).</summary>
    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return k?.GetValue("PauseMediaOnGame") is not int v || v != 0;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(SettingsKey);
            k.SetValue("PauseMediaOnGame", value ? 1 : 0, RegistryValueKind.DWord);
            Log.Info($"Pause media when a game starts: {value}");
        }
    }
}
