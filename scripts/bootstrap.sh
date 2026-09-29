#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet was not found. Install the .NET 10 SDK first." >&2
  exit 1
fi

# Levels 1-5, 8, 9 and the Copilot hook need nothing but the SDK and nuget.org.
core=(
  src/Jev.Core/Jev.Core.csproj
  src/Jev.CopilotHook/Jev.CopilotHook.csproj
  tests/Jev.Core.SelfTests/Jev.Core.SelfTests.csproj
)

dotnet restore JevCopilotDemo.sln
for project in "${core[@]}"; do
  dotnet build "$project" -c Release --no-restore
done

dotnet run --project tests/Jev.Core.SelfTests/Jev.Core.SelfTests.csproj -c Release --no-build
./scripts/test-hook.sh

# TenLevels pulls GitHub.Copilot.SDK, whose MSBuild targets download the Copilot CLI from
# registry.npmjs.org at build time. On a restricted network that download is the only thing that
# fails, so it is built last and its failure is reported rather than aborting the bootstrap.
echo
if dotnet build src/TenLevels.Jev/TenLevels.Jev.csproj -c Release --no-restore; then
  echo "Bootstrap complete."
else
  cat >&2 <<'MSG'

The ten-levels project failed to build. If the error mentions registry.npmjs.org, the
GitHub.Copilot.SDK build targets could not download the Copilot CLI. Either:
  * build with CopilotSkipCliDownload=true and set COPILOT_CLI_PATH to an installed copilot binary, or
  * set CopilotNpmRegistryUrl to a mirror, or
  * set CopilotCliBinaryPath to a pre-downloaded binary.
Levels 1-5, 8, and 9 and the Copilot hook are built and usable regardless.
MSG
fi

echo
echo "Try: dotnet run --project src/TenLevels.Jev -- --list"
echo "Then: JEV_MODE=mock copilot"
