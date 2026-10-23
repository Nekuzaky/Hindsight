<#
.SYNOPSIS
    Opens the project in batch mode, compiles every assembly and lists compiler errors and warnings.
.DESCRIPTION
    Hindsight must build with zero warnings, so any warning printed here fails the script. Unity only recompiles
    the assemblies that changed; -Full deletes the compiled scripts first so every assembly is rebuilt and checked.
    Exit code 0 means the project compiled cleanly.
.EXAMPLE
    ./Tools/Compile.ps1
    ./Tools/Compile.ps1 -Full
#>
[CmdletBinding()]
param([switch] $Full)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$logFile = Join-Path (Get-OutputDirectory 'logs') 'compile.log'
if ($Full) {
    foreach ($cache in 'Library\ScriptAssemblies', 'Library\Bee') {
        Remove-Item (Join-Path (Get-RepoRoot) $cache) -Recurse -Force -ErrorAction SilentlyContinue
    }
}
Write-Host "Compiling with Unity $(Get-UnityVersion)..."
$exitCode = Invoke-Unity -Arguments @('-quit') -LogFile $logFile
$messages = @(Get-CompilerMessages -LogFile $logFile)

if ($messages.Count -gt 0) {
    $messages | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "$($messages.Count) compiler message(s). Log: $logFile" -ForegroundColor Red
    exit 1
}
if ($exitCode -ne 0) {
    Write-Host "Unity exited with code $exitCode. Log: $logFile" -ForegroundColor Red
    exit $exitCode
}
Write-Host 'Compiled cleanly: 0 errors, 0 warnings.' -ForegroundColor Green
