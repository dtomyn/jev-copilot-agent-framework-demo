#!/usr/bin/env bash
# Starts the local Laya decision service (laya-serve in Docker) and waits until it answers.
#
# The compose project lives outside this repository, because Laya is an upstream checkout rather
# than a vendored dependency. Point LAYA_HOME at it; the default matches docs/LAYA.md.
#
# First boot downloads a checkpoint into a named volume and can take several minutes. The
# container is only healthy once /health answers, so this script waits rather than returning a
# base URL that is not serving yet.
set -euo pipefail

laya_home="${LAYA_HOME:-$HOME/laya-local}"
timeout_minutes="${LAYA_WAIT_MINUTES:-20}"

if ! command -v docker >/dev/null 2>&1; then
  echo "docker was not found. Install Docker, or run laya-serve directly (see docs/LAYA.md)." >&2
  exit 1
fi

compose="$laya_home/docker-compose.yml"
if [ ! -f "$compose" ]; then
  echo "No docker-compose.yml under '$laya_home'. Set LAYA_HOME." >&2
  exit 1
fi

bind="${LAYA_BIND_ADDRESS:-127.0.0.1}"
port="${LAYA_HOST_PORT:-8010}"
base_url="http://$bind:$port"

echo "Starting laya-serve from $compose ..."
docker compose --file "$compose" up --detach --build

echo "Waiting for $base_url/health (up to $timeout_minutes minute(s); a cold start downloads the checkpoint)..."
deadline=$(( $(date +%s) + (timeout_minutes * 60) ))
while [ "$(date +%s)" -lt "$deadline" ]; do
  if curl -fsS --max-time 5 "$base_url/health" >/dev/null 2>&1; then
    echo
    echo "laya-serve is ready."
    curl -fsS "$base_url/health"
    echo
    echo "Point the demo at it:"
    echo "  export DECISION_PROVIDER=laya"
    echo "  export LAYA_BASE_URL=$base_url"
    echo "  dotnet run --project src/TenLevels.Jev -- --level 4 --provider laya --mode live"
    exit 0
  fi

  # Not up yet. The container publishes the port only after preloading, so a refused connection
  # here is the normal case.
  printf '.'
  sleep 5
done

echo
echo "laya-serve did not answer within $timeout_minutes minute(s). Check: docker compose --file $compose logs --tail 50" >&2
exit 1
