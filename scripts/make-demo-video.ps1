# Records the interactive demo UI against live TypeSafe Jev and encodes artifacts/jev-demo.mp4,
# small enough (under 10 MB) to attach to a GitHub README. The video is generated, never committed.
#
# Needs: TYPESAFE_API_KEY, .NET 10, Python 3.10+, and an authenticated GitHub Copilot CLI (Levels 7
# and 10 run the real agent). Takes about five minutes, most of it the two agent runs.
[CmdletBinding()]
param(
    # Where the finished video is written.
    [string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts/jev-demo.mp4")
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$tooling = Join-Path $PSScriptRoot "demo-video"
$port = 5088
$url = "http://127.0.0.1:$port"

if (-not $env:TYPESAFE_API_KEY) { throw "Set TYPESAFE_API_KEY to the TypeSafe Jev key first." }

$python = (Get-Command python -ErrorAction SilentlyContinue) ?? (Get-Command python3 -ErrorAction SilentlyContinue)
if (-not $python) { throw "Python 3.10+ is required." }

$probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
try { $probe.Start() } catch { throw "Port $port is in use. Stop the running demo UI (or run webui.ps1 -Restart) and try again." } finally { $probe.Stop() }

& $python.Source -m pip install --quiet -r (Join-Path $tooling "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "pip install failed." }
& $python.Source -m playwright install chromium
if ($LASTEXITCODE -ne 0) { throw "playwright install failed." }

# The UI shells out to TenLevels.Jev for the agent levels, so build it up front rather than inside a recording.
dotnet build (Join-Path $repository "src/Jev.WebUi") -c Release --nologo -v m
if ($LASTEXITCODE -ne 0) { throw "Jev.WebUi build failed." }
dotnet build (Join-Path $repository "src/TenLevels.Jev") -c Release --nologo -v m
if ($LASTEXITCODE -ne 0) { throw "TenLevels.Jev build failed." }

# The UI is started WITHOUT the key in its environment: the recording enters it through the Config
# dialog, which is the flow being demonstrated.
$key = $env:TYPESAFE_API_KEY

# Microsoft Decision has no Config dialog, so the UI reads it from its environment. Take only those
# names from the repository's .env (the shell wins when both are set) and hand them to the UI process.
$microsoft = @{}
$dotenv = Join-Path $repository ".env"
if (Test-Path $dotenv) {
    foreach ($line in Get-Content $dotenv) {
        if ($line -match '^\s*(MICROSOFT_DECISION_[A-Z_]+)\s*=\s*(.*?)\s*$') {
            $microsoft[$Matches[1]] = $Matches[2].Trim('"', "'")
        }
    }
}
foreach ($name in @($microsoft.Keys)) {
    $fromShell = [Environment]::GetEnvironmentVariable($name)
    if ($fromShell) { $microsoft[$name] = $fromShell }
}
$microsoftLive = $microsoft["MICROSOFT_DECISION_ENDPOINT"] -and $microsoft["MICROSOFT_DECISION_DEPLOYMENT"] -and
    ($microsoft["MICROSOFT_DECISION_API_KEY"] -or $microsoft["MICROSOFT_DECISION_ACCESS_TOKEN"])
if (-not $microsoftLive) { Write-Warning "MICROSOFT_DECISION_* is incomplete (.env or shell): the Microsoft segment is skipped." }
$frames = Join-Path ([System.IO.Path]::GetTempPath()) "jev-demo-frames-$PID"
$ui = $null
try {
    Remove-Item Env:TYPESAFE_API_KEY
    $env:ASPNETCORE_URLS = $url
    if ($microsoftLive) { foreach ($name in $microsoft.Keys) { Set-Item "Env:$name" $microsoft[$name] } }
    $ui = Start-Process dotnet -ArgumentList @("run", "--project", (Join-Path $repository "src/Jev.WebUi"), "-c", "Release", "--no-build") `
        -WorkingDirectory $repository -PassThru -WindowStyle Hidden
    $env:TYPESAFE_API_KEY = $key
    Remove-Item Env:ASPNETCORE_URLS
    foreach ($name in $microsoft.Keys) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }

    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        try { $ready = (Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep 1 }
    }
    if (-not $ready) { throw "The demo UI did not come up on $url." }

    $env:DEMO_URL = $url
    $env:DEMO_MICROSOFT = if ($microsoftLive) { "1" } else { "0" }
    & $python.Source (Join-Path $tooling "record.py") $frames
    if ($LASTEXITCODE -ne 0) { throw "Recording failed." }
    & $python.Source (Join-Path $tooling "assemble.py") $frames $Output
    if ($LASTEXITCODE -ne 0) { throw "Encoding failed." }
}
finally {
    # Stopping the UI also discards the key it held in memory.
    if ($ui -and -not $ui.HasExited) { Stop-Process -Id $ui.Id -Force }
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    if (Test-Path $frames) { Remove-Item $frames -Recurse -Force }
}

Write-Host "Wrote $Output"
