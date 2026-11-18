#!/bin/sh
# Builds the Linux packages (see README.md). Run it in WSL (make.bat does) or on any Linux machine: sh make.sh [msbuild arguments, as -p:FileVersion=<version>]
#   Debian, Ubuntu: the packages for Debian, Fedora and Arch Linux (the default target of LinuxPackages.proj).
#   Alpine: the packages for Alpine (-t:PackageAlpine, added when there is no -t argument), as the musl runtimes only build there.
# Tools that are missing are installed (apt or apk) after a question. MAKE_YES=1 answers it with yes (make.bat does, in a distro it has just made).
set -e
cd "$(dirname "$0")"

. /etc/os-release
export PATH="$PATH:$HOME/.dotnet/tools:/usr/share/dotnet"

if [ "$(id -u)" = "0" ]; then SUDO=""; else SUDO="sudo"; fi

# Installs PowerShell (pwsh) from the package repository of Microsoft: Debian, Ubuntu, Fedora (the .NET SDK is not enough, the scripts of src/scripts need pwsh).
install_pwsh() {
    case "$ID" in
        debian|ubuntu)
            package="$(mktemp --suffix=.deb)"
            wget -q -O "$package" "https://packages.microsoft.com/config/$ID/$VERSION_ID/packages-microsoft-prod.deb"
            $SUDO dpkg -i "$package"
            rm -f "$package"
            $SUDO apt-get update
            $SUDO apt-get install -y powershell
            ;;
        fedora)
            $SUDO rpm --import https://packages.microsoft.com/keys/microsoft.asc
            curl -sSL https://packages.microsoft.com/config/rhel/9/prod.repo | $SUDO tee /etc/yum.repos.d/microsoft.repo >/dev/null
            $SUDO dnf install -y powershell
            ;;
        *)
            echo "Install PowerShell for $ID yourself: https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux" >&2
            return 1
            ;;
    esac
}

# Installs the .NET SDK of Microsoft (channel 11.0) in /usr/share/dotnet, when the distribution has no package of it.
install_dotnet_script() {
    curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    $SUDO bash /tmp/dotnet-install.sh --channel 11.0 --quality preview --install-dir /usr/share/dotnet
    $SUDO ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
    rm -f /tmp/dotnet-install.sh
}

install_prerequisites() {
    case "$ID" in
        debian|ubuntu)
            $SUDO apt-get update
            $SUDO apt-get install -y wget curl ca-certificates clang zlib1g-dev rpm zstd tar gzip \
                gcc-aarch64-linux-gnu binutils-aarch64-linux-gnu gcc-arm-linux-gnueabihf binutils-arm-linux-gnueabihf
            command -v dotnet >/dev/null 2>&1 || $SUDO apt-get install -y dotnet-sdk-11.0 || install_dotnet_script
            command -v pwsh >/dev/null 2>&1 || install_pwsh
            ;;
        alpine)
            $SUDO apk add --no-cache bash curl tar gzip coreutils clang build-base zlib-dev \
                icu-libs krb5-libs libgcc libintl libssl3 libstdc++ zlib
            command -v dotnet >/dev/null 2>&1 || install_dotnet_script
            ;;
        *)
            echo "Install the tools yourself (see the Build section of README.md): $missing" >&2
            return 1
            ;;
    esac
}

# Alpine: the musl runtimes only build there, so the target is the one for Alpine.
case "$ID" in
    alpine)
        case " $* " in
            *" -t:"*) ;;
            *) set -- -t:PackageAlpine "$@" ;;
        esac
        ;;
esac

# Only the Alpine packages (see README.md) do not need wpkg, so not pwsh and rpmbuild either.
case " $* " in
    *PackageAlpine*|*PublishDesktopAot*) tools="dotnet clang tar gzip sha256sum" ;;
    *) tools="dotnet clang pwsh rpmbuild tar gzip zstd" ;;
esac

missing=""
for tool in $tools; do
    command -v "$tool" >/dev/null 2>&1 || missing="$missing $tool"
done

if [ -n "$missing" ]; then
    echo "Missing tools:$missing"
    if [ "${MAKE_YES:-}" = "1" ]; then
        answer=y
    else
        printf 'Install them now (apt or apk, needs sudo)? [y/N] '
        read -r answer
    fi
    case "$answer" in
        y|Y) install_prerequisites ;;
    esac

    missing=""
    for tool in $tools; do
        command -v "$tool" >/dev/null 2>&1 || missing="$missing $tool"
    done
    if [ -n "$missing" ]; then
        echo "Missing tools:$missing (see the Build section of README.md)" >&2
        exit 1
    fi
fi

exec dotnet msbuild LinuxPackages.proj "$@"
