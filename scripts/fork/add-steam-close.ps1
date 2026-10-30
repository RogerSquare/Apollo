# One-time setup: make Apollo's "Steam" app close the running game when the session ends.
# - Self-elevates (UAC prompt).
# - Copies close-steam-game.ps1 into the install's scripts folder.
# - Backs up config\apps.json, then appends an undo command to the Steam app (undo commands
#   run last-to-first, so the game closes before Big Picture does).
# - Restarts ApolloService so it reloads apps.json. Run it while nobody is streaming.
# Keep this file ASCII-only (Windows PowerShell 5.1 without BOM).
param(
    [string]$InstallDir = 'C:\Program Files\Apollo',
    [string]$AppName = 'Steam'
)
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
        '-InstallDir', "`"$InstallDir`"", '-AppName', "`"$AppName`"")
    $p = Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait -PassThru
    exit $p.ExitCode
}

$script = Join-Path $InstallDir 'scripts\close-steam-game.ps1'
Copy-Item (Join-Path $PSScriptRoot 'close-steam-game.ps1') $script -Force
$undo = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""

$appsFile = Join-Path $InstallDir 'config\apps.json'
$apps = Get-Content $appsFile -Raw | ConvertFrom-Json
$app = $apps.apps | Where-Object { $_.name -eq $AppName } | Select-Object -First 1
if (-not $app) { throw "No app named $AppName in $appsFile" }

$prep = @($app.'prep-cmd' | Where-Object { $_ })
if ($prep | Where-Object { $_.undo -like '*close-steam-game.ps1*' }) {
    Write-Host 'Undo command already present; script refreshed.'
    exit 0
}
$prep += [pscustomobject]@{ do = ''; undo = $undo; elevated = $false }
$app.'prep-cmd' = $prep

Copy-Item $appsFile ("$appsFile.before-steam-close") -Force
[IO.File]::WriteAllText($appsFile, ($apps | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))

Restart-Service ApolloService -Force
Write-Host "Added to $AppName : $undo"
