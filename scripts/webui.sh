#!/usr/bin/env bash
# Builds and starts the presentation UI, then opens a browser at it.
#
# The UI needs Jev.Core and the hook executable, both of which build offline. It does NOT need
# TenLevels.Jev, which downloads the GitHub Copilot runtime from registry.npmjs.org: build that
# one only when the Copilot-backed levels (6, 7, 10) are going to be run from the browser.
#
#   --no-build             skip the build
#   --no-browser           do not launch a browser
#   --include-agent-levels also build TenLevels.Jev
set -euo pipefail

repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
url="http://127.0.0.1:5088"
build=1
browser=1
agent_levels=0

for argument in "$@"; do
  case "$argument" in
    --no-build) build=0 ;;
    --no-browser) browser=0 ;;
    --include-agent-levels) agent_levels=1 ;;
    *) echo "Unknown option: $argument" >&2; exit 2 ;;
  esac
done

if [ "$build" -eq 1 ]; then
  echo "Building the UI and the hook..."
  dotnet build "$repository/src/Jev.WebUi/Jev.WebUi.csproj" --nologo --verbosity quiet

  if [ "$agent_levels" -eq 1 ]; then
    echo "Building TenLevels.Jev (this downloads the Copilot CLI from npm on a cold cache)..."
    dotnet build "$repository/src/TenLevels.Jev/TenLevels.Jev.csproj" --nologo --verbosity quiet
  fi
fi

if [ "$browser" -eq 1 ]; then
  # Not waited on: the page retries its own API calls, so a browser that arrives a second early
  # simply loads a moment later.
  if command -v xdg-open >/dev/null 2>&1; then
    xdg-open "$url" >/dev/null 2>&1 &
  elif command -v open >/dev/null 2>&1; then
    open "$url" >/dev/null 2>&1 &
  fi
fi

echo
echo "Presentation UI: $url"
echo "Provider defaults to Laya in mock mode, which needs no API key and no network."
echo "Press Ctrl+C to stop."
echo

if [ "$build" -eq 1 ]; then
  exec dotnet run --project "$repository/src/Jev.WebUi/Jev.WebUi.csproj" --no-build
fi

exec dotnet run --project "$repository/src/Jev.WebUi/Jev.WebUi.csproj"
