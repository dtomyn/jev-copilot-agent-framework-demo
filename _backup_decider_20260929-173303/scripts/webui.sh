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
#   --laya                 start the local Laya container (scripts/laya-up.sh), wait for /health,
#                          and point the UI at it, so live mode is available for Laya
#   --restart              stop a UI that is already running on the port, then start this one;
#                          only ever stops a Jev.WebUi process
set -euo pipefail

repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
port=5088
url="http://127.0.0.1:$port"
arguments_text="$*"
build=1
browser=1
agent_levels=0
laya=0
restart=0

for argument in "$@"; do
  case "$argument" in
    --no-build) build=0 ;;
    --no-browser) browser=0 ;;
    --include-agent-levels) agent_levels=1 ;;
    --laya) laya=1 ;;
    --restart) restart=1 ;;
    *) echo "Unknown option: $argument" >&2; exit 2 ;;
  esac
done

# Git Bash on Windows has tasklist/taskkill; macOS and Linux have ps/kill.
windows=0
if command -v tasklist >/dev/null 2>&1; then windows=1; fi

port_in_use() {
  if command -v lsof >/dev/null 2>&1 || [ "$windows" -eq 1 ]; then
    [ -n "$(port_owner_pids)" ]
    return
  fi

  # No way to list listeners: probe instead. 7 is curl's "could not connect"; anything else,
  # including a non-HTTP reply, means something is there. The limit is generous because Windows
  # takes about two seconds to refuse a loopback connection, which a short one turns into a timeout.
  local status=0
  curl -s --max-time 10 -o /dev/null "$url" || status=$?
  [ "$status" -ne 7 ]
}

port_owner_pids() {
  if command -v lsof >/dev/null 2>&1; then
    lsof -nP -iTCP:"$port" -sTCP:LISTEN -t 2>/dev/null || true
  elif [ "$windows" -eq 1 ]; then
    # Windows netstat: "TCP  127.0.0.1:5088  0.0.0.0:0  LISTENING  52476"
    netstat -ano 2>/dev/null | awk -v suffix=":$port" \
      '$1 == "TCP" && $4 == "LISTENING" && substr($2, length($2) - length(suffix) + 1) == suffix { print $5 }'
  fi | sort -u
}

describe_pid() {
  if [ "$windows" -eq 1 ]; then
    tasklist //FI "PID eq $1" //FO CSV //NH 2>/dev/null | cut -d, -f1 | tr -d '"\r'
  else
    ps -p "$1" -o args= 2>/dev/null || true
  fi
}

stop_command() {
  if [ "$windows" -eq 1 ]; then echo "taskkill //PID $1 //F"; else echo "kill $1"; fi
}

# Checked first, because an instance that is already running keeps the environment it started
# with: it would not see --laya, and this one would only fail to bind after the build and the wait.
if port_in_use; then
  owners="$(port_owner_pids)"
  only_webui=0
  if [ -n "$owners" ]; then
    only_webui=1
    for pid in $owners; do
      case "$(describe_pid "$pid")" in *Jev.WebUi*) ;; *) only_webui=0 ;; esac
    done
  fi

  if [ "$restart" -eq 1 ] && [ "$only_webui" -eq 1 ]; then
    for pid in $owners; do
      echo "Stopping the running UI (PID $pid)..."
      if [ "$windows" -eq 1 ]; then taskkill //PID "$pid" //F >/dev/null; else kill "$pid"; fi
    done

    for _ in $(seq 40); do port_in_use || break; sleep 0.25; done
    if port_in_use; then
      echo "Port $port is still in use after stopping the UI." >&2
      exit 1
    fi
  else
    {
      echo
      echo "Port $port is already in use, so the UI cannot start."
      echo
      for pid in $owners; do echo "  PID $pid  $(describe_pid "$pid")"; done
      echo

      if [ "$only_webui" -eq 1 ]; then
        echo "That is an earlier demo UI. It keeps the environment it was started with, so it"
        echo "will not pick up --laya or a changed LAYA_BASE_URL. Stop it and start this one:"
        echo
        echo "  ./scripts/webui.sh${arguments_text:+ $arguments_text} --restart"
        echo
        echo "or stop it yourself (or press Ctrl+C in the window it is running in):"
      elif [ -n "$owners" ]; then
        echo "That is not the demo UI, so --restart will not stop it. Free the port yourself:"
      else
        echo "The owning process could not be identified. Find it with:"
        echo
        if [ "$windows" -eq 1 ]; then echo "  netstat -ano | grep :$port"; else echo "  lsof -nP -iTCP:$port -sTCP:LISTEN"; fi
        echo
        exit 1
      fi

      echo
      for pid in $owners; do echo "  $(stop_command "$pid")"; done
      echo
    } >&2
    exit 1
  fi
fi

if [ "$build" -eq 1 ]; then
  echo "Building the UI and the hook..."
  dotnet build "$repository/src/Jev.WebUi/Jev.WebUi.csproj" --nologo --verbosity quiet

  if [ "$agent_levels" -eq 1 ]; then
    echo "Building TenLevels.Jev (this downloads the Copilot CLI from npm on a cold cache)..."
    dotnet build "$repository/src/TenLevels.Jev/TenLevels.Jev.csproj" --nologo --verbosity quiet
  fi
fi

if [ "$laya" -eq 1 ]; then
  # After the build, so a compile error surfaces before a potentially long cold-start wait.
  # laya-up leaves an already running container alone, so this is safe to repeat.
  "$repository/scripts/laya-up.sh"

  # The UI offers live mode only when this is set in its own environment. Same defaults as
  # laya-up, which is the service that was just confirmed healthy.
  export LAYA_BASE_URL="http://${LAYA_BIND_ADDRESS:-127.0.0.1}:${LAYA_HOST_PORT:-8010}"
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
if [ "$laya" -eq 1 ]; then
  echo "Laya live mode is available: requests go to $LAYA_BASE_URL."
else
  echo "Provider defaults to Laya in mock mode, which needs no API key and no network."
  echo "Pass --laya to start the local Laya container and enable live mode."
fi
echo "Press Ctrl+C to stop."
echo

if [ "$build" -eq 1 ]; then
  exec dotnet run --project "$repository/src/Jev.WebUi/Jev.WebUi.csproj" --no-build
fi

exec dotnet run --project "$repository/src/Jev.WebUi/Jev.WebUi.csproj"
