#!/bin/sh
# Builds the Linux packages (see README.md). Run it in WSL (or on any Linux machine): sh make.sh [-p:FileVersion=<version>]
set -e
cd "$(dirname "$0")"

# Installs PowerShell (pwsh) from the package repository of Microsoft: Debian, Ubuntu, Fedora (the .NET SDK is not enough, the scripts of src/scripts need pwsh).
install_pwsh() {
    . /etc/os-release
    case "$ID" in
        debian|ubuntu)
            package="$(mktemp --suffix=.deb)"
            wget -q -O "$package" "https://packages.microsoft.com/config/$ID/$VERSION_ID/packages-microsoft-prod.deb"
            sudo dpkg -i "$package"
            rm -f "$package"
            sudo apt-get update
            sudo apt-get install -y powershell
            ;;
        fedora)
            sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc
            curl -sSL https://packages.microsoft.com/config/rhel/9/prod.repo | sudo tee /etc/yum.repos.d/microsoft.repo >/dev/null
            sudo dnf install -y powershell
            ;;
        *)
            echo "Install PowerShell for $ID yourself: https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux" >&2
            return 1
            ;;
    esac
}

if ! command -v pwsh >/dev/null 2>&1; then
    printf 'PowerShell (pwsh) is not installed. Install it now (needs sudo)? [y/N] '
    read -r answer
    case "$answer" in
        y|Y) install_pwsh ;;
    esac
fi

missing=""
for tool in dotnet clang pwsh rpmbuild tar gzip; do
    command -v "$tool" >/dev/null 2>&1 || missing="$missing $tool"
done
if [ -n "$missing" ]; then
    echo "Missing tools:$missing (see the Build section of README.md)" >&2
    exit 1
fi

exec dotnet msbuild LinuxPackages.proj "$@"
