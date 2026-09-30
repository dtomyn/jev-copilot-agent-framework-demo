$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet build src/Jev.CopilotHook/Jev.CopilotHook.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# The harness runs in the caller's shell, so the provider it selects must not outlive it.
$previousMode = $env:DECISION_MODE
$previousProvider = $env:DECISION_PROVIDER

$status = 0
try {
    $env:DECISION_MODE = "mock"

    # Every sample is replayed against all providers, and `expectedDecision` is shared: the gate
    # thresholds on max(p), so the provider must change the reported reason and nothing else. A
    # sample that only passes on one provider means provider-specific behaviour leaked out of
    # SystemOneEndpoint into policy code.
    foreach ($provider in @("jev", "laya", "decider")) {
        $env:DECISION_PROVIDER = $provider
        foreach ($sample in Get-ChildItem "samples/hooks/*.json") {
            $payload = Get-Content -Raw $sample.FullName
            $expected = ($payload | ConvertFrom-Json).expectedDecision
            $actual = $payload | & "$root/scripts/jev-hook.ps1"
            $decision = ($actual | ConvertFrom-Json).permissionDecision
            if ($expected -and $expected -ne $decision) {
                Write-Error "FAIL  [$provider] $($sample.BaseName): expected $expected, got $decision -> $actual" -ErrorAction Continue
                $status = 1
            }
            else {
                Write-Host "PASS  [$provider] $($sample.BaseName) -> $decision"
            }
        }
    }
}
finally {
    $env:DECISION_MODE = $previousMode
    $env:DECISION_PROVIDER = $previousProvider
}

exit $status
