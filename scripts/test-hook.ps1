$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet build src/Jev.CopilotHook/Jev.CopilotHook.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$env:JEV_MODE = "mock"
$status = 0
foreach ($sample in Get-ChildItem "samples/hooks/*.json") {
    $payload = Get-Content -Raw $sample.FullName
    $expected = ($payload | ConvertFrom-Json).expectedDecision
    $actual = $payload | & "$root/scripts/jev-hook.ps1"
    $decision = ($actual | ConvertFrom-Json).permissionDecision
    if ($expected -and $expected -ne $decision) {
        Write-Error "FAIL  $($sample.BaseName): expected $expected, got $decision -> $actual" -ErrorAction Continue
        $status = 1
    }
    else {
        Write-Host "PASS  $($sample.BaseName) -> $decision"
    }
}
exit $status
