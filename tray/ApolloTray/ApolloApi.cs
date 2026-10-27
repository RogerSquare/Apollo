using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ApolloTray;

public sealed record StreamInfo(string Uuid, string Name, int Width, int Height, int Fps, int BitrateKbps, string Codec, bool Hdr, DateTimeOffset StartedAt);

/// <summary>The running app and the host policy it is using (Apollo's /api/session "app").</summary>
public sealed record AppInfo(string Name, string ClientName, bool VirtualDisplay, bool Paused, string Display, bool MuteDesk, string? DisplayName);

public sealed record ApolloStatus(bool Reachable, StreamInfo? Stream, AppInfo? App, string GlobalDisplay, bool GlobalMuteDesk)
{
    public static readonly ApolloStatus Offline = new(false, null, null, "stock", false);
    /// <summary>Something is running, so toggles change it live (also while paused between clients).</summary>
    public bool Live => App != null;
    public string Display => App?.Display ?? GlobalDisplay;
    public bool MuteDesk => App?.MuteDesk ?? GlobalMuteDesk;
}

/// <summary>
/// Talks to the Apollo fork's config API on loopback, authenticated with the per-start token Apollo
/// writes to %ProgramData%\ApolloFork\api.token (no web UI password needed).
/// </summary>
public sealed class ApolloApi : IDisposable
{
    public const string WebUi = "https://localhost:47990";
    static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ApolloFork", "api.token");

    readonly HttpClient _http;
    string? _token;

    public ApolloApi()
    {
        var handler = new HttpClientHandler
        {
            // Apollo's web UI cert is self-signed; we only ever talk to loopback.
            ServerCertificateCustomValidationCallback = (req, _, _, _) => req.RequestUri is { IsLoopback: true },
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(WebUi + "/"), Timeout = TimeSpan.FromSeconds(4) };
    }

    public async Task<ApolloStatus> GetStatusAsync()
    {
        try
        {
            using var doc = await SendAsync(HttpMethod.Get, "api/session", null);
            if (doc == null) return ApolloStatus.Offline;
            var root = doc.RootElement;

            StreamInfo? stream = null;
            foreach (var s in root.GetProperty("sessions").EnumerateArray())
            {
                if (!s.GetProperty("running").GetBoolean()) continue;
                stream = new StreamInfo(
                    s.GetProperty("uuid").GetString() ?? "",
                    s.GetProperty("name").GetString() ?? "Device",
                    s.GetProperty("width").GetInt32(), s.GetProperty("height").GetInt32(), s.GetProperty("fps").GetInt32(),
                    s.GetProperty("bitrate_kbps").GetInt32(), s.GetProperty("codec").GetString() ?? "", s.GetProperty("hdr").GetBoolean(),
                    DateTimeOffset.FromUnixTimeMilliseconds(s.GetProperty("started_at_ms").GetInt64()));
                break;
            }

            AppInfo? app = null;
            if (root.GetProperty("app") is { ValueKind: JsonValueKind.Object } a)
            {
                var p = a.GetProperty("policy");
                app = new AppInfo(a.GetProperty("name").GetString() ?? "", a.GetProperty("client_name").GetString() ?? "",
                    a.GetProperty("virtual_display").GetBoolean(), a.GetProperty("paused").GetBoolean(),
                    p.GetProperty("display").GetString() ?? "stock", p.GetProperty("mute_wavelink").GetBoolean(),
                    a.TryGetProperty("display_name", out var dn) && dn.GetString() is { Length: > 0 } d ? d : null);
            }

            var g = root.GetProperty("global");
            return new ApolloStatus(true, stream, app, g.GetProperty("display").GetString() ?? "stock", g.GetProperty("mute_wavelink").GetBoolean());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        {
            return ApolloStatus.Offline;
        }
    }

    /// <summary>Live (running session, remembered for its device) or the global default when idle.</summary>
    public Task<bool> SetPolicyAsync(bool live, string? display = null, bool? muteDesk = null)
    {
        var body = new System.Collections.Generic.Dictionary<string, object>();
        if (display != null) body["display"] = display;
        if (muteDesk != null) body["mute_wavelink"] = muteDesk.Value;
        return PostOkAsync(live ? "api/session/host-policy" : "api/host-policy", body);
    }

    /// <summary>Complete a pairing the client started (it shows the PIN).</summary>
    public Task<bool> PairAsync(string pin, string name) => PostOkAsync("api/pin", new { pin, name });

    /// <summary>End the stream: closes the running app, which also restores the desk.</summary>
    public Task<bool> EndStreamAsync() => PostOkAsync("api/apps/close", new { });

    async Task<bool> PostOkAsync(string path, object body)
    {
        try
        {
            using var doc = await SendAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body));
            return doc?.RootElement.TryGetProperty("status", out var s) == true && s.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex)
        {
            Log.Warn($"POST {path}: {ex.Message}");
            return false;
        }
    }

    async Task<JsonDocument?> SendAsync(HttpMethod method, string path, string? json)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            _token ??= ReadToken();
            if (_token == null) return null;
            using var req = new HttpRequestMessage(method, path);
            req.Headers.Add("X-Apollo-Token", _token);
            // While we poll, Apollo mutes its own informational toasts (our flyout replaces them).
            req.Headers.Add("X-Apollo-Companion", "tray");
            if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                _token = null; // Apollo restarted and rotated it: re-read once
                continue;
            }
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }
        return null;
    }

    static string? ReadToken()
    {
        try { return File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() : null; }
        catch { return null; }
    }

    public void Dispose() => _http.Dispose();
}
