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

## Host policy (per-device display/audio automation)

A global default plus optional per-client overrides decide what the host does while a client streams.

| Field | Values | Meaning |
|---|---|---|
| `display` | `stock` (default) | Apollo's existing behavior |
| | `primary` | Virtual display becomes primary, physical monitor stays extended |
| | `exclusive` | Virtual display becomes primary, physical monitor is detached |
| `mute_wavelink` | `false` (default) / `true` | Mute the Wave Link output while streaming |

Defaults equal stock Apollo, so an unconfigured install behaves exactly like upstream.

Storage:
- Global: `sunshine.conf` keys `host_display_policy = stock|primary|exclusive`, `host_mute_wavelink = enabled|disabled`.
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

`/api/clients/list` also returns `host_display` and `host_mute_wavelink` per client. The existing
`/api/clients/update` endpoint is unchanged and never touches the override.

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
