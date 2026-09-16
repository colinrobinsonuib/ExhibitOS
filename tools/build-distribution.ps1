<#
.SYNOPSIS
    Builds and publishes ExhibitOS Manager, Watchdog, and runtime dependencies
    into a deployable distribution directory (dist/).
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"

$dotnetExe = "$env:USERPROFILE\.dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    $dotnetExe = (Get-Command dotnet.exe -ErrorAction SilentlyContinue)?.Source
}

if (-not $dotnetExe) {
    throw "dotnet SDK was not found."
}

Write-Host "Building and Publishing ExhibitOS ($Configuration)..." -ForegroundColor Cyan

$distDir = [System.IO.Path]::GetFullPath($OutputDir)
$distRuntime = Join-Path $distDir "runtime"
$distBinNode = Join-Path $distRuntime "bin\node"
$distBinMpv = Join-Path $distRuntime "bin\mpv"

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $distRuntime | Out-Null
New-Item -ItemType Directory -Force -Path $distBinNode | Out-Null
New-Item -ItemType Directory -Force -Path $distBinMpv | Out-Null

# 1. Publish ExhibitWatchdog
Write-Host "Publishing ExhibitWatchdog..." -ForegroundColor Yellow
$watchdogProj = Join-Path $PSScriptRoot "..\src\ExhibitWatchdog\ExhibitWatchdog.csproj"
& $dotnetExe publish $watchdogProj -c $Configuration -o $distRuntime --self-contained false

# 2. Publish ExhibitOSManager
Write-Host "Publishing ExhibitOSManager..." -ForegroundColor Yellow
$managerProj = Join-Path $PSScriptRoot "..\src\ExhibitOSManager\ExhibitOSManager.csproj"
& $dotnetExe publish $managerProj -c $Configuration -o $distDir --self-contained false

# 3. Stage static-server.js
$staticServerSrc = Join-Path $PSScriptRoot "..\src\ExhibitOS.StaticServer\static-server.js"
if (Test-Path $staticServerSrc) {
    Copy-Item $staticServerSrc (Join-Path $distBinNode "static-server.js") -Force
}

# 4. Copy staged offline runtimes (mpv.exe, node.exe)
$srcBinMpv = Join-Path $PSScriptRoot "..\runtime\bin\mpv\mpv.exe"
if (Test-Path $srcBinMpv) {
    Copy-Item $srcBinMpv (Join-Path $distBinMpv "mpv.exe") -Force
    Write-Host "✓ Bundled offline mpv.exe into distribution" -ForegroundColor Green
} else {
    Write-Warning "mpv.exe not found in runtime/bin/mpv. Run tools/fetch-runtimes.ps1 first."
}

$srcBinNode = Join-Path $PSScriptRoot "..\runtime\bin\node\node.exe"
if (Test-Path $srcBinNode) {
    Copy-Item $srcBinNode (Join-Path $distBinNode "node.exe") -Force
    Write-Host "✓ Bundled offline node.exe into distribution" -ForegroundColor Green
} else {
    Write-Warning "node.exe not found in runtime/bin/node. Run tools/fetch-runtimes.ps1 first."
}

Write-Host "✓ ExhibitOS Distribution generated at: $distDir" -ForegroundColor Green
