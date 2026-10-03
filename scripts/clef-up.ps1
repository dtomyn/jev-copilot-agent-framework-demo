# Builds/starts Cloudflare Clef in Docker and waits for its HTTP health endpoint.
#
# Clef-Flash is 9B parameters in BF16: about 19 GB for the weights alone. -Gpu needs an NVIDIA GPU
# with at least 24 GB of memory, Docker Desktop's WSL2 backend, and a driver that exposes the GPU
# to Linux containers. Without -Gpu it runs on CPU, which needs Docker/WSL allowed ~24 GB of RAM
# and answers in seconds rather than milliseconds: too slow for the hook's deadline. On a smaller
# machine, use the hosted Workers AI endpoint instead (see docs/CLEF.md); it needs no container.
[CmdletBinding()]
param(
    [switch]$Gpu,
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $repository "docker/clef/compose.yml"
$gpuCompose = Join-Path $repository "docker/clef/compose.gpu.yml"
$port = if ($env:CLEF_HOST_PORT) { $env:CLEF_HOST_PORT } else { "8012" }
$baseUrl = "http://127.0.0.1:$port"
$model = if ($env:CLEF_MODEL) { $env:CLEF_MODEL } else { "clef-flash" }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "docker was not found. Install Docker Desktop, or use the hosted Workers AI endpoint (see docs/CLEF.md)."
}

$composeArgs = @("compose", "-p", "jev-clef", "-f", $compose)
if ($Gpu) {
    $composeArgs += @("-f", $gpuCompose)
}
else {
    Write-Host "Starting Clef on CPU. Expect ~24 GB of container RAM and multi-second answers; pass -Gpu on a 24 GB+ NVIDIA GPU." -ForegroundColor Yellow
}

Write-Host "Starting Clef ($model) on $baseUrl ..."
Write-Host "The first start downloads ~19 GB of weights into the Docker volume 'jev-clef_clef-model-cache'."

if ($Rebuild) {
    Write-Host "Forcing a clean Clef image rebuild..."
    & docker @composeArgs build --no-cache
    if ($LASTEXITCODE -ne 0) { throw "docker compose failed to rebuild Clef." }
}

# Always ask Compose to build: Docker's layer cache makes the no-change case cheap, and this
# guarantees switching between CPU and GPU profiles cannot accidentally reuse the other image.
$upArgs = @($composeArgs + @("up", "-d", "--build"))
& docker @upArgs
if ($LASTEXITCODE -ne 0) { throw "docker compose failed to start Clef." }

# /health answers 503 while the weights download and load, and 500 if loading failed for good.
$deadline = (Get-Date).AddMinutes(90)
while ((Get-Date) -lt $deadline) {
    try {
        $health = Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 5
        Write-Host "Clef is ready: $($health | ConvertTo-Json -Compress)" -ForegroundColor Green
        Write-Host ""
        Write-Host "For this PowerShell session:"
        Write-Host "  `$env:DECISION_PROVIDER = 'clef'"
        Write-Host "  `$env:CLEF_BASE_URL = '$baseUrl'"
        Write-Host "  dotnet run --project src/TenLevels.Jev -- --level 4 --provider clef --mode live"
        exit 0
    }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 500) {
            break
        }

        Start-Sleep -Seconds 5
    }
}

Write-Host "Clef did not become healthy. Recent container logs:" -ForegroundColor Red
& docker @composeArgs logs --tail 80 clef
exit 1
