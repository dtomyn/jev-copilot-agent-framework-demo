# Starts the local Laya decision service (laya-serve in Docker) and waits until it answers.
#
# The compose project lives outside this repository, because Laya is an upstream checkout rather
# than a vendored dependency. Point LAYA_HOME at it; the default matches the layout described in
# docs/LAYA.md.
#
# First boot downloads a checkpoint into a named volume and can take several minutes. The
# container is only healthy once /health answers, so this script waits rather than returning a
# base URL that is not serving yet.
[CmdletBinding()]
param(
    # Directory holding docker-compose.yml and the laya/ checkout.
    [string]$LayaHome = $(if ($env:LAYA_HOME) { $env:LAYA_HOME } else { "C:/laya-local" }),

    # How long to wait for /health. Generous, because a cold start builds the checkpoints.
    [int]$TimeoutMinutes = 20
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "docker was not found. Install Docker Desktop, or run laya-serve directly (see docs/LAYA.md)."
}

$compose = Join-Path $LayaHome "docker-compose.yml"
if (-not (Test-Path $compose)) {
    throw "No docker-compose.yml under '$LayaHome'. Set LAYA_HOME or pass -LayaHome."
}

$bind = if ($env:LAYA_BIND_ADDRESS) { $env:LAYA_BIND_ADDRESS } else { "127.0.0.1" }
$port = if ($env:LAYA_HOST_PORT) { $env:LAYA_HOST_PORT } else { "8010" }
$baseUrl = "http://${bind}:${port}"

Write-Host "Starting laya-serve from $compose ..."
docker compose --file $compose up --detach --build
if ($LASTEXITCODE -ne 0) { throw "docker compose up failed." }

Write-Host "Waiting for $baseUrl/health (up to $TimeoutMinutes minute(s); a cold start downloads the checkpoint)..."
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    try {
        $health = Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 5
        if ($health.status -eq "ok") {
            Write-Host ""
            Write-Host "laya-serve is ready."
            Write-Host "  loaded checkpoints: $($health.loaded -join ', ')"
            Write-Host "  device:             $($health.device)"
            Write-Host ""
            Write-Host "Point the demo at it:"
            Write-Host "  `$env:DECISION_PROVIDER = 'laya'"
            Write-Host "  `$env:LAYA_BASE_URL = '$baseUrl'"
            Write-Host "  dotnet run --project src/TenLevels.Jev -- --level 4 --provider laya --mode live"
            exit 0
        }
    }
    catch {
        # Not up yet. The container publishes the port only after preloading, so a refused
        # connection here is the normal case and not worth reporting on every attempt.
        Write-Host "." -NoNewline
    }

    Start-Sleep -Seconds 5
}

Write-Host ""
throw "laya-serve did not answer within $TimeoutMinutes minute(s). Check: docker compose --file $compose logs --tail 50"
