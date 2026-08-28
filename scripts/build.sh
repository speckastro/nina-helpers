#!/usr/bin/env bash
# Build on Linux, run the test suite, and stage the plugin folder the installer packages.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${CONFIG:-Release}"
STAGE="$ROOT/artifacts/SpeckSequenceHelpers"

cd "$ROOT"
echo "== restore + build"
dotnet build SpeckSequenceHelpers.sln -c "$CONFIG" -p:EnableWindowsTargeting=true

echo "== tests"
dotnet test tests/SpeckSequenceHelpers.Core.Tests -c "$CONFIG" --no-build

echo "== stage plugin"
# Publish to a scratch folder, then take only our own assembly. The publish output also
# contains NINA's whole dependency closure (NINA.*, ASCOM, Accord, OxyPlot, ...); N.I.N.A.
# already loads those itself, and a second copy in the plugin folder causes duplicate-type
# load errors. The plugin has no third-party dependencies of its own.
PUBLISH="$(mktemp -d)"
trap 'rm -rf "$PUBLISH"' EXIT
dotnet publish src/SpeckSequenceHelpers/SpeckSequenceHelpers.csproj -c "$CONFIG" -r win-x64 --self-contained false \
  -p:EnableWindowsTargeting=true -o "$PUBLISH"

rm -rf "$STAGE"
mkdir -p "$STAGE"
cp "$PUBLISH/SpeckSequenceHelpers.dll" "$STAGE/"
cp "$PUBLISH/SpeckSequenceHelpers.pdb" "$STAGE/"

echo "== staged: $STAGE"
ls -1 "$STAGE" | sed 's/^/   /'
echo
echo "Copy the folder to %localappdata%\\NINA\\Plugins\\3.0.0\\SpeckSequenceHelpers on the Windows box, then restart NINA."
