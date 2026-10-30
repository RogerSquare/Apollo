# RogerSquare/Apollo fork

Personal fork of [ClassicOldSong/Apollo](https://github.com/ClassicOldSong/Apollo), based on tag `v0.4.6`.
Progress is tracked in the Atrium project **Apollo Fork**.

Planned additions: per-device host policy (detach / keep the physical monitor, mute Wave Link
while streaming), a live session API, a Windows 11 style web UI, and a quick-settings tray app.

## Build (Windows)

Prerequisites: MSYS2 at `C:\msys64` with the UCRT64 packages from `docs/building.md`
(plus `mingw-w64-ucrt-x86_64-ninja`).

```powershell
.\scripts\fork\build.ps1            # configure on first run, then incremental ninja build
.\scripts\fork\build.ps1 -Reconfigure
```

Output: `build\sunshine.exe` and `build\assets\` (web UI, shaders, images).

Toolchain known to work (2026-10): GCC 16.2, CMake 4.4, mingw-w64 15.

## Deploy into the installed Apollo

```powershell
.\scripts\fork\deploy.ps1     # UAC prompt; stops ApolloService, swaps exe + assets, restarts
.\scripts\fork\rollback.ps1   # restore the original install
```

- The first deploy snapshots the original install (minus `config\`) to
  `%ProgramData%\ApolloFork\backup-original`. Rollback restores from there.
- `config\` (sunshine.conf, apps.json, sunshine_state.json, credentials) is never touched.
- Deploy log: `%ProgramData%\ApolloFork\deploy.log`.

### Close the Steam game when the session ends

The Steam app only launches Big Picture (`detached`), so a game started from it belongs to
Steam and Apollo has nothing to terminate on quit. `scripts\fork\close-steam-game.ps1` reads
Steam's `RunningAppID`, finds the game's install folder from the library manifests, and closes
every process running from it (WM_CLOSE, then force after 8 s). Log:
`%LOCALAPPDATA%\ApolloTray\close-steam-game.log`.

```powershell
.\scripts\fork\add-steam-close.ps1   # one-time, while not streaming: UAC, adds the undo command
                                     # to the Steam app (backs up apps.json), restarts Apollo
```

It runs on quit (or on disconnect only if the app has terminate-on-pause), not on a plain
disconnect, so a paused session can still be resumed into the game.

## Host policy (per-device display/audio automation)

A global default plus optional per-client overrides decide what the host does while a client streams.

| Field | Values | Meaning |
|---|---|---|
| `display` | `stock` (default) | Apollo's existing behavior |
| | `primary` | Virtual display becomes primary, physical monitor stays extended |
| | `exclusive` | Virtual display becomes primary, physical monitor is detached |
| `mute_wavelink` | `false` (default) / `true` | Mute the Wave Link output while streaming |

Defaults equal stock Apollo, so an unconfigured install behaves exactly like upstream.

Display behavior (only when Apollo uses a virtual display for the session):
- `primary`: libdisplaydevice EnsurePrimary on the virtual display, then every other active display is
  placed touching it on its left, top-aligned (`VDISPLAY::arrangeOthersLeftOf`, not saved to the Windows DB).
- `exclusive`: libdisplaydevice EnsureOnlyDisplay; physical monitors are detached for the session.
- The initial topology is persisted (`config\display_device.state`) and restored synchronously on
  terminate *before* the virtual display is removed, on pause (re-applied on resume using the resuming
  client's policy), and on the next start if Apollo crashed mid-session.
- Fork change: `display_device::init` no longer deletes that state file when `dd_configuration_option`
  is disabled, since host policy uses it regardless.

Storage:
- Global: `sunshine.conf` keys `host_display_policy = stock|primary|exclusive`, `host_mute_wavelink = enabled|disabled`,
  `host_touch_scale = off|100-300` (steps of 25; see Touch mode below).
- Per client: `host_display` / `host_mute_wavelink` on each `named_devices` entry in `sunshine_state.json`,
  only written when overridden. Stock Apollo ignores these keys (and drops them if it rewrites the file).

API (same cookie auth as the rest of `/api`):

```text
GET  /api/host-policy            -> { global: {display, mute_wavelink},
                                      clients: [{uuid, name, override: {display, mute_wavelink}, effective: {...}}] }
POST /api/host-policy            {display?, mute_wavelink?}  partial update, persisted, live for next session
POST /api/clients/host-policy    {uuid, display?, mute_wavelink?}  replaces the override;
                                  omitted fields = "inherit"; mute_wavelink is "inherit"|"on"|"off"
```

Live session (for the tray app and Handoff):

```text
GET  /api/session                -> { streaming, sessions: [{uuid, name, address, width, height, fps,
                                      bitrate_kbps, codec, hdr, running, started_at_ms}],
                                      app: null | {name, client_uuid, client_name, virtual_display, paused,
                                                   policy: {display, mute_wavelink}, applied_display},
                                      global: {...} }
POST /api/session/host-policy    {display?, mute_wavelink?}  applies to the running session now AND saves it
                                  as that device's override (web-UI launches: live only). status false when idle.
```

`app.policy` is what the session wants; `app.applied_display` is the topology right now (stock while paused).
Live changes switch topology in place (stock <-> primary <-> exclusive); a change made while paused is
applied on resume. `mute_wavelink` is state only in Apollo; Handoff acts on it.

### Local API token

Companion apps authenticate without the web UI password: on every start Apollo writes a random token to
`%ProgramData%\ApolloFork\api.token` (protected DACL: SYSTEM + Administrators full, INTERACTIVE read) and
accepts it in the `X-Apollo-Token` header **from loopback only**. Re-read the file on 401 (it rotates on
restart). Browsers can't read the file, and a page can't send the custom header cross-origin without a
CORS preflight Apollo never grants.

`/api/clients/list` also returns `host_display`, `host_mute_wavelink` and `host_touch_scale` per client. The existing
`/api/clients/update` endpoint is unchanged and never touches the override.

## Tray app (`tray/ApolloTray`)

C# .NET 10 WPF (Fluent) tray app in the same style as Handoff: left-click opens a Windows 11 flyout,
right-click a native menu. Talks to Apollo only through the local token API above.

- Flyout: who's streaming (resolution, fps, codec, bitrate, duration) + Disconnect; **Desk monitor**
  tile (on = `primary`, off = `exclusive`) and **Desk audio** tile (`mute_wavelink`, acted on by Handoff).
  While something runs the tiles change the session live and save it for that device; when idle they
  set the global defaults. Pair a device: PIN + name -> `POST /api/pin`.
- When a device connects the flyout opens by itself on the **streamed display** (from `app.display_name`
  in `/api/session`, placed per monitor in pixels), so it can be adjusted from the device; it closes
  after 15 s unless used. Toggle: menu > "Show when a device connects" (HKCU\Software\ApolloTray).
- **Touch mode** tile (`touch_scale`): see below. The menu's Touch mode submenu picks the scale.
- **Pause media when a game starts** (menu toggle, default on): when Steam's `RunningAppID` changes to
  a game, everything playing in Windows' media controls (a browser video, Spotify) is paused. Works
  streaming or not. `--pause-media` runs the pause once and exits (testing).
- Menu: the same toggles, Disconnect, Open Apollo settings, Start with Windows, Quit.
- Log: `%LOCALAPPDATA%\ApolloTray\apollotray.log`. Re-launching the exe opens the flyout; `--menu` opens the menu.

```powershell
# install / update (quit the running tray first)
Get-Process ApolloTray -EA SilentlyContinue | Stop-Process
dotnet publish tray/ApolloTray -c Release -r win-x64 --self-contained false -o "$env:LOCALAPPDATA\Programs\ApolloTray"
```

Installed copies add themselves to HKCU Run on first start; builds under `bin\` never do.

### Touch mode

For small touch screens (the Switch). Apollo only stores `touch_scale` (global default + per device, also
settable live); the tray applies it, because these are per-user settings and Apollo runs as SYSTEM.
While a device with touch mode is streaming to a virtual display (not while paused):

- the stream's display scaling is set to the chosen percent (DisplayConfig's undocumented DPI get/set,
  relative to the display's recommended step; clamped to what Windows allows for that resolution,
  e.g. 175% max at 1080p)
- taskbar: search box -> search icon, Task View hidden (`WM_SETTINGCHANGE "TraySettings"` applies it live)
- touch keyboard opens on tap even with a keyboard attached (`TabletTip\1.7` TouchKeyboardTapInvoke=2)

Originals go to `%LOCALAPPDATA%\ApolloTray\touch-restore.json` before anything changes and are put back
when the stream stops or touch mode is turned off; a leftover file (crash, reboot) is restored on the
next tray start. Scaling is restored live if the display still exists, otherwise in
`HKCU\Control Panel\Desktop\PerMonitorSettings`.

## Tests

```powershell
# separate build dir so the release build stays untouched
C:\msys64\usr\bin\bash.exe -lc "cmake -B build-tests -G Ninja -S . -DBUILD_TESTS=ON -DBUILD_DOCS=OFF -DCMAKE_POLICY_VERSION_MINIMUM=3.5 && ninja -C build-tests test_sunshine"
build-tests\tests\test_sunshine.exe --gtest_filter=HostPolicy*
```

## Fork changes vs upstream v0.4.6

- `src/platform/windows/input.cpp`: skip local synthetic-pointer declarations on mingw-w64 >= 15,
  which now ships them in `winuser.h`.
- `tests/CMakeLists.txt`: exclude upstream's `test_http_pairing.cpp`, which no longer compiles against
  Apollo's own nvhttp signatures.
- Host policy: `src/host_policy.{h,cpp}` (new), `config.{h,cpp}`, `crypto.h`, `nvhttp.{h,cpp}`,
  `confighttp.cpp`, `tests/unit/test_host_policy.cpp`.
