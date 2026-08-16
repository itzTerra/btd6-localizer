#!/usr/bin/env bash
set -euo pipefail

MIN_DOTNET_MAJOR=8

have_dotnet() {
    command -v dotnet >/dev/null 2>&1
}

dotnet_major_version() {
    dotnet --version 2>/dev/null | cut -d. -f1
}

confirm() {
    local reply
    read -r -p "$1 [y/N] " reply
    [[ "$reply" =~ ^[Yy]$ ]]
}

install_dotnet_linux() {
    if command -v apt >/dev/null 2>&1; then
        echo "About to run: sudo apt update && sudo apt install -y dotnet-sdk-8.0"
        confirm "Proceed?" || exit 1
        sudo apt update && sudo apt install -y dotnet-sdk-8.0
    elif command -v dnf >/dev/null 2>&1; then
        echo "About to run: sudo dnf install -y dotnet-sdk-8.0"
        confirm "Proceed?" || exit 1
        sudo dnf install -y dotnet-sdk-8.0
    elif command -v pacman >/dev/null 2>&1; then
        echo "About to run: sudo pacman -S --noconfirm dotnet-sdk"
        confirm "Proceed?" || exit 1
        sudo pacman -S --noconfirm dotnet-sdk
    else
        echo "No supported package manager (apt, dnf, pacman) found."
        echo "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    fi
}

install_dotnet_windows() {
    if command -v winget >/dev/null 2>&1; then
        echo "About to run: winget install Microsoft.DotNet.SDK.8"
        confirm "Proceed?" || exit 1
        winget install Microsoft.DotNet.SDK.8
    else
        echo "winget was not found."
        echo "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    fi
}

install_dotnet() {
    case "$(uname -s)" in
        Linux*)
            install_dotnet_linux
            ;;
        MINGW*|MSYS*|CYGWIN*)
            install_dotnet_windows
            ;;
        *)
            echo "No supported automatic installer for this platform ($(uname -s))."
            echo "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
            exit 1
            ;;
    esac
}

if ! have_dotnet || [[ "$(dotnet_major_version)" -lt "$MIN_DOTNET_MAJOR" ]]; then
    echo ".NET SDK $MIN_DOTNET_MAJOR or newer is required but was not found."
    install_dotnet
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec dotnet run --project "$SCRIPT_DIR/src/Btd6Localizer" -- "$@"
