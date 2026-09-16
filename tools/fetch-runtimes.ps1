<#
.SYNOPSIS
    Downloads and stages bundled portable runtimes (mpv, Node.js LTS) and static-server.js
    into runtime/bin/ structure for ExhibitOS.
#>

[CmdletBinding()]
param(
    [string]$TargetDir = "$PSScriptRoot\..\runtime\bin",
    [string]$NodeVersion = "v22.14.0",
    [string]$MpvVersion = "20250223-git-55cbca0"
)

$ErrorActionPreference = "Stop"

$nodeTargetDir = Join-Path $TargetDir "node"
$mpvTargetDir = Join-Path $TargetDir "mpv"

New-Item -ItemType Directory -Force -Path $nodeTargetDir | Out-Null
New-Item -ItemType Directory -Force -Path $mpvTargetDir | Out-Null

Write-Host "Staging ExhibitOS Runtimes into: $TargetDir" -ForegroundColor Cyan

# 1. Copy static-server.js
$staticServerSrc = Join-Path $PSScriptRoot "..\src\ExhibitOS.StaticServer\static-server.js"
if (Test-Path $staticServerSrc) {
    Copy-Item $staticServerSrc (Join-Path $nodeTargetDir "static-server.js") -Force
    Write-Host "✓ Staged static-server.js -> $nodeTargetDir" -ForegroundColor Green
}

# 2. Node.js LTS Portable (node.exe)
$nodeExeTarget = Join-Path $nodeTargetDir "node.exe"
if (-not (Test-Path $nodeExeTarget)) {
    Write-Host "Downloading portable Node.js ($NodeVersion)..." -ForegroundColor Yellow
    $nodeZipUrl = "https://nodejs.org/dist/$NodeVersion/node-$NodeVersion-win-x64.zip"
    $tempZip = Join-Path $env:TEMP "node-$NodeVersion-win-x64.zip"
    $tempExtract = Join-Path $env:TEMP "node-extract-$NodeVersion"

    try {
        Invoke-WebRequest -Uri $nodeZipUrl -OutFile $tempZip -UseBasicParsing
        Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force
        Copy-Item "$tempExtract\node-$NodeVersion-win-x64\node.exe" $nodeExeTarget -Force
        Write-Host "✓ Staged portable node.exe -> $nodeExeTarget" -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not download Node.js automatically: $_"
    }
    finally {
        Remove-Item $tempZip, $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "✓ Node.exe already present: $nodeExeTarget" -ForegroundColor Green
}

# 3. mpv Standalone Player (mpv.exe)
$mpvExeTarget = Join-Path $mpvTargetDir "mpv.exe"
if (-not (Test-Path $mpvExeTarget)) {
    Write-Host "Fetching latest standalone mpv release info..." -ForegroundColor Yellow
    try {
        $releaseJson = Invoke-RestMethod -Uri "https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest" -UseBasicParsing
        $mpvAsset = $releaseJson.assets | Where-Object { $_.name -match '^mpv-x86_64-\d+.*\.7z$' } | Select-Object -First 1

        if ($mpvAsset) {
            Write-Host "Downloading mpv ($($mpvAsset.name))..." -ForegroundColor Yellow
            $temp7z = Join-Path $env:TEMP $mpvAsset.name
            $tempMpvDir = Join-Path $env:TEMP "mpv-extract-$([Guid]::NewGuid())"
            New-Item -ItemType Directory -Path $tempMpvDir -Force | Out-Null

            Invoke-WebRequest -Uri $mpvAsset.browser_download_url -OutFile $temp7z -UseBasicParsing
            
            # Windows 10/11 built-in tar.exe supports .7z extraction
            & tar.exe -xf $temp7z -C $tempMpvDir
            
            $extractedMpv = Get-ChildItem $tempMpvDir -Filter "mpv.exe" -Recurse | Select-Object -First 1
            if ($extractedMpv) {
                Copy-Item $extractedMpv.FullName $mpvExeTarget -Force
                Write-Host "✓ Staged standalone mpv.exe -> $mpvExeTarget" -ForegroundColor Green
            } else {
                Write-Warning "mpv.exe not found in extracted archive."
            }

            Remove-Item $temp7z, $tempMpvDir -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            Write-Warning "Could not find x86_64 asset in mpv release."
        }
    }
    catch {
        Write-Warning "Could not automatically fetch mpv: $_"
        Write-Host "To bundle manually: place mpv.exe into: $mpvTargetDir" -ForegroundColor Cyan
    }
} else {
    Write-Host "✓ mpv.exe already present: $mpvExeTarget" -ForegroundColor Green
}

Write-Host "Runtime staging setup completed." -ForegroundColor Cyan
