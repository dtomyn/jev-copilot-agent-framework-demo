#!/usr/bin/env bash
# Stops the local Laya decision service.
#
# The model cache lives in a named volume, so stopping and starting again is cheap: only
# --volumes forces the next start to download the checkpoints over again.
set -euo pipefail

laya_home="${LAYA_HOME:-$HOME/laya-local}"
compose="$laya_home/docker-compose.yml"

if [ ! -f "$compose" ]; then
  echo "No docker-compose.yml under '$laya_home'. Set LAYA_HOME." >&2
  exit 1
fi

if [ "${1:-}" = "--volumes" ]; then
  echo "Removing the model cache volume. The next start re-downloads the checkpoints." >&2
  docker compose --file "$compose" down --volumes
else
  docker compose --file "$compose" down
fi

echo "laya-serve stopped."
