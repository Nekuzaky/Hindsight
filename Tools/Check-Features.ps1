<#
.SYNOPSIS
    Checks Resume here, saving and opening a recording end to end in real Play Mode.
.DESCRIPTION
    Builds a small scene with a Rigidbody and a Rigidbody2D, plays it, rewinds, resumes from the past frame and
    checks the restored velocities, saves the recording, leaves Play Mode, opens the file, rewinds the scene and
    checks that the scene is never marked as modified. Writes Tools/Output/feature-check/report.txt.
.EXAMPLE
    ./Tools/Check-Features.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$report = Join-Path (Get-OutputDirectory 'feature-check') 'report.txt'
$logFile = Join-Path (Get-OutputDirectory 'logs') 'feature-check.log'
Remove-Item $report -ErrorAction SilentlyContinue

Write-Host "Checking features with Unity $(Get-UnityVersion)..."
$exitCode = Invoke-Unity -Arguments @('-executeMethod', 'Nekuzaky.Hindsight.Dev.FeatureCheck.Run') -LogFile $logFile

if (-not (Test-Path $report)) {
    Get-CompilerMessages -LogFile $logFile | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "No report was written (exit code $exitCode). Log: $logFile" -ForegroundColor Red
    exit 1
}
Get-Content $report | ForEach-Object { Write-Host $_ -ForegroundColor $(if ($_ -like 'PASS*') { 'Green' } else { 'Red' }) }
exit $exitCode
