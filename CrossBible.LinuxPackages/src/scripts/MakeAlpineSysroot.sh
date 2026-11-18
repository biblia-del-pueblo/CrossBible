#!/bin/sh
# Makes a root file system of Alpine for another architecture, in /opt/sysroot/<architecture>: MakeAlpineSysroot.sh <aarch64|armv7>
# Native AOT links the program with the C library of the target (musl), its start files, the runtime of gcc and libstdc++: for another architecture than the one of
# this machine they come from here (see the PublishRuntime target of LinuxPackages.proj: -p:SysRoot, with lld as the linker). Nothing is done when it is there already.
set -e

arch=$1
case "$arch" in
    aarch64|armv7) ;;
    *) echo "Usage: $0 <aarch64|armv7>" >&2; exit 1 ;;
esac

root=/opt/sysroot/$arch
if [ -f "$root/usr/lib/crt1.o" ] && [ -f "$root/usr/lib/libc.a" ]; then
    exit 0
fi

version=$(cut -d. -f1,2 /etc/alpine-release)
mirror=https://dl-cdn.alpinelinux.org/alpine/v$version
echo "Making the root file system of Alpine $version for $arch in $root"

mkdir -p "$root/etc/apk"
echo "$arch" > "$root/etc/apk/arch"
# The packages of another architecture: their signatures are checked with the keys of that architecture, and their scripts are not run (they could not).
apk --root "$root" --arch "$arch" --initdb --keys-dir "/usr/share/apk/keys/$arch" \
    -X "$mirror/main" -X "$mirror/community" --no-scripts --no-cache \
    add musl-dev gcc libstdc++-dev zlib-dev zlib-static >/dev/null
