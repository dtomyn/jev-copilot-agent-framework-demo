# Builds and starts the presentation UI, then opens a browser at it.
#
# The UI needs Jev.Core and the hook executable, both of which build offline. It does NOT need
# TenLevels.Jev, which downloads the GitHub Copilot runtime from registry.npmjs.org: build that
# one only when the Copilot-backed levels (6, 7, 10) are going to be run from the browser.
[CmdletBinding()]
param(
    # Skip the build. Use when the solution is already built and the demo is about to start.
    [switch]$NoBuild,

    # Do not launch a browser.
    [switch]$NoBrowser,

    # Also build TenLevels.Jev, so the "Run the full agent" button on levels 6, 7 and 10 works
    # without a first-click npm download.
    [switch]$IncludeAgentLevels
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$url = "http://127.0.0.1:5088"

if (-not $NoBuild) {
    Write-Host "Building the UI and the hook..."
    dotnet build (Join-Path $repository "src/Jev.WebUi/Jev.WebUi.csproj") --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "The build failed." }

    if ($IncludeAgentLevels) {
        Write-Host "Building TenLevels.Jev (this downloads the Copilot CLI from npm on a cold cache)..."
        dotnet build (Join-Path $repository "src/TenLevels.Jev/TenLevels.Jev.csproj") --nologo --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "TenLevels.Jev failed to build. Levels 6, 7 and 10 will not run from the browser." }
    }
}

if (-not $NoBrowser) {
    # Started before the server blocks, and deliberately not waited on: the page retries its own
    # API calls, so a browser that arrives a second early simply loads a moment later.
    Start-Process $url | Out-Null
}

Write-Host ""
Write-Host "Presentation UI: $url"
Write-Host "Provider defaults to Laya in mock mode, which needs no API key and no network."
Write-Host "Press Ctrl+C to stop."
Write-Host ""

$runArguments = @("run", "--project", (Join-Path $repository "src/Jev.WebUi/Jev.WebUi.csproj"))
if (-not $NoBuild) {
    # Already built above; skipping the implicit rebuild also avoids a file lock when the UI is
    # restarted while a previous instance is still shutting down.
    $runArguments += "--no-build"
}

dotnet @runArguments
