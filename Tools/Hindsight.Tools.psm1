# Shared helpers for the scripts in this folder. Windows PowerShell 5.1 or PowerShell 7.

Set-StrictMode -Version Latest

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:OutputRoot = Join-Path $script:RepoRoot 'Tools\Output'

function Get-RepoRoot { $script:RepoRoot }

function Get-OutputDirectory {
    param([Parameter(Mandatory)][string] $Name)
    $path = Join-Path $script:OutputRoot $Name
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    $path
}

function Get-UnityVersion {
    $versionFile = Join-Path $script:RepoRoot 'ProjectSettings\ProjectVersion.txt'
    $line = Get-Content $versionFile | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    if (-not $line) { throw "Cannot read the editor version from $versionFile" }
    ($line -split ':', 2)[1].Trim()
}

function Get-UnityPath {
    if ($env:UNITY_EDITOR -and (Test-Path $env:UNITY_EDITOR)) { return $env:UNITY_EDITOR }
    $version = Get-UnityVersion
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "D:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "$env:ProgramFiles\Unity $version\Editor\Unity.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "Unity $version was not found. Install it from Unity Hub or set UNITY_EDITOR to the path of Unity.exe."
}

function Test-ProjectOpen {
    $lockFile = Join-Path $script:RepoRoot 'Temp\UnityLockfile'
    if (-not (Test-Path $lockFile)) { return $false }
    try {
        $stream = [System.IO.File]::Open($lockFile, 'Open', 'ReadWrite', 'None')
        $stream.Close()
        return $false
    } catch {
        return $true
    }
}

function Invoke-Unity {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $LogFile,
        [switch] $Interactive
    )
    if (Test-ProjectOpen) { throw 'The project is open in the Unity Editor. Close it first: Unity cannot open one project twice.' }

    $unity = Get-UnityPath
    $all = @('-projectPath', "`"$script:RepoRoot`"", '-logFile', "`"$LogFile`"")
    if (-not $Interactive) { $all = @('-batchmode') + $all }
    $all += $Arguments

    # Start-Process -Wait would also wait for the helper processes Unity leaves running, so wait on Unity only.
    $process = Start-Process -FilePath $unity -ArgumentList $all -PassThru -WindowStyle Hidden
    $process.WaitForExit()
    $process.ExitCode
}

function Get-CompilerMessages {
    param([Parameter(Mandatory)][string] $LogFile)
    if (-not (Test-Path $LogFile)) { return @() }
    Get-Content $LogFile |
        Where-Object { $_ -match '\b(error|warning) CS\d{4}\b' } |
        Sort-Object -Unique
}

Export-ModuleMember -Function Get-RepoRoot, Get-OutputDirectory, Get-UnityVersion, Get-UnityPath, Test-ProjectOpen, Invoke-Unity, Get-CompilerMessages
