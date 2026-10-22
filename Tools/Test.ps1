<#
.SYNOPSIS
    Runs Hindsight's EditMode tests in batch mode and prints a summary.
.PARAMETER Filter
    Optional NUnit name filter, for example "Nekuzaky.Hindsight.Tests.HistoryTests".
.EXAMPLE
    ./Tools/Test.ps1
    ./Tools/Test.ps1 -Filter RecorderTests
#>
[CmdletBinding()]
param([string] $Filter)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$outputDirectory = Get-OutputDirectory 'tests'
$results = Join-Path $outputDirectory 'results.xml'
$logFile = Join-Path (Get-OutputDirectory 'logs') 'tests.log'
Remove-Item $results -ErrorAction SilentlyContinue

$arguments = @('-runTests', '-testPlatform', 'EditMode', '-testResults', "`"$results`"", '-assemblyNames', 'Nekuzaky.Hindsight.Tests')
if ($Filter) { $arguments += @('-testFilter', $Filter) }

Write-Host "Running EditMode tests with Unity $(Get-UnityVersion)..."
$exitCode = Invoke-Unity -Arguments $arguments -LogFile $logFile

$messages = @(Get-CompilerMessages -LogFile $logFile)
if ($messages.Count -gt 0) {
    $messages | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}
if (-not (Test-Path $results)) {
    Write-Host "No test results were written (exit code $exitCode). Log: $logFile" -ForegroundColor Red
    exit 1
}

[xml] $xml = Get-Content $results
$run = $xml.'test-run'
$failures = @($xml.SelectNodes("//test-case[@result='Failed']"))
foreach ($failure in $failures) {
    Write-Host "FAILED $($failure.fullname)" -ForegroundColor Red
    Write-Host ("    " + ($failure.failure.message.'#cdata-section' -replace "`n", "`n    ")) -ForegroundColor DarkRed
}

$invariant = [Globalization.CultureInfo]::InvariantCulture
# Unity writes the duration with the machine's decimal separator.
$seconds = [double]::Parse(($run.duration -replace ',', '.'), $invariant)
$color = if ([int]$run.failed -eq 0) { 'Green' } else { 'Red' }
$summary = [string]::Format($invariant, '{0} tests: {1} passed, {2} failed, {3} skipped ({4:0.0} s)', $run.total, $run.passed, $run.failed, $run.skipped, $seconds)
Write-Host $summary -ForegroundColor $color
exit ([int]$run.failed)
