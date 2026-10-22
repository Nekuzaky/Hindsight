<#
.SYNOPSIS
    Records the README screenshots and the demo GIF from the real Unity Editor.
.DESCRIPTION
    Opens the editor (not in batch mode), plays the demo scene, rewinds to the first comet that falls through the
    floor and saves frames. Keep the editor window visible and do not use the mouse while it runs (about a minute).
    ffmpeg then turns the frames into docs/images/hindsight-demo.gif and docs/images/hindsight-demo.mp4.
.EXAMPLE
    ./Tools/Capture-Media.ps1
#>
[CmdletBinding()]
param([int] $GifWidth = 960)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
if (-not $ffmpeg) { throw 'ffmpeg was not found on PATH. Install it, for example with: winget install Gyan.FFmpeg' }

$frames = Join-Path (Get-OutputDirectory 'media') 'frames'
$images = Join-Path (Get-RepoRoot) 'docs\images'
New-Item -ItemType Directory -Force -Path $images | Out-Null
$logFile = Join-Path (Get-OutputDirectory 'logs') 'capture.log'

Write-Host 'Opening the editor to record the demo. Keep its windows visible until it closes...'
$exitCode = Invoke-Unity -Interactive -Arguments @('-executeMethod', 'Nekuzaky.Hindsight.Dev.MediaCapture.Run') -LogFile $logFile
$count = @(Get-ChildItem $frames -Filter 'frame_*.png' -ErrorAction SilentlyContinue).Count
if ($count -lt 10) {
    Write-Host "Only $count frames were captured (exit code $exitCode). Log: $logFile" -ForegroundColor Red
    exit 1
}

$pattern = Join-Path $frames 'frame_%04d.png'
$gif = Join-Path $images 'hindsight-demo.gif'
$mp4 = Join-Path $images 'hindsight-demo.mp4'
$palette = Join-Path $frames 'palette.png'
$scale = "fps=15,scale=${GifWidth}:-1:flags=lanczos"

& $ffmpeg.Source -y -loglevel error -framerate 15 -i $pattern -vf "$scale,palettegen=stats_mode=diff" $palette
& $ffmpeg.Source -y -loglevel error -framerate 15 -i $pattern -i $palette -lavfi "$scale [x]; [x][1:v] paletteuse=dither=sierra2_4a" $gif
& $ffmpeg.Source -y -loglevel error -framerate 15 -i $pattern -vf 'scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p' -c:v libx264 -crf 20 -movflags +faststart $mp4

Write-Host "$count frames -> $gif ($([math]::Round((Get-Item $gif).Length / 1MB, 1)) MB)" -ForegroundColor Green
Get-ChildItem $images | Format-Table Name, Length
