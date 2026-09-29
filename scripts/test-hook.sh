#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet build src/Jev.CopilotHook/Jev.CopilotHook.csproj -c Release

status=0
for sample in samples/hooks/*.json; do
  name="$(basename "$sample" .json)"
  expected="$(grep -o '"expectedDecision":"[a-z]*"' "$sample" | cut -d'"' -f4)"
  actual="$(JEV_MODE=mock ./scripts/jev-hook.sh < "$sample")"
  decision="$(printf '%s' "$actual" | grep -o '"permissionDecision":"[a-z]*"' | cut -d'"' -f4)"
  if [ -n "$expected" ] && [ "$expected" != "$decision" ]; then
    echo "FAIL  $name: expected $expected, got $decision -> $actual" >&2
    status=1
  else
    echo "PASS  $name -> $decision"
  fi
done
exit $status
