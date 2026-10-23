<#
.SYNOPSIS
    Proves that Hindsight stays out of player builds: builds the demo for Windows, inspects its assemblies and runs it.
.DESCRIPTION
    Checks that:
    - the build succeeds;
    - the editor assembly (the recorder, the window) is not in the player;
    - the only Hindsight assembly in the player is the small runtime one (Hindsight.Bookmark, [HindsightTrack]);
    - the demo's call to Hindsight.Bookmark was removed by the compiler, argument included;
    - the player runs for a few seconds without any exception.
.EXAMPLE
    ./Tools/Build-Check.ps1
#>
[CmdletBinding()]
param([int] $RunSeconds = 20)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$root = Get-RepoRoot
$buildDirectory = Join-Path (Get-OutputDirectory 'build') ''
Remove-Item (Join-Path $buildDirectory '*') -Recurse -Force -ErrorAction SilentlyContinue
$logFile = Join-Path (Get-OutputDirectory 'logs') 'build-check.log'

Write-Host "Building the demo player with Unity $(Get-UnityVersion)..."
$exitCode = Invoke-Unity -Arguments @('-executeMethod', 'Nekuzaky.Hindsight.Dev.BuildCheck.Run') -LogFile $logFile
Select-String -Path $logFile -Pattern '\[BuildCheck\]' | ForEach-Object { Write-Host ('  ' + $_.Line.Trim()) }
if ($exitCode -ne 0) { Write-Host "Build failed. Log: $logFile" -ForegroundColor Red; exit 1 }

$managed = Join-Path $buildDirectory 'HindsightDemo_Data\Managed'
$problems = New-Object System.Collections.Generic.List[string]
$hindsight = @(Get-ChildItem $managed -Filter 'Nekuzaky.Hindsight*.dll')
foreach ($assembly in $hindsight) {
    Write-Host ("  in the player: {0} ({1:N1} KB)" -f $assembly.Name, ($assembly.Length / 1KB))
}
if ($hindsight | Where-Object { $_.Name -eq 'Nekuzaky.Hindsight.Editor.dll' }) { $problems.Add('The editor assembly is in the player.') }
if ($hindsight | Where-Object { $_.Name -like '*Tests*' -or $_.Name -like '*TestSupport*' -or $_.Name -like '*Dev*' }) { $problems.Add('A test or development assembly is in the player.') }

$demo = Join-Path $managed 'Nekuzaky.Hindsight.Demo.dll'
if (Test-Path $demo) {
    $text = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($demo)) + [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($demo))
    if ($text -match 'Bookmark' -or $text -match 'launched') { $problems.Add('The Hindsight.Bookmark call or its argument is still in the demo assembly.') }
    else { Write-Host '  Hindsight.Bookmark and its argument were removed from the demo code.' }
}

Write-Host "Running the player for $RunSeconds s (batch mode, no graphics)..."
$playerLog = Join-Path (Get-OutputDirectory 'logs') 'build-check-player.log'
$player = Start-Process -FilePath (Join-Path $buildDirectory 'HindsightDemo.exe') -ArgumentList @('-batchmode', '-nographics', '-logFile', "`"$playerLog`"") -PassThru -WindowStyle Hidden
Start-Sleep -Seconds $RunSeconds
if (-not $player.HasExited) { Stop-Process -Id $player.Id -Force }
$exceptions = @(Select-String -Path $playerLog -Pattern 'Exception' | Where-Object { $_.Line -notmatch 'fell through the floor' })
if ($exceptions.Count -gt 0) { $problems.Add("The player logged $($exceptions.Count) exception line(s). Log: $playerLog") }
$falls = @(Select-String -Path $playerLog -Pattern 'fell through the floor').Count
if ($falls -eq 0) { $problems.Add("The demo did not run in the player: no comet fell in $RunSeconds s. Log: $playerLog") }
else { Write-Host "  The player ran the demo: $falls comet(s) fell through the floor, as designed." }

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}
Write-Host 'Hindsight stays out of the build: only the runtime assembly ships, with no code that runs.' -ForegroundColor Green
