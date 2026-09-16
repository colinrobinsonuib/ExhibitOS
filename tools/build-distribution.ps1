<#
.SYNOPSIS
    Builds and publishes ExhibitOS Manager, Watchdog, and runtime dependencies
    into a deployable distribution directory (dist/).
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\..\dist",
    [string]$InstallerOutputDir = "$PSScriptRoot\..\dist-installer",
    [string]$RuntimeIdentifier = "win-x64"
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
$installerDir = [System.IO.Path]::GetFullPath($InstallerOutputDir)
$distRuntime = Join-Path $distDir "runtime"
$distBinNode = Join-Path $distRuntime "bin\node"
$distBinMpv = Join-Path $distRuntime "bin\mpv"

if (Test-Path $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
if (Test-Path $installerDir) {
    Remove-Item -LiteralPath $installerDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
New-Item -ItemType Directory -Force -Path $distRuntime | Out-Null
New-Item -ItemType Directory -Force -Path $distBinNode | Out-Null
New-Item -ItemType Directory -Force -Path $distBinMpv | Out-Null

# 1. Publish ExhibitWatchdog (Self-Contained)
Write-Host "Publishing ExhibitWatchdog (self-contained $RuntimeIdentifier)..." -ForegroundColor Yellow
$watchdogProj = Join-Path $PSScriptRoot "..\src\ExhibitWatchdog\ExhibitWatchdog.csproj"
& $dotnetExe publish $watchdogProj -c $Configuration -r $RuntimeIdentifier --self-contained true -o $distRuntime -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "ExhibitWatchdog publish failed." }

# 2. Publish ExhibitOSManager (Self-Contained)
Write-Host "Publishing ExhibitOSManager (self-contained $RuntimeIdentifier)..." -ForegroundColor Yellow
$managerProj = Join-Path $PSScriptRoot "..\src\ExhibitOSManager\ExhibitOSManager.csproj"
& $dotnetExe publish $managerProj -c $Configuration -r $RuntimeIdentifier --self-contained true -o $distDir -p:WindowsAppSDKSelfContained=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "ExhibitOSManager publish failed." }

# `dotnet publish -o` does not reliably copy the app's compiled WinUI resource
# artifacts for an unpackaged application. The executable can start without
# these files, but Microsoft.UI.Xaml then fail-fasts as soon as it resolves a
# control resource. Copy the freshly built artifacts explicitly and fail the
# distribution build if either one is unavailable.
$managerBuildRoot = Join-Path (Split-Path $managerProj -Parent) "bin\$Configuration"
foreach ($resourceName in @("App.xbf", "ExhibitOSManager.pri")) {
    $resource = Get-ChildItem -LiteralPath $managerBuildRoot -Recurse -File -Filter $resourceName |
        Where-Object { $_.FullName -notmatch '[\\/]publish[\\/]' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $resource) {
        throw "Required WinUI resource '$resourceName' was not produced for ExhibitOSManager."
    }

    Copy-Item -LiteralPath $resource.FullName -Destination (Join-Path $distDir $resourceName) -Force
}

foreach ($resourceName in @("App.xbf", "ExhibitOSManager.pri")) {
    if (-not (Test-Path -LiteralPath (Join-Path $distDir $resourceName))) {
        throw "Required WinUI resource '$resourceName' is missing from the distribution."
    }
}
Write-Host "✓ Included compiled WinUI resources" -ForegroundColor Green

# Windows App SDK publishes satellite MUI folders for every supported culture.
# ExhibitOS currently ships an English-only UI, so retain only the fallback.
$distRootWithSeparator = $distDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$removedCultureDirectories = 0
foreach ($directory in Get-ChildItem -LiteralPath $distDir -Directory) {
    try {
        $culture = [System.Globalization.CultureInfo]::GetCultureInfo($directory.Name)
    }
    catch {
        continue
    }

    if ($culture.Name -ieq "en-US") {
        continue
    }

    $resolvedDirectory = [System.IO.Path]::GetFullPath($directory.FullName)
    if (-not $resolvedDirectory.StartsWith($distRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove culture directory outside distribution root: $resolvedDirectory"
    }

    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
    $removedCultureDirectories++
}
Write-Host "✓ Removed $removedCultureDirectories unused satellite-language directories" -ForegroundColor Green

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
    throw "mpv.exe not found in runtime/bin/mpv. Run tools/fetch-runtimes.ps1 first."
}

$srcBinNode = Join-Path $PSScriptRoot "..\runtime\bin\node\node.exe"
if (Test-Path $srcBinNode) {
    Copy-Item $srcBinNode (Join-Path $distBinNode "node.exe") -Force
    Write-Host "✓ Bundled offline node.exe into distribution" -ForegroundColor Green
} else {
    throw "node.exe not found in runtime/bin/node. Run tools/fetch-runtimes.ps1 first."
}

# 5. Embed the complete installation tree in one self-extracting setup executable.
Write-Host "Building single-file ExhibitOSSetup.exe..." -ForegroundColor Yellow
$payloadDir = Join-Path $PSScriptRoot "..\src\ExhibitOS.Bootstrapper\obj\payload"
$payloadZip = Join-Path $payloadDir "ExhibitOSPayload.zip"
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
if (Test-Path $payloadZip) {
    Remove-Item -LiteralPath $payloadZip -Force
}
Compress-Archive -Path (Join-Path $distDir "*") -DestinationPath $payloadZip -CompressionLevel Optimal

$bootstrapperProj = Join-Path $PSScriptRoot "..\src\ExhibitOS.Bootstrapper\ExhibitOS.Bootstrapper.csproj"
& $dotnetExe publish $bootstrapperProj -c $Configuration -r $RuntimeIdentifier -o $installerDir --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PayloadPath=$payloadZip
if ($LASTEXITCODE -ne 0) { throw "ExhibitOS single-file setup publish failed." }

$setupExe = Join-Path $installerDir "ExhibitOSSetup.exe"
if (-not (Test-Path $setupExe)) {
    throw "Single-file setup was not generated at '$setupExe'."
}

Write-Host "✓ Staged installation tree: $distDir" -ForegroundColor Green
Write-Host "✓ Copy this single file to the target PC: $setupExe" -ForegroundColor Green
