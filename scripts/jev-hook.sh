#!/usr/bin/env bash
# GitHub Copilot CLI preToolUse hook wrapper (Linux/macOS).
# Always exits 0 with exactly one decision object: a non-zero exit would deny every tool call,
# and no output at all would be treated as a hook error.
set -u

tfm="net10.0"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dll="$root/src/Jev.CopilotHook/bin/Release/$tfm/Jev.CopilotHook.dll"

if ! command -v dotnet >/dev/null 2>&1; then
  printf '%s\n' '{"permissionDecision":"ask","permissionDecisionReason":"The policy hook requires the .NET 10 runtime; require human approval."}'
  exit 0
fi

if [ ! -f "$dll" ]; then
  printf '%s\n' '{"permissionDecision":"ask","permissionDecisionReason":"The policy hook is not built. Run ./scripts/bootstrap.sh; require human approval until then."}'
  exit 0
fi

exec dotnet "$dll" preToolUse
