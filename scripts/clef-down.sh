#!/usr/bin/env bash
set -euo pipefail
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
args=(compose -p jev-clef -f "$repository/docker/clef/compose.yml" down)
if [ "${1:-}" = "--volumes" ]; then args+=(--volumes); fi
docker "${args[@]}"
echo "Clef stopped. Model weights are retained unless --volumes was passed."
