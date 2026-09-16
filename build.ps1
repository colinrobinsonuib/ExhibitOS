<#
.SYNOPSIS
    Full fresh build of ExhibitOS solution, runtimes, and distribution bundle.
.PARAMETER Clean
    Performs a clean build, removing previous build artifacts and dist/ directory.
.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release).
.PARAMETER SkipTests
    Skips running the test suite.
#>

[CmdletBinding()]
param(
    [switch]$Clean,
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$dotnetExe = "$env:USERPROFILE\.dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    $dotnetExe = (Get-Command dotnet.exe -ErrorAction SilentlyContinue)?.Source
}
if (-not $dotnetExe) {
    throw ".NET SDK was not found. Please ensure .NET SDK is installed."
}

$root = $PSScriptRoot

if ($Clean) {
    Write-Host "Cleaning solution and dist directory..." -ForegroundColor Yellow
    & $dotnetExe clean "$root\ExhibitOS.slnx" -c $Configuration | Out-Null
    if (Test-Path "$root\dist") {
        Remove-Item "$root\dist" -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "✓ Clean completed." -ForegroundColor Green
}

# 1. Fetch & Stage Runtimes (mpv, node, static-server.js)
Write-Host "`n[1/3] Staging runtimes (mpv, node, static-server.js)..." -ForegroundColor Cyan
& "$root\tools\fetch-runtimes.ps1"

# 2. Run Test Suite
if (-not $SkipTests) {
    Write-Host "`n[2/3] Running test suite..." -ForegroundColor Cyan
    & $dotnetExe test "$root\ExhibitOS.slnx" -c $Configuration --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed. Build aborted."
    }
} else {
    Write-Host "`n[2/3] Skipping tests." -ForegroundColor Yellow
}

# 3. Build & Publish Distribution Bundle
Write-Host "`n[3/3] Publishing distribution bundle to dist/..." -ForegroundColor Cyan
& "$root\tools\build-distribution.ps1" -Configuration $Configuration

Write-Host "`n=======================================================" -ForegroundColor Green
Write-Host "✓ Fresh build succeeded!" -ForegroundColor Green
Write-Host "Single-file target installer: $root\dist-installer\ExhibitOSSetup.exe" -ForegroundColor Green
Write-Host "Expanded installation tree (for diagnostics): $root\dist" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Green
