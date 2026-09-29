# Stops the local Laya decision service.
#
# The model cache lives in a named volume, so stopping and starting again is cheap: only
# -RemoveVolumes forces the next start to download the checkpoints over again.
[CmdletBinding()]
param(
    [string]$LayaHome = $(if ($env:LAYA_HOME) { $env:LAYA_HOME } else { "C:/laya-local" }),
    [switch]$RemoveVolumes
)

$ErrorActionPreference = "Stop"

$compose = Join-Path $LayaHome "docker-compose.yml"
if (-not (Test-Path $compose)) {
    throw "No docker-compose.yml under '$LayaHome'. Set LAYA_HOME or pass -LayaHome."
}

if ($RemoveVolumes) {
    Write-Warning "Removing the model cache volume. The next start re-downloads the checkpoints."
    docker compose --file $compose down --volumes
}
else {
    docker compose --file $compose down
}

if ($LASTEXITCODE -ne 0) { throw "docker compose down failed." }
Write-Host "laya-serve stopped."
