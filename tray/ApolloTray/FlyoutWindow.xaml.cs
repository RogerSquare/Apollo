using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ApolloTray;

/// <summary>Tray flyout: who's streaming, the two quick toggles, PIN pairing.</summary>
public partial class FlyoutWindow : Window
{
    readonly ApolloApi _api;
    ApolloStatus _status = ApolloStatus.Offline;
    DateTime _hiddenAt;
    bool _busy;

    /// <summary>Ask the app to re-poll now (after a change, or when opening).</summary>
    public event Action? RefreshRequested;

    public FlyoutWindow(ApolloApi api)
    {
        InitializeComponent();
        _api = api;
        SourceInitialized += (_, _) => Native.ApplyAcrylic(this);
        Deactivated += (_, _) => HideFlyout();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideFlyout(); };
        // Content height changes with status (Disconnect, wrapped hints): stay anchored to the corner.
        SizeChanged += (_, _) => { if (IsVisible) Place(); };
        Render();
    }

    void Place()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 12;
        Top = wa.Bottom - ActualHeight - 12;
    }

    public void SetStatus(ApolloStatus status)
    {
        _status = status;
        if (!_busy) Render();
    }

    void Render()
    {
        var s = _status;
        var stream = s.Stream;

        // hero
        HeroDisc.Fill = stream != null ? AccentTint() : Res("SubtleFillColorSecondaryBrush");
        HeroGlyph.Foreground = stream != null ? Res("AccentTextFillColorPrimaryBrush") : Res("TextFillColorSecondaryBrush");
        if (!s.Reachable)
        {
            HeroGlyph.Text = ""; // warning
            HeroName.Text = "Apollo isn't running";
            HeroDetail.Text = "Start the Apollo service to stream";
        }
        else if (stream != null)
        {
            HeroGlyph.Text = ""; // game controller
            HeroName.Text = stream.Name;
            HeroDetail.Text = Describe(stream);
        }
        else if (s.App != null)
        {
            HeroGlyph.Text = ""; // pause
            HeroName.Text = s.App.ClientName.Length > 0 ? s.App.ClientName : s.App.Name;
            HeroDetail.Text = $"{s.App.Name} paused · waiting for the device";
        }
        else
        {
            HeroGlyph.Text = ""; // monitor
            HeroName.Text = "Not streaming";
            HeroDetail.Text = $"Ready on {Environment.MachineName}";
        }
        EndButton.Visibility = s.Live ? Visibility.Visible : Visibility.Collapsed;

        // toggles
        var displayApplies = !s.Live || s.App!.VirtualDisplay;
        MonitorTile.IsEnabled = s.Reachable && displayApplies;
        MonitorTile.IsChecked = s.Display != "exclusive";
        MonitorLabel.Text = !displayApplies ? "Monitor (not virtual)" : MonitorTile.IsChecked == true ? "Monitor on" : "Monitor off";
        AudioTile.IsEnabled = s.Reachable;
        AudioTile.IsChecked = !s.MuteDesk;
        AudioTile.Content = s.MuteDesk ? "" : ""; // mute / volume
        AudioLabel.Text = s.MuteDesk ? "Desk audio off" : "Desk audio on";

        ScopeText.Text = !s.Reachable ? ""
            : s.Live ? $"Applies now and is remembered for {(stream?.Name ?? s.App!.ClientName)}"
            : "Defaults for the next stream";
        // Per-device settings made during a stream win over these defaults.
        ScopeText.ToolTip = s.Live ? null : "Devices you've changed during a stream keep their own setting.";
        ScopeText.Visibility = s.Reachable ? Visibility.Visible : Visibility.Collapsed;

        PinBox.IsEnabled = NameBox.IsEnabled = s.Reachable;
        UpdatePairButton();
        FooterText.Text = s.Reachable ? "" : "Can't reach Apollo on this PC";
    }

    static string Describe(StreamInfo s)
    {
        var mins = (int)(DateTimeOffset.Now - s.StartedAt).TotalMinutes;
        var parts = new[]
        {
            $"{s.Width}×{s.Height}",
            $"{s.Fps} fps",
            s.Codec + (s.Hdr ? " HDR" : ""),
            s.BitrateKbps >= 1000 ? $"{s.BitrateKbps / 1000.0:0.#} Mbps" : $"{s.BitrateKbps} kbps",
            mins < 1 ? "just now" : mins < 60 ? $"{mins} min" : $"{mins / 60} h {mins % 60} min",
        };
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    async void Monitor_Click(object sender, RoutedEventArgs e)
    {
        // Checked now = the user turned the monitor on: keep it extended next to the stream.
        var display = MonitorTile.IsChecked == true ? "primary" : "exclusive";
        await Change(() => _api.SetPolicyAsync(_status.Live, display: display), $"monitor -> {display}");
    }

    async void Audio_Click(object sender, RoutedEventArgs e)
    {
        var mute = AudioTile.IsChecked != true;
        await Change(() => _api.SetPolicyAsync(_status.Live, muteDesk: mute), $"desk audio -> {(mute ? "off" : "on")}");
    }

    async System.Threading.Tasks.Task Change(Func<System.Threading.Tasks.Task<bool>> call, string what)
    {
        _busy = true;
        try
        {
            var ok = await call();
            Log.Info($"{(_status.Live ? "Live" : "Default")} {what}: {(ok ? "ok" : "FAILED")}");
        }
        finally
        {
            _busy = false;
            RefreshRequested?.Invoke(); // re-read so the tiles show what Apollo actually has
        }
    }

    async void End_Click(object sender, RoutedEventArgs e)
    {
        EndButton.IsEnabled = false;
        var ok = await _api.EndStreamAsync();
        Log.Info($"End stream: {(ok ? "ok" : "FAILED")}");
        EndButton.IsEnabled = true;
        RefreshRequested?.Invoke();
    }

    // ---- pairing -----------------------------------------------------------------------------

    void Pin_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

    void Pair_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePairButton();

    void UpdatePairButton()
    {
        PinHint.Visibility = PinBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NameHint.Visibility = NameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PairButton.IsEnabled = _status.Reachable && PinBox.Text.Length == 4 && PinBox.Text.All(char.IsDigit) && NameBox.Text.Trim().Length > 0;
    }

    void Pair_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PairButton.IsEnabled) Pair_Click(sender, e);
    }

    async void Pair_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        PairButton.IsEnabled = false;
        PairHint.Text = "Pairing…";
        var ok = await _api.PairAsync(PinBox.Text, name);
        Log.Info($"Pair '{name}': {(ok ? "ok" : "FAILED")}");
        PairHint.Text = ok ? $"Paired {name}." : "Didn't pair. Start pairing on the device first, then enter the PIN it shows.";
        if (ok) { PinBox.Clear(); NameBox.Clear(); }
        UpdatePairButton();
    }

    void WebUi_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        try { Process.Start(new ProcessStartInfo(ApolloApi.WebUi) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Open web UI", ex); }
    }

    // ---- show / hide (same behavior as Handoff's flyout) -------------------------------------

    public void Toggle()
    {
        // Clicking the tray icon while open deactivates us first; don't immediately reopen.
        if (IsVisible) { HideFlyout(); return; }
        if ((DateTime.Now - _hiddenAt).TotalMilliseconds < 250) return;
        ShowFlyout();
    }

    void ShowFlyout()
    {
        Root.Background = Native.SurfaceTint();   // re-read each open so theme changes apply
        LowerPane.Background = Native.RecessTint();
        PairHint.Text = "Start pairing on the device, then enter the PIN it shows.";
        Render();
        RefreshRequested?.Invoke();
        Show();
        UpdateLayout();
        Place();
        Activate();
        Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    void HideFlyout()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.Now;
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        HideFlyout();
    }

    Brush AccentTint()
    {
        var c = TryFindResource("AccentTextFillColorPrimaryBrush") is SolidColorBrush b ? b.Color : Color.FromRgb(0x60, 0xCD, 0xFF);
        return new SolidColorBrush(Color.FromArgb(0x24, c.R, c.G, c.B));
    }

    Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}
