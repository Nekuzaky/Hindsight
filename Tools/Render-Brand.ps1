<#
.SYNOPSIS
    Renders the banner, the GitHub social preview and the editor icons from the HTML sources in docs/brand.
.DESCRIPTION
    Starts a small local web server, then opens docs/brand/render.html once per image in your default browser. The
    page draws the source through an SVG foreignObject onto a canvas and posts the PNG back to the server, which
    writes it to an allow-listed path. Headless browsers are not used: they hang on some machines.
    Pass -NoBrowser to print the URLs instead, for example to open them in another browser.
.EXAMPLE
    ./Tools/Render-Brand.ps1
#>
[CmdletBinding()]
param([int] $Port = 8765, [switch] $NoBrowser, [int] $TimeoutSeconds = 60)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Hindsight.Tools.psm1') -Force

$root = Get-RepoRoot
$jobs = @(
    @{ Page = 'banner.html'; Width = 1280; Height = 400; Out = 'docs/images/banner.png' },
    @{ Page = 'social-preview.html'; Width = 1280; Height = 640; Out = 'docs/images/social-preview.png' },
    @{ Page = 'icon.html'; Width = 32; Height = 32; Color = 'd4d8e2'; Out = 'Packages/com.nekuzaky.hindsight/Editor/UI/hindsight-window-dark.png' },
    @{ Page = 'icon.html'; Width = 32; Height = 32; Color = '4a4f5c'; Out = 'Packages/com.nekuzaky.hindsight/Editor/UI/hindsight-window-light.png' }
)
$allowed = $jobs | ForEach-Object { $_.Out }
$types = @{ '.html' = 'text/html; charset=utf-8'; '.svg' = 'image/svg+xml'; '.png' = 'image/png'; '.css' = 'text/css' }

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()

function Send([System.Net.HttpListenerResponse] $response, [int] $status, [byte[]] $body, [string] $type) {
    $response.StatusCode = $status
    if ($type) { $response.ContentType = $type }
    $response.OutputStream.Write($body, 0, $body.Length)
    $response.Close()
}

# Serves files under docs/brand and accepts one upload per allow-listed path. Returns the saved path, if any.
function Handle([System.Net.HttpListenerContext] $context) {
    $request = $context.Request
    $path = [Uri]::UnescapeDataString($request.Url.AbsolutePath.TrimStart('/'))
    if ($request.HttpMethod -eq 'POST') {
        $out = $request.QueryString['out']
        if ($allowed -notcontains $out) { Send $context.Response 403 ([byte[]]@()) $null; return $null }
        $reader = New-Object System.IO.StreamReader($request.InputStream)
        $data = $reader.ReadToEnd().Split(',', 2)[1]
        [System.IO.File]::WriteAllBytes((Join-Path $root $out), [Convert]::FromBase64String($data))
        Send $context.Response 200 ([Text.Encoding]::UTF8.GetBytes('saved')) 'text/plain'
        return $out
    }
    $file = Join-Path $root $path
    if (-not $path.StartsWith('docs/brand/') -or -not (Test-Path $file -PathType Leaf)) {
        Send $context.Response 404 ([byte[]]@()) $null
        return $null
    }
    Send $context.Response 200 ([System.IO.File]::ReadAllBytes($file)) $types[[System.IO.Path]::GetExtension($file)]
    return $null
}

try {
    foreach ($job in $jobs) {
        $query = "page=$($job.Page)&w=$($job.Width)&h=$($job.Height)&out=$([Uri]::EscapeDataString($job.Out))"
        if ($job.Color) { $query += "&color=$($job.Color)" }
        $url = "http://127.0.0.1:$Port/docs/brand/render.html?$query"
        if ($NoBrowser) { Write-Host "Open: $url" } else { Start-Process $url }

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $saved = $null
        while (-not $saved -and (Get-Date) -lt $deadline) {
            $pending = $listener.GetContextAsync()
            while (-not $pending.Wait(250)) {
                if ((Get-Date) -ge $deadline) { break }
            }
            if ($pending.IsCompleted) { $saved = Handle $pending.Result }
        }
        if ($saved) { Write-Host "  $saved" -ForegroundColor Green } else { throw "Timed out waiting for $($job.Out)." }
    }
} finally {
    $listener.Stop()
    $listener.Close()
}
