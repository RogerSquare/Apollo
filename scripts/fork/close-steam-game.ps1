# Close the Steam game that is running right now.
# Used as an undo command on Apollo's Steam app: Apollo only launches Big Picture, so a game
# started from it is Steam's child, not Apollo's, and survives the session ending.
# Asks Steam for the running app id, finds its install folder, then closes every process
# running from that folder: politely first, force after -GraceSeconds.
param(
    [int]$AppId = 0,
    [int]$GraceSeconds = 8,
    [string]$Folder = ''   # testing: close processes from this folder, skip the Steam lookup
)
$ErrorActionPreference = 'Stop'

function Log($msg) {
    $dir = Join-Path $env:LOCALAPPDATA 'ApolloTray'
    New-Item -ItemType Directory -Force $dir | Out-Null
    Add-Content (Join-Path $dir 'close-steam-game.log') ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $msg)
}

function Find-GameFolder([int]$id) {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -Name SteamPath).SteamPath -replace '/', '\'
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($lib in ($libraries | Select-Object -Unique)) {
        $acf = Join-Path $lib "steamapps\appmanifest_$id.acf"
        if (Test-Path $acf) {
            $m = Select-String -Path $acf -Pattern '"installdir"\s+"([^"]+)"' | Select-Object -First 1
            if ($m) { return Join-Path $lib ('steamapps\common\' + $m.Matches[0].Groups[1].Value) }
        }
    }
    return $null
}

try {
    $game = $Folder
    if (-not $game) {
        if ($AppId -eq 0) {
            $AppId = [int](Get-ItemProperty 'HKCU:\Software\Valve\Steam' -Name RunningAppID -ErrorAction SilentlyContinue).RunningAppID
        }
        if ($AppId -eq 0) { Log 'no Steam game running'; exit 0 }
        $game = Find-GameFolder $AppId
        if (-not $game) { Log "app $AppId : install folder not found"; exit 0 }
    }

    $prefix = $game.TrimEnd('\') + '\'
    $procs = @(Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    if ($procs.Count -eq 0) { Log "app $AppId : nothing running from $game"; exit 0 }
    Log ("app {0} : closing {1}" -f $AppId, (($procs | ForEach-Object { "$($_.Name)($($_.Id))" }) -join ', '))

    foreach ($p in $procs) { try { [void]$p.CloseMainWindow() } catch {} }
    $deadline = (Get-Date).AddSeconds($GraceSeconds)
    while ((Get-Date) -lt $deadline -and @($procs | Where-Object { -not $_.HasExited }).Count -gt 0) {
        Start-Sleep -Milliseconds 250
        foreach ($p in $procs) { $p.Refresh() }
    }
    $left = @($procs | Where-Object { -not $_.HasExited })
    foreach ($p in $left) { try { Stop-Process -Id $p.Id -Force } catch { Log "could not stop $($p.Name)($($p.Id)): $_" } }
    Log ("app {0} : closed ({1} forced)" -f $AppId, $left.Count)
} catch {
    Log "error: $_"
}
exit 0
