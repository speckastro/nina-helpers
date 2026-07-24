#!/usr/bin/env bash
# Idempotent sandbox setup for nina-helpers: user-level .NET 8 SDK.
set -euo pipefail

DOTNET_DIR="$HOME/.dotnet"

if [ ! -x "$DOTNET_DIR/dotnet" ]; then
    echo "Installing .NET 8 SDK to $DOTNET_DIR ..."
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$DOTNET_DIR"
else
    echo ".NET SDK already present: $("$DOTNET_DIR/dotnet" --version)"
fi

mkdir -p "$HOME/.local/bin"
ln -sf "$DOTNET_DIR/dotnet" "$HOME/.local/bin/dotnet"

# Ensure DOTNET_ROOT for future shells (needed when invoking via the symlink).
if ! grep -qs 'DOTNET_ROOT' "$HOME/.profile" 2>/dev/null; then
    {
        echo 'export DOTNET_ROOT="$HOME/.dotnet"'
        echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
        echo 'export DOTNET_NOLOGO=1'
    } >> "$HOME/.profile"
fi

"$DOTNET_DIR/dotnet" --list-sdks
