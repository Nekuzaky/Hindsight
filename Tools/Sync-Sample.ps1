<#
.SYNOPSIS
    Copies the demo from Assets/Demo into the package sample (Samples~/Demo), meta files included.
.DESCRIPTION
    The demo is developed in Assets/Demo so it can be opened in this project. Samples~ is hidden from Unity, so run
    this script after changing the demo. csc.rsp is left out: the sample must not turn warnings into errors in
    other people's projects.
.EXAMPLE
    ./Tools/Sync-Sample.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$root = Get-RepoRoot
$source = Join-Path $root 'Assets\Demo'
$target = Join-Path $root 'Packages\com.nekuzaky.hindsight\Samples~\Demo'

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null

Get-ChildItem $source -Recurse -File | Where-Object { $_.Name -ne 'csc.rsp' -and $_.Name -ne 'csc.rsp.meta' } | ForEach-Object {
    $relative = $_.FullName.Substring($source.Length + 1)
    $destination = Join-Path $target $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item $_.FullName $destination
}

$count = @(Get-ChildItem $target -Recurse -File).Count
Write-Host "Sample synced: $count files in $target" -ForegroundColor Green
