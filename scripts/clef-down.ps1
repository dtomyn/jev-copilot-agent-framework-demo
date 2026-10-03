# Stops the repository's Clef Docker service. Model weights are retained unless -RemoveVolumes.
[CmdletBinding()]
param([switch]$RemoveVolumes)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $repository "docker/clef/compose.yml"
$composeArgs = @("compose", "-p", "jev-clef", "-f", $compose, "down")
if ($RemoveVolumes) { $composeArgs += "--volumes" }
& docker @composeArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Clef stopped.$(if ($RemoveVolumes) { ' The cached model volume was removed.' } else { ' Model weights remain cached.' })"
