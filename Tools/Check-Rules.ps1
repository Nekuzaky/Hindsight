<#
.SYNOPSIS
    Checks the code rules Hindsight follows (adapted from NASA's Power of 10) on the package and dev sources.
.DESCRIPTION
    - Functions are at most 60 lines long.
    - No goto, no unsafe code, no direct recursion, no warning silenced with #pragma.
    - Development assemblies (under Assets) build with warnings as errors (csc.rsp with -warnaserror+).
    - The package never forces warnings as errors: a deprecation in a future Unity version must not stop someone
      else's project from compiling. Zero warnings in the package is enforced by Tools/Compile.ps1 instead.
    It is a lightweight text check, not a C# parser: it reads brace-delimited bodies after method signatures.
.EXAMPLE
    ./Tools/Check-Rules.ps1
#>
[CmdletBinding()]
param([int] $MaxLines = 60)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$root = Get-RepoRoot
$folders = @('Packages\com.nekuzaky.hindsight', 'Assets\Demo', 'Assets\Dev') | ForEach-Object { Join-Path $root $_ }
$files = $folders | ForEach-Object { Get-ChildItem $_ -Recurse -Filter '*.cs' } | Where-Object { $_.FullName -notmatch '\\Samples~\\' }
$signature = '^\s*(?:\[[^\]]+\]\s*)*(?:(?:public|private|internal|protected|static|override|virtual|sealed|async|extern|new|readonly)\s+)*[\w<>\[\],.?]+\s+(?<name>\w+)\s*(?:<[^>]+>)?\s*\((?<args>.*)\)?\s*(?:where\s.+)?$'
$controlWords = 'if|for|foreach|while|switch|catch|using|lock|else|return|new|fixed'
$problems = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $lines = Get-Content $file.FullName
    $relative = $file.FullName.Substring($root.Length + 1)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -cmatch '\bgoto\b') { $problems.Add("${relative}:$($i + 1) uses goto") }
        if ($line -cmatch '\bunsafe\b' -and $line -notmatch '^\s*//') { $problems.Add("${relative}:$($i + 1) uses unsafe code") }
        if ($line -match '#pragma\s+warning\s+disable') { $problems.Add("${relative}:$($i + 1) silences a warning") }

        # A signature may wrap over a few lines: join them until the parameter list closes.
        $start = $i
        $header = $line
        for ($k = 0; $k -lt 4 -and $header.TrimEnd().EndsWith(',') -and $i + 1 -lt $lines.Count; $k++) {
            $i++
            $header += ' ' + $lines[$i].Trim()
        }

        if ($header -notmatch $signature -or $header -match "^\s*($controlWords)\b" -or $header.TrimEnd().EndsWith(';')) { $i = $start; continue }
        $name = $Matches['name']
        if ($i + 1 -ge $lines.Count -or $lines[$i + 1].Trim() -ne '{') { $i = $start; continue }

        # Walk the body until its closing brace.
        $depth = 0
        $end = $i + 1
        for ($j = $i + 1; $j -lt $lines.Count; $j++) {
            $depth += ([regex]::Matches($lines[$j], '\{')).Count - ([regex]::Matches($lines[$j], '\}')).Count
            if ($depth -le 0) { $end = $j; break }
        }

        $length = $end - $start + 1
        if ($length -gt $MaxLines) { $problems.Add("${relative}:$($start + 1) $name is $length lines long (max $MaxLines)") }

        $body = ($lines[($i + 2)..($end - 1)] -join "`n")
        if ($body -match "(?<!new\s)(?<![\w.])$name\s*\(" -and $name -notmatch '^(Add|Set|Get|Show|Open|Clear)$') {
            $problems.Add("${relative}:$($i + 1) $name may call itself (recursion)")
        }
        $i = $end
    }
}

Get-ChildItem $folders -Recurse -Filter '*.asmdef' | Where-Object { $_.FullName -notmatch '\\Samples~\\' } | ForEach-Object {
    $asmdef = $_.FullName.Substring($root.Length + 1)
    $rsp = Join-Path $_.DirectoryName 'csc.rsp'
    $strict = (Test-Path $rsp) -and (Select-String -Path $rsp -Pattern 'warnaserror\+' -Quiet)
    if ($asmdef.StartsWith('Packages\') -and $strict) {
        $problems.Add("$asmdef ships -warnaserror+: a deprecation in a future Unity version would break users' projects")
    } elseif ($asmdef.StartsWith('Assets\') -and -not $strict) {
        $problems.Add("$asmdef has no csc.rsp with -warnaserror+")
    }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "$($problems.Count) problem(s) in $($files.Count) files." -ForegroundColor Red
    exit 1
}
Write-Host "Rules respected in $($files.Count) files: functions <= $MaxLines lines, no recursion, no goto, no unsafe, no silenced warnings." -ForegroundColor Green
