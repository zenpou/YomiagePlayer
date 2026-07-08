<#
.SYNOPSIS
    Build a self-contained, single-architecture distributable of YomiagePlayer.

.DESCRIPTION
    Runs dotnet publish (self-contained), strips native files for
    architectures other than the target RID (LibVLC and Whisper.net
    ship all Windows/Linux/macOS variants unconditionally), bundles
    ffmpeg (from tools/ffmpeg) and the third-party license notices,
    and drops the result in dist/<Runtime>/. Zips the result unless
    -SkipZip is passed.

    NOTE: comments/messages in this script are kept in plain ASCII —
    Windows PowerShell 5.1 misreads BOM-less UTF-8 script files
    containing Japanese text as the system codepage and can corrupt
    parsing (see docs/plans memory notes). Do not add Japanese text
    here without also adding a UTF-8 BOM.

.PARAMETER Runtime
    Target RID. Default win-x64. (win-arm64 is untested.)

.PARAMETER Configuration
    Build configuration. Default Release.

.PARAMETER SkipZip
    Skip creating the zip archive.

.EXAMPLE
    ./scripts/publish.ps1
.EXAMPLE
    ./scripts/publish.ps1 -Runtime win-x64 -SkipZip
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repoRoot "dist\$Runtime"
$project = Join-Path $repoRoot "src\YomiagePlayer"

function Stop-RunningOutput {
    # A previous build's exe left running from $outDir locks its own DLLs,
    # which breaks publish/prune/zip. Only touch processes running from
    # under $outDir - never anything outside this build's own output.
    if (-not (Test-Path $outDir)) { return }
    $resolved = (Resolve-Path $outDir).Path
    Get-Process -Name "YomiagePlayer" -ErrorAction SilentlyContinue | ForEach-Object {
        $procPath = $_.Path
        if ($procPath -and $procPath.StartsWith($resolved, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "Stopping running instance from previous build: $procPath (pid $($_.Id))"
            Stop-Process -Id $_.Id -Force
            Start-Sleep -Milliseconds 500
        }
    }
}

Write-Host "== YomiagePlayer publish build ($Runtime / $Configuration) ==" -ForegroundColor Cyan

Stop-RunningOutput

if (Test-Path $outDir) {
    Write-Host "Removing existing output: $outDir"
    Remove-Item -Recurse -Force $outDir
}

Write-Host "`n-- dotnet publish --" -ForegroundColor Cyan
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Write-Host "`n-- Pruning native files for other architectures --" -ForegroundColor Cyan

# LibVLCSharp unconditionally copies libvlc/{win-x64,win-x86,win-arm64}/
# regardless of the publish RID; keep only the target one (~80-100MB each).
$libvlcDir = Join-Path $outDir "libvlc"
if (Test-Path $libvlcDir) {
    Get-ChildItem $libvlcDir -Directory | Where-Object { $_.Name -ne $Runtime } | ForEach-Object {
        Write-Host "  removing libvlc\$($_.Name)"
        Remove-Item -Recurse -Force $_.FullName
    }
}

# Whisper.net.Runtime.* unconditionally copies runtimes/<rid>/ and
# runtimes/{cuda,vulkan}/<rid>/ for win/linux/macos; keep only the target RID.
$runtimesDir = Join-Path $outDir "runtimes"
if (Test-Path $runtimesDir) {
    Get-ChildItem $runtimesDir -Directory | ForEach-Object {
        if ($_.Name -in @("cuda", "vulkan")) {
            Get-ChildItem $_.FullName -Directory | Where-Object { $_.Name -ne $Runtime } | ForEach-Object {
                Write-Host "  removing runtimes\$($_.Parent.Name)\$($_.Name)"
                Remove-Item -Recurse -Force $_.FullName
            }
        }
        elseif ($_.Name -ne $Runtime) {
            Write-Host "  removing runtimes\$($_.Name)"
            Remove-Item -Recurse -Force $_.FullName
        }
    }
}

Write-Host "`n-- Bundling ffmpeg --" -ForegroundColor Cyan
$ffmpegSrc = Join-Path $repoRoot "tools\ffmpeg"
$ffmpegDst = Join-Path $outDir "ffmpeg"
$ffmpegExe = Join-Path $ffmpegSrc "ffmpeg.exe"
$ffprobeExe = Join-Path $ffmpegSrc "ffprobe.exe"
if ((Test-Path $ffmpegExe) -and (Test-Path $ffprobeExe)) {
    New-Item -ItemType Directory -Force $ffmpegDst | Out-Null
    Copy-Item $ffmpegExe, $ffprobeExe -Destination $ffmpegDst -Force
    Write-Host "  bundled ffmpeg.exe, ffprobe.exe -> ffmpeg\"
}
else {
    Write-Warning "ffmpeg.exe/ffprobe.exe not found under tools/ffmpeg. See tools/ffmpeg/README.md, place them, and re-run."
}

Write-Host "`n-- Bundling license notices --" -ForegroundColor Cyan
$licenseSrc = Join-Path $repoRoot "docs\licenses\THIRD-PARTY-NOTICES.md"
if (Test-Path $licenseSrc) {
    Copy-Item $licenseSrc -Destination $outDir -Force
    Write-Host "  bundled THIRD-PARTY-NOTICES.md"
}

$sizeBytes = (Get-ChildItem $outDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMb = [math]::Round($sizeBytes / 1MB, 1)
Write-Host "`nOutput size: ${sizeMb}MB ($outDir)" -ForegroundColor Green

if (-not $SkipZip) {
    Write-Host "`n-- Creating zip --" -ForegroundColor Cyan
    Stop-RunningOutput # in case the exe was launched (e.g. for testing) while this script was running
    $zipPath = Join-Path $repoRoot "dist\YomiagePlayer-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    try {
        Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -ErrorAction Stop
        $zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
        Write-Host "Created: $zipPath (${zipMb}MB)" -ForegroundColor Green
    }
    catch {
        Write-Warning "Zip creation failed (a file may still be locked by a running process): $($_.Exception.Message)"
        Write-Warning "The unzipped build is still available at: $outDir"
    }
}

Write-Host "`nDone. The Whisper model is not bundled - download it from the Settings window on first run." -ForegroundColor Yellow
