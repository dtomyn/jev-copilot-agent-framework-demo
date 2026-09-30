# Builds/starts Mapika Decider in Docker and waits for its HTTP health endpoint.
# On Windows 11, -Gpu requires Docker Desktop's WSL2 backend plus an NVIDIA driver that exposes
# the GPU to Linux containers. Without -Gpu the image is CPU-only and works without GPU plumbing.
[CmdletBinding()]
param(
    [switch]$Gpu,
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $repository "docker/decider/compose.yml"
$gpuCompose = Join-Path $repository "docker/decider/compose.gpu.yml"
$port = if ($env:DECIDER_HOST_PORT) { $env:DECIDER_HOST_PORT } else { "8011" }
$baseUrl = "http://127.0.0.1:$port"
$model = if ($env:DECIDER_MODEL) { $env:DECIDER_MODEL } else { "Mapika/decider-4b" }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "docker was not found. Install Docker Desktop, or run decider.serve directly (see docs/DECIDER.md)."
}

$composeArgs = @("compose", "-p", "jev-decider", "-f", $compose)
if ($Gpu) {
    $composeArgs += @("-f", $gpuCompose)
}

Write-Host "Starting Decider ($model) on $baseUrl ..."
Write-Host "The first start downloads the model into the Docker volume 'jev-decider_decider-model-cache'."

if ($Rebuild) {
    Write-Host "Forcing a clean Decider image rebuild..."
    & docker @composeArgs build --no-cache
    if ($LASTEXITCODE -ne 0) { throw "docker compose failed to rebuild Decider." }
}

# Always ask Compose to build: Docker's layer cache makes the no-change case cheap, and this
# guarantees switching between CPU and GPU profiles cannot accidentally reuse the other image.
$upArgs = @($composeArgs + @("up", "-d", "--build"))
& docker @upArgs
if ($LASTEXITCODE -ne 0) { throw "docker compose failed to start Decider." }

$deadline = (Get-Date).AddMinutes(30)
while ((Get-Date) -lt $deadline) {
    try {
        $health = Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 5
        Write-Host "Decider is ready: $($health | ConvertTo-Json -Compress)" -ForegroundColor Green
        Write-Host ""
        Write-Host "For this PowerShell session:"
        Write-Host "  `$env:DECISION_PROVIDER = 'decider'"
        Write-Host "  `$env:DECIDER_BASE_URL = '$baseUrl'"
        Write-Host "  dotnet run --project src/TenLevels.Jev -- --level 4 --provider decider --mode live"
        exit 0
    }
    catch {
        Start-Sleep -Seconds 3
    }
}

Write-Host "Decider did not become healthy. Recent container logs:" -ForegroundColor Red
& docker @composeArgs logs --tail 80 decider
exit 1
