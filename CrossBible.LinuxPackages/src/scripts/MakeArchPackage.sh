#!/bin/sh
# Makes a package for pacman (Arch Linux): MakeArchPackage.sh <root> <version> <architecture> <output>
#   <root>          folder with the usr tree of the package (see the StageRoot target of LinuxPackages.proj)
#   <version>       pkgver-pkgrel, as 1.0.0-1
#   <architecture>  x86_64, aarch64 or armv7h
#   <output>        the file to write, as crossbible-1.0.0-1-x86_64.pkg.tar.zst
# A pacman package is a tar archive (zstd compressed) with a .PKGINFO file first and the files after it. It is not signed: install it with pacman -U.
set -e

root=$1
version=$2
arch=$3
output=$4
if [ -z "$root" ] || [ -z "$version" ] || [ -z "$arch" ] || [ -z "$output" ]; then
    echo "Usage: $0 <root> <version> <architecture> <output>" >&2
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

size=$(( $(du -sk "$work/pkg" | cut -f1) * 1024 ))

cat > "$work/pkg/.PKGINFO" <<EOF
pkgname = crossbible
pkgbase = crossbible
pkgver = $version
pkgdesc = A cross platform bible reader, that downloads free bibles from ebible.org and crosswire.org
url = https://www.crossbible.org
builddate = $(date +%s)
packager = Simon Egli <simon.jakob.egli@gmail.com>
size = $size
arch = $arch
license = MIT
depend = glibc
depend = zlib
depend = icu
depend = openssl
depend = fontconfig
depend = ttf-font
depend = libx11
depend = libice
depend = libsm
depend = libglvnd
EOF

mkdir -p "$(dirname "$output")"
rm -f "$output"
tar --owner=0 --group=0 --numeric-owner -C "$work/pkg" -cf - .PKGINFO usr | zstd -19 -T0 -q -o "$output"
echo "Made $output"
