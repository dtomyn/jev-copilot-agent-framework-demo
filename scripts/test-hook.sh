#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet build src/Jev.CopilotHook/Jev.CopilotHook.csproj -c Release

# Every sample is replayed against all providers, and `expectedDecision` is shared: the gate
# thresholds on max(p), so the provider must change the reported reason and nothing else. A
# sample that only passes on one provider means provider-specific behaviour leaked out of
# SystemOneEndpoint into policy code.
status=0
for provider in jev laya decider clef; do
  for sample in samples/hooks/*.json; do
    name="$(basename "$sample" .json)"
    expected="$(grep -o '"expectedDecision":"[a-z]*"' "$sample" | cut -d'"' -f4)"
    actual="$(DECISION_MODE=mock DECISION_PROVIDER="$provider" ./scripts/jev-hook.sh < "$sample")"
    decision="$(printf '%s' "$actual" | grep -o '"permissionDecision":"[a-z]*"' | cut -d'"' -f4)"
    if [ -n "$expected" ] && [ "$expected" != "$decision" ]; then
      echo "FAIL  [$provider] $name: expected $expected, got $decision -> $actual" >&2
      status=1
    else
      echo "PASS  [$provider] $name -> $decision"
    fi
  done
done
exit $status
