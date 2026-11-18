#!/bin/sh
# Makes a package for apk (Alpine Linux): MakeAlpinePackage.sh <root> <version> <architecture> <output>
#   <root>          folder with the usr tree of the package (see the StageRoot target of LinuxPackages.proj)
#   <version>       version and release of Alpine, as 1.0.0-r0
#   <architecture>  x86_64, aarch64 or armv7
#   <output>        the file to write, as crossbible-1.0.0-r0-linux-x86_64.apk
# The package is made by "apk mkpkg" of apk-tools 3 (Alpine 3.21 and later), as a package of its own format cannot be made by hand: apk 3 refuses the files of a
# package that has no checksum for each of them ("file format is obsolete"). So it runs on Alpine (make.bat alpine does). The package is not signed: install it
# with apk add --allow-untrusted.
set -e

root=$1
version=$2
arch=$3
output=$4
if [ -z "$root" ] || [ -z "$version" ] || [ -z "$arch" ] || [ -z "$output" ]; then
    echo "Usage: $0 <root> <version> <architecture> <output>" >&2
    exit 1
fi

if ! command -v apk >/dev/null 2>&1 || ! apk mkpkg --help 2>&1 | grep -q "creates a package file"; then
    echo "apk mkpkg (apk-tools 3, Alpine 3.21 or later) is needed to make the package for Alpine: run the build in Alpine (make.bat alpine)." >&2
    exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# The files are copied to a folder of the Linux file system: the ones on a Windows drive (/mnt/c in WSL) have the permissions of every file set to the same value.
mkdir "$work/pkg"
cp -R "$root/usr" "$work/pkg/usr"
find "$work/pkg" -type d -exec chmod 755 {} +
find "$work/pkg" -type f -exec chmod 644 {} +
chmod 755 "$work/pkg/usr/bin/crossbible" "$work/pkg/usr/share/crossbible/CrossBible.Desktop"

mkdir -p "$(dirname "$output")"
rm -f "$output"
apk mkpkg \
    --files "$work/pkg" \
    --info "name:crossbible" \
    --info "version:$version" \
    --info "arch:$arch" \
    --info "description:A cross platform bible reader, that downloads free bibles from ebible.org and crosswire.org" \
    --info "url:https://www.crossbible.org" \
    --info "license:MIT" \
    --info "origin:crossbible" \
    --info "maintainer:Simon Egli <simon.jakob.egli@gmail.com>" \
    --info "depends:libstdc++ libgcc zlib icu-libs libssl3 fontconfig font-dejavu libx11 libice libsm mesa-gl" \
    --output "$output"
echo "Made $output"
