#!/usr/bin/env bash
# Idempotent sandbox setup for nina-helpers.
# Everything installs user-level into HOME (persists across sessions, no image rebuild).
set -euo pipefail

DOTNET_DIR="$HOME/.dotnet"
mkdir -p "$HOME/.local/bin"

# --- .NET 8 SDK (build/test the plugin, cross-targeting net8.0-windows) ---
if [ ! -x "$DOTNET_DIR/dotnet" ]; then
    echo "Installing .NET 8 SDK to $DOTNET_DIR ..."
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$DOTNET_DIR"
else
    echo ".NET SDK already present: $("$DOTNET_DIR/dotnet" --version)"
fi
ln -sf "$DOTNET_DIR/dotnet" "$HOME/.local/bin/dotnet"

# Env for future shells (dotnet via symlink needs DOTNET_ROOT).
if ! grep -qs 'DOTNET_ROOT' "$HOME/.profile" 2>/dev/null; then
    {
        echo 'export DOTNET_ROOT="$HOME/.dotnet"'
        echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
        echo 'export DOTNET_NOLOGO=1'
    } >> "$HOME/.profile"
fi
if ! grep -qs '\.dotnet/tools' "$HOME/.profile" 2>/dev/null; then
    echo 'export PATH="$HOME/.dotnet/tools:$PATH"' >> "$HOME/.profile"
fi

# --- GitHub CLI (repo/PR/release operations over HTTPS) ---
if ! command -v gh >/dev/null 2>&1 && [ ! -x "$HOME/.local/bin/gh" ]; then
    echo "Installing GitHub CLI ..."
    GH_VER=$(curl -s https://api.github.com/repos/cli/cli/releases/latest \
        | python3 -c "import json,sys; print(json.load(sys.stdin)['tag_name'].lstrip('v'))")
    curl -sL "https://github.com/cli/cli/releases/download/v${GH_VER}/gh_${GH_VER}_linux_amd64.tar.gz" \
        | tar -xz -C /tmp
    install -m 0755 "/tmp/gh_${GH_VER}_linux_amd64/bin/gh" "$HOME/.local/bin/gh"
    rm -rf "/tmp/gh_${GH_VER}_linux_amd64"
fi

# --- PowerShell 7 (NINA plugin manifest tooling at publish time) ---
PWSH_DIR="$HOME/.powershell"
if [ ! -x "$PWSH_DIR/pwsh" ]; then
    echo "Installing PowerShell from release tarball ..."
    PWSH_VER=$(curl -s https://api.github.com/repos/PowerShell/PowerShell/releases/latest \
        | python3 -c "import json,sys; print(json.load(sys.stdin)['tag_name'].lstrip('v'))")
    mkdir -p "$PWSH_DIR"
    curl -sL "https://github.com/PowerShell/PowerShell/releases/download/v${PWSH_VER}/powershell-${PWSH_VER}-linux-x64.tar.gz" \
        | tar -xz -C "$PWSH_DIR"
    chmod +x "$PWSH_DIR/pwsh"
fi
ln -sf "$PWSH_DIR/pwsh" "$HOME/.local/bin/pwsh"

echo "--- versions ---"
"$DOTNET_DIR/dotnet" --list-sdks
"$HOME/.local/bin/gh" --version | head -1 || true
DOTNET_ROOT="$DOTNET_DIR" "$HOME/.local/bin/pwsh" --version || true
