# Restore the original Apollo install captured by deploy.ps1's first run.
# Self-elevates. Leaves config\ untouched.
# Keep this file ASCII-only (Windows PowerShell 5.1 without BOM).
param([string]$InstallDir = 'C:\Program Files\Apollo')
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-InstallDir', "`"$InstallDir`"")
    exit $p.ExitCode
}

$backup = Join-Path $env:ProgramData 'ApolloFork\backup-original'
if (-not (Test-Path (Join-Path $backup 'sunshine.exe'))) { throw "No original backup at $backup" }

Stop-Service ApolloService -Force
Get-Process sunshine -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
try {
    # /MIR on assets so fork-only files disappear; top-level files copied over.
    robocopy (Join-Path $backup 'assets') (Join-Path $InstallDir 'assets') /MIR /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Asset restore failed (robocopy $LASTEXITCODE)" }
    Get-ChildItem $backup -File | ForEach-Object { Copy-Item $_.FullName $InstallDir -Force }
} finally {
    Start-Service ApolloService
}
$ver = (Get-Item (Join-Path $InstallDir 'sunshine.exe')).VersionInfo.ProductVersion
Write-Host "Rolled back to original Apollo $ver"
