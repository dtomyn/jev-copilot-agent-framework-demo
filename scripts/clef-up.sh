#!/usr/bin/env bash
# Builds/starts Cloudflare Clef in Docker and waits for /health.
# Clef-Flash needs ~19 GB for its BF16 weights: use --gpu on a 24 GB+ NVIDIA GPU. CPU works but is
# too slow for the hook's deadline. See docs/CLEF.md for the hosted Workers AI alternative.
set -euo pipefail

repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose="$repository/docker/clef/compose.yml"
gpu_compose="$repository/docker/clef/compose.gpu.yml"
gpu=0
rebuild=0

for arg in "$@"; do
  case "$arg" in
    --gpu) gpu=1 ;;
    --rebuild) rebuild=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

command -v docker >/dev/null 2>&1 || { echo "docker was not found. See docs/CLEF.md." >&2; exit 1; }
port="${CLEF_HOST_PORT:-8012}"
base_url="http://127.0.0.1:$port"
model="${CLEF_MODEL:-clef-flash}"
compose_args=(compose -p jev-clef -f "$compose")
if [ "$gpu" -eq 1 ]; then
  compose_args+=(-f "$gpu_compose")
else
  echo "Starting Clef on CPU. Expect ~24 GB of container RAM and multi-second answers; pass --gpu on a 24 GB+ NVIDIA GPU." >&2
fi

echo "Starting Clef ($model) on $base_url ..."
echo "The first start downloads ~19 GB of weights into the Docker volume 'jev-clef_clef-model-cache'."
if [ "$rebuild" -eq 1 ]; then
  echo "Forcing a clean Clef image rebuild..."
  docker "${compose_args[@]}" build --no-cache
fi

# Always build through Compose. With an unchanged backend this is layer-cache cheap; when the user
# switches CPU <-> GPU it prevents Compose from reusing the image built for the other backend.
docker "${compose_args[@]}" up -d --build

# /health answers 503 while the weights download and load, and 500 if loading failed for good.
for _ in $(seq 1 1080); do
  status="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 5 "$base_url/health" 2>/dev/null || true)"
  if [ "$status" = "200" ]; then
    echo "Clef is ready: $(curl -fsS --max-time 5 "$base_url/health")"
    echo
    echo "  export DECISION_PROVIDER=clef"
    echo "  export CLEF_BASE_URL=$base_url"
    echo "  dotnet run --project src/TenLevels.Jev -- --level 4 --provider clef --mode live"
    exit 0
  fi
  if [ "$status" = "500" ]; then break; fi
  sleep 5
done

echo "Clef did not become healthy. Recent logs:" >&2
docker "${compose_args[@]}" logs --tail 80 clef >&2
exit 1
