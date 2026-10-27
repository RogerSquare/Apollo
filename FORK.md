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

## Fork changes vs upstream v0.4.6

- `src/platform/windows/input.cpp`: skip local synthetic-pointer declarations on mingw-w64 >= 15,
  which now ships them in `winuser.h`.
