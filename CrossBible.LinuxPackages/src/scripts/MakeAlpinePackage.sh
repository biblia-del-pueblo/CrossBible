#!/bin/sh
# Makes a package for apk (Alpine Linux): MakeAlpinePackage.sh <root> <version> <architecture> <output>
#   <root>          folder with the usr tree of the package (see the StageRoot target of LinuxPackages.proj)
#   <version>       version and release of Alpine, as 1.0.0-r0
#   <architecture>  x86_64, aarch64 or armv7
#   <output>        the file to write, as crossbible-1.0.0-r0-x86_64.apk
# An apk (format 2) is three gzip streams one after the other, that make one tar archive: the signature (not here), the control part (.PKGINFO, which has
# the sha256 of the data part) and the data part (the files). It is not signed: install it with apk add --allow-untrusted. Needs GNU tar (apk add tar on Alpine).
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

# The data part.
tar --format=ustar --owner=0 --group=0 --numeric-owner -C "$work/pkg" -cf - usr | gzip -9n > "$work/data.tar.gz"
datahash=$(sha256sum "$work/data.tar.gz" | cut -d' ' -f1)

# The control part: its tar archive has no end (the two empty blocks), as the data part follows it in the same archive. -b 1 leaves out the padding of
# the last block, so the end is exactly the last 1024 bytes.
mkdir "$work/control"
cat > "$work/control/.PKGINFO" <<EOF
pkgname = crossbible
pkgver = $version
pkgdesc = A cross platform bible reader, that downloads free bibles from ebible.org and crosswire.org
url = https://www.crossbible.org
builddate = $(date +%s)
packager = Simon Egli <simon.jakob.egli@gmail.com>
size = $size
arch = $arch
origin = crossbible
license = MIT
depend = libstdc++
depend = libgcc
depend = zlib
depend = icu-libs
depend = libssl3
depend = fontconfig
depend = libx11
depend = libice
depend = libsm
depend = mesa-gl
datahash = $datahash
EOF
tar --format=ustar --owner=0 --group=0 --numeric-owner -b 1 -C "$work/control" -cf "$work/control.tar" .PKGINFO
size_tar=$(wc -c < "$work/control.tar")
head -c $((size_tar - 1024)) "$work/control.tar" | gzip -9n > "$work/control.tar.gz"

mkdir -p "$(dirname "$output")"
cat "$work/control.tar.gz" "$work/data.tar.gz" > "$output"
echo "Made $output"
