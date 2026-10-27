# Build the fork with MSYS2 UCRT64 (C:\msys64). Output: build\sunshine.exe + build\assets.
# Keep this file ASCII-only (Windows PowerShell 5.1 without BOM).
param([switch]$Reconfigure, [string]$Msys = 'C:\msys64')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$env:MSYSTEM = 'UCRT64'
$env:CHERE_INVOKING = '1'
Push-Location $repo
try {
    $bash = Join-Path $Msys 'usr\bin\bash.exe'
    if ($Reconfigure -or -not (Test-Path 'build\build.ninja')) {
        & $bash -lc 'cmake -B build -G Ninja -S . -DCMAKE_BUILD_TYPE=Release -DBUILD_DOCS=OFF -DBUILD_TESTS=OFF -DCMAKE_POLICY_VERSION_MINIMUM=3.5'
        if ($LASTEXITCODE) { throw "configure failed ($LASTEXITCODE)" }
    }
    & $bash -lc 'ninja -C build'
    if ($LASTEXITCODE) { throw "build failed ($LASTEXITCODE)" }
} finally { Pop-Location }
