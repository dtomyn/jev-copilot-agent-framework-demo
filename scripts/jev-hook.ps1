# GitHub Copilot CLI preToolUse hook wrapper (Windows/PowerShell).
# Always exits 0 with exactly one decision object: a non-zero exit would deny every tool call,
# and no output at all would be treated as a hook error.
$ErrorActionPreference = "Stop"

$tfm = "net10.0"
$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $root "src/Jev.CopilotHook/bin/Release/$tfm/Jev.CopilotHook.dll"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Output '{"permissionDecision":"ask","permissionDecisionReason":"The policy hook requires the .NET 10 runtime; require human approval."}'
    exit 0
}

if (-not (Test-Path $dll)) {
    Write-Output '{"permissionDecision":"ask","permissionDecisionReason":"The policy hook is not built. Run ./scripts/bootstrap.ps1; require human approval until then."}'
    exit 0
}

# PowerShell does not forward its own pipeline input to a child native process, so read the
# payload explicitly and pipe it in. $MyInvocation.ExpectingInput distinguishes `... | hook.ps1`
# (test harness) from Copilot invoking the script with a real stdin handle.
$payload = if ($MyInvocation.ExpectingInput) { @($input) -join "`n" } else { [Console]::In.ReadToEnd() }

$payload | & dotnet $dll preToolUse
exit 0
