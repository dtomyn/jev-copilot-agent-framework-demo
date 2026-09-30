#!/usr/bin/env bash
set -euo pipefail
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
args=(compose -p jev-decider -f "$repository/docker/decider/compose.yml" down)
if [ "${1:-}" = "--volumes" ]; then args+=(--volumes); fi
docker "${args[@]}"
echo "Decider stopped. Model weights are retained unless --volumes was passed."
