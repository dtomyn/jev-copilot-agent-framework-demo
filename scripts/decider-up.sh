#!/usr/bin/env bash
# Builds/starts Mapika Decider in Docker and waits for /health.
set -euo pipefail

repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose="$repository/docker/decider/compose.yml"
gpu_compose="$repository/docker/decider/compose.gpu.yml"
gpu=0
rebuild=0

for arg in "$@"; do
  case "$arg" in
    --gpu) gpu=1 ;;
    --rebuild) rebuild=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

command -v docker >/dev/null 2>&1 || { echo "docker was not found. See docs/DECIDER.md." >&2; exit 1; }
port="${DECIDER_HOST_PORT:-8011}"
base_url="http://127.0.0.1:$port"
model="${DECIDER_MODEL:-Mapika/decider-4b}"
compose_args=(compose -p jev-decider -f "$compose")
if [ "$gpu" -eq 1 ]; then compose_args+=(-f "$gpu_compose"); fi

echo "Starting Decider ($model) on $base_url ..."
if [ "$rebuild" -eq 1 ]; then
  echo "Forcing a clean Decider image rebuild..."
  docker "${compose_args[@]}" build --no-cache
fi

# Always build through Compose. With an unchanged backend this is layer-cache cheap; when the user
# switches CPU <-> GPU it prevents Compose from reusing the image built for the other backend.
up_args=(up -d --build)
docker "${compose_args[@]}" "${up_args[@]}"

for _ in $(seq 1 600); do
  if curl -fsS --max-time 5 "$base_url/health" >/tmp/jev-decider-health.json 2>/dev/null; then
    echo "Decider is ready: $(cat /tmp/jev-decider-health.json)"
    rm -f /tmp/jev-decider-health.json
    echo
    echo "  export DECISION_PROVIDER=decider"
    echo "  export DECIDER_BASE_URL=$base_url"
    echo "  dotnet run --project src/TenLevels.Jev -- --level 4 --provider decider --mode live"
    exit 0
  fi
  sleep 3
done

echo "Decider did not become healthy. Recent logs:" >&2
docker "${compose_args[@]}" logs --tail 80 decider >&2
exit 1
