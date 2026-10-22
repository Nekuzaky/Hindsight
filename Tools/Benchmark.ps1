<#
.SYNOPSIS
    Measures what Hindsight costs in real Play Mode and writes Tools/Output/benchmark/benchmark.md.
.DESCRIPTION
    Runs five scenarios (static scene, moving objects, physics bodies), each with recording off and on,
    and reports Hindsight's main-thread time per frame, total frame time, memory, garbage, seek and graph times.
.EXAMPLE
    ./Tools/Benchmark.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$output = Get-OutputDirectory 'benchmark'
$report = Join-Path $output 'benchmark.md'
$logFile = Join-Path (Get-OutputDirectory 'logs') 'benchmark.log'
Remove-Item $report -ErrorAction SilentlyContinue

Write-Host "Benchmarking with Unity $(Get-UnityVersion). This takes a few minutes..."
$exitCode = Invoke-Unity -Arguments @('-executeMethod', 'Nekuzaky.Hindsight.Dev.Benchmark.Run') -LogFile $logFile

if (-not (Test-Path $report)) {
    Get-CompilerMessages -LogFile $logFile | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "No report was written (exit code $exitCode). Log: $logFile" -ForegroundColor Red
    exit 1
}
Get-Content $report | Write-Host
