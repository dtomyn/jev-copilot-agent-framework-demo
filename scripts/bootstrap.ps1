$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found. Install the .NET 10 SDK first."
}

# Levels 1-5, 8, 9 and the Copilot hook need nothing but the SDK and nuget.org.
$core = @(
    "src/Jev.Core/Jev.Core.csproj",
    "src/Jev.CopilotHook/Jev.CopilotHook.csproj",
    "tests/Jev.Core.SelfTests/Jev.Core.SelfTests.csproj"
)

dotnet restore JevCopilotDemo.sln
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

foreach ($project in $core) {
    dotnet build $project -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $project." }
}

dotnet run --project tests/Jev.Core.SelfTests/Jev.Core.SelfTests.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Self-tests failed." }

& "$root/scripts/test-hook.ps1"
if ($LASTEXITCODE -ne 0) { throw "Hook samples failed." }

# TenLevels pulls GitHub.Copilot.SDK, whose MSBuild targets download the Copilot CLI from
# registry.npmjs.org at build time. On a restricted network that download is the only thing that
# fails, so it is built last and its failure is reported rather than aborting the bootstrap.
Write-Host ""
dotnet build src/TenLevels.Jev/TenLevels.Jev.csproj -c Release --no-restore
if ($LASTEXITCODE -eq 0) {
    Write-Host "Bootstrap complete."
}
else {
    Write-Warning @"
The ten-levels project failed to build. If the error mentions registry.npmjs.org, the
GitHub.Copilot.SDK build targets could not download the Copilot CLI. Either:
  * build with CopilotSkipCliDownload=true and set COPILOT_CLI_PATH to an installed copilot binary, or
  * set CopilotNpmRegistryUrl to a mirror, or
  * set CopilotCliBinaryPath to a pre-downloaded binary.
Levels 1-5, 8, and 9 and the Copilot hook are built and usable regardless.
"@
}

Write-Host ""
Write-Host "Try: dotnet run --project src/TenLevels.Jev -- --list"
Write-Host 'Then: $env:JEV_MODE="mock"; copilot'
