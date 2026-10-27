# Deploy the fork build into the installed Apollo (C:\Program Files\Apollo).
# - Self-elevates (UAC prompt).
# - First run snapshots the original install (minus config) to
#   %ProgramData%\ApolloFork\backup-original so rollback.ps1 can restore it.
# - Never touches config\ (sunshine.conf, apps.json, sunshine_state.json, credentials).
# Keep this file ASCII-only (Windows PowerShell 5.1 without BOM).
param(
    [string]$InstallDir = 'C:\Program Files\Apollo',
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\..\build')
)
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
        '-InstallDir', "`"$InstallDir`"", '-BuildDir', "`"$((Resolve-Path $BuildDir).Path)`"")
    $p = Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait -PassThru
    exit $p.ExitCode
}

$BuildDir = (Resolve-Path $BuildDir).Path
$exe = Join-Path $BuildDir 'sunshine.exe'
if (-not (Test-Path $exe)) { throw "No build found at $exe - run scripts\fork\build.ps1 first" }

$stateRoot = Join-Path $env:ProgramData 'ApolloFork'
$backup = Join-Path $stateRoot 'backup-original'
$logFile = Join-Path $stateRoot 'deploy.log'
New-Item -ItemType Directory -Force $stateRoot | Out-Null
function Log($m) { $line = "[{0}] {1}" -f (Get-Date -Format s), $m; Write-Host $line; Add-Content $logFile $line }

if (-not (Test-Path $backup)) {
    Log "Snapshotting original install to $backup"
    robocopy $InstallDir $backup /E /XD (Join-Path $InstallDir 'config') /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Backup failed (robocopy $LASTEXITCODE)" }
}

Log 'Stopping ApolloService'
Stop-Service ApolloService -Force
Get-Process sunshine -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

try {
    Log "Copying sunshine.exe from $BuildDir"
    Copy-Item $exe (Join-Path $InstallDir 'sunshine.exe') -Force
    foreach ($dll in Get-ChildItem $BuildDir -Filter *.dll -File) {
        Copy-Item $dll.FullName $InstallDir -Force
    }
    # assets: web UI + shaders + images. robocopy follows the shaders junction.
    robocopy (Join-Path $BuildDir 'assets') (Join-Path $InstallDir 'assets') /E /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Asset copy failed (robocopy $LASTEXITCODE)" }
} finally {
    Log 'Starting ApolloService'
    Start-Service ApolloService
}
$ver = (Get-Item (Join-Path $InstallDir 'sunshine.exe')).VersionInfo.ProductVersion
Log "Deployed fork build (version $ver)"
