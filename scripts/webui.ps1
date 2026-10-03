# Builds and starts the presentation UI, then opens a browser at it.
#
# The UI needs Jev.Core and the hook executable, both of which build offline. It does NOT need
# TenLevels.Jev, which downloads the GitHub Copilot runtime from registry.npmjs.org: build that
# one only when the Copilot-backed levels (6, 7, 10) are going to be run from the browser.
#
# With -Laya, -Decider, and/or -Clef it can also start the local model containers, wait for
# /health, and point the UI at them so live mode is available from the first page load.
[CmdletBinding()]
param(
    # Skip the build. Use when the solution is already built and the demo is about to start.
    [switch]$NoBuild,

    # Do not launch a browser.
    [switch]$NoBrowser,

    # Also build TenLevels.Jev, so the "Run the full agent" button on levels 6, 7 and 10 works
    # without a first-click npm download.
    [switch]$IncludeAgentLevels,

    # Start laya-serve in Docker first and enable live mode for Laya. See docs/LAYA.md.
    [switch]$Laya,

    # Start Mapika Decider in the repository's Docker image. See docs/DECIDER.md.
    [switch]$Decider,

    # Start Decider with NVIDIA GPU access. Implies -Decider.
    [switch]$DeciderGpu,

    # Start Cloudflare Clef in the repository's Docker image. See docs/CLEF.md: it needs ~19 GB
    # for its weights, so this is for a large GPU. Hosted Clef needs no flag, only credentials.
    [switch]$Clef,

    # Start Clef with NVIDIA GPU access. Implies -Clef.
    [switch]$ClefGpu,

    # Stop a UI that is already running on the port, then start this one. Only ever stops a
    # Jev.WebUi process; anything else holding the port is reported and left alone.
    [switch]$Restart
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$port = 5088
$url = "http://127.0.0.1:$port"
if ($DeciderGpu) { $Decider = $true }
if ($ClefGpu) { $Clef = $true }

function Test-PortFree {
    $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
    try { $probe.Start(); return $true }
    catch { return $false }
    finally { $probe.Stop() }
}

function Get-PortOwner {
    $ids = if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique
    }
    elseif (Get-Command lsof -ErrorAction SilentlyContinue) {
        lsof -nP "-iTCP:$port" -sTCP:LISTEN -t 2>$null | Sort-Object -Unique
    }
    foreach ($id in $ids) { Get-Process -Id $id -ErrorAction SilentlyContinue }
}

function Test-IsWebUi($process) {
    # The apphost is named after the project; `dotnet Jev.WebUi.dll` shows up as dotnet instead.
    $process.ProcessName -eq "Jev.WebUi" -or "$($process.CommandLine)" -match "Jev\.WebUi"
}

# Checked first, because an instance that is already running keeps the environment it started
# with: it would not see newly enabled local providers, and this one would only fail to bind after the build and the wait.
if (-not (Test-PortFree)) {
    $owners = @(Get-PortOwner)
    $isOnlyWebUi = $owners.Count -gt 0 -and @($owners | Where-Object { -not (Test-IsWebUi $_) }).Count -eq 0

    if ($Restart -and $isOnlyWebUi) {
        foreach ($owner in $owners) {
            Write-Host "Stopping the running UI (PID $($owner.Id))..."
            Stop-Process -Id $owner.Id -Force
        }

        $deadline = (Get-Date).AddSeconds(10)
        while (-not (Test-PortFree) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
        if (-not (Test-PortFree)) {
            Write-Host "Port $port is still in use after stopping the UI." -ForegroundColor Red
            exit 1
        }
    }
    else {
        Write-Host ""
        Write-Host "Port $port is already in use, so the UI cannot start." -ForegroundColor Red
        Write-Host ""
        foreach ($owner in $owners) {
            $started = if ($owner.StartTime) { "started $($owner.StartTime.ToString('g'))" } else { "" }
            Write-Host "  PID $($owner.Id)  $($owner.ProcessName)  $started"
            if ($owner.Path) { Write-Host "  $($owner.Path)" }
        }
        Write-Host ""

        if ($isOnlyWebUi) {
            Write-Host "That is an earlier demo UI. It keeps the environment it was started with, so it"
            Write-Host "will not pick up -Laya/-Decider/-Clef or changed provider URLs. Stop it and start this one:"
            Write-Host ""
            Write-Host "  ./scripts/webui.ps1 $(@($PSBoundParameters.Keys | ForEach-Object { "-$_" }) + '-Restart' -join ' ')"
            Write-Host ""
            Write-Host "or stop it yourself (or press Ctrl+C in the window it is running in):"
        }
        elseif ($owners.Count -gt 0) {
            Write-Host "That is not the demo UI, so -Restart will not stop it. Free the port yourself:"
        }
        else {
            Write-Host "The owning process could not be identified. On Windows, find it with:"
            Write-Host ""
            Write-Host "  Get-NetTCPConnection -LocalPort $port -State Listen | Select-Object OwningProcess"
            exit 1
        }

        Write-Host ""
        foreach ($owner in $owners) { Write-Host "  Stop-Process -Id $($owner.Id)" }
        Write-Host ""
        exit 1
    }
}

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

if ($Laya) {
    # After the build, so a compile error surfaces before a potentially long cold-start wait.
    # laya-up leaves an already running container alone, so this is safe to repeat.
    & (Join-Path $PSScriptRoot "laya-up.ps1")
    if ($LASTEXITCODE -ne 0) { throw "laya-serve did not start." }

    # The UI offers live mode only when this is set in its own environment. Same defaults as
    # laya-up, which is the service that was just confirmed healthy.
    $layaBind = if ($env:LAYA_BIND_ADDRESS) { $env:LAYA_BIND_ADDRESS } else { "127.0.0.1" }
    $layaPort = if ($env:LAYA_HOST_PORT) { $env:LAYA_HOST_PORT } else { "8010" }
    $env:LAYA_BASE_URL = "http://${layaBind}:${layaPort}"
}

if ($Decider) {
    $deciderArgs = @()
    if ($DeciderGpu) { $deciderArgs += "-Gpu" }
    & (Join-Path $PSScriptRoot "decider-up.ps1") @deciderArgs
    if ($LASTEXITCODE -ne 0) { throw "decider.serve did not start." }

    $deciderPort = if ($env:DECIDER_HOST_PORT) { $env:DECIDER_HOST_PORT } else { "8011" }
    $env:DECIDER_BASE_URL = "http://127.0.0.1:${deciderPort}"
}

if ($Clef) {
    $clefArgs = @()
    if ($ClefGpu) { $clefArgs += "-Gpu" }
    & (Join-Path $PSScriptRoot "clef-up.ps1") @clefArgs
    if ($LASTEXITCODE -ne 0) { throw "The Clef container did not start." }

    $clefPort = if ($env:CLEF_HOST_PORT) { $env:CLEF_HOST_PORT } else { "8012" }
    $env:CLEF_BASE_URL = "http://127.0.0.1:${clefPort}"
}

if (-not $NoBrowser) {
    # Started before the server blocks, and deliberately not waited on: the page retries its own
    # API calls, so a browser that arrives a second early simply loads a moment later.
    Start-Process $url | Out-Null
}

Write-Host ""
Write-Host "Presentation UI: $url"
if ($Laya) { Write-Host "Laya live mode is available: requests go to $env:LAYA_BASE_URL." }
if ($Decider) { Write-Host "Decider live mode is available: requests go to $env:DECIDER_BASE_URL." }
if ($Clef) { Write-Host "Clef live mode is available: requests go to $env:CLEF_BASE_URL." }
if (-not $Laya -and -not $Decider -and -not $Clef) {
    Write-Host "Provider defaults to Laya in mock mode, which needs no API key and no network."
    Write-Host "Pass -Laya, -Decider, and/or -Clef (optionally -DeciderGpu/-ClefGpu) to enable local live providers."
}
Write-Host "Press Ctrl+C to stop."
Write-Host ""

$runArguments = @("run", "--project", (Join-Path $repository "src/Jev.WebUi/Jev.WebUi.csproj"))
if (-not $NoBuild) {
    # Already built above; skipping the implicit rebuild also avoids a file lock when the UI is
    # restarted while a previous instance is still shutting down.
    $runArguments += "--no-build"
}

dotnet @runArguments
