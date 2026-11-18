#!/bin/sh
# Installs CrossBible, from the unpacked .tar.gz package: the generic one (for every Linux and processor that has the .NET runtime 11 or later) or a native one
# (compiled for one processor, needs no runtime).
#
#   sudo ./install.sh                  into /usr/local (the program in lib/crossbible, the command crossbible in bin)
#   ./install.sh --user                into ~/.local, for you only (no root needed)
#   ./install.sh --prefix /opt/cb      into another folder
#   ./install.sh --uninstall [...]     removes what was installed (with the same --user or --prefix)
#
# The generic package is the .NET program: it runs on the .NET runtime (https://dotnet.microsoft.com/download), that has to be installed (version 11 or later), and has
# the libraries for the graphics (SkiaSharp, HarfBuzz) of all the Linux systems: .NET takes the ones of this machine.
set -e

here=$(cd "$(dirname "$0")" && pwd)
prefix=/usr/local
uninstall=0

usage() {
    sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
    case "$1" in
        --prefix) prefix=$2; shift 2 ;;
        --prefix=*) prefix=${1#--prefix=}; shift ;;
        --user) prefix=$HOME/.local; shift ;;
        --uninstall) uninstall=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 1 ;;
    esac
done

libdir=$prefix/lib/crossbible
bindir=$prefix/bin
sharedir=$prefix/share

# The menu of the desktop is told about the new entry in the places it reads (not in a folder of your own: the tool would leave a cache file there).
refresh_desktop_files() {
    case "$prefix" in
        /usr|/usr/local|"$HOME/.local")
            command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$sharedir/applications" >/dev/null 2>&1 || true
            ;;
    esac
}

if [ "$uninstall" = "1" ]; then
    rm -rf "$libdir" "$sharedir/doc/crossbible"
    rm -f "$bindir/crossbible" "$sharedir/applications/crossbible.desktop" "$sharedir/pixmaps/crossbible.png"
    refresh_desktop_files
    echo "CrossBible is removed from $prefix"
    exit 0
fi

# What is in the package: the .NET program (CrossBible.Desktop.dll, runs on the runtime) or the native one (CrossBible.Desktop, a program of the processor).
if [ -f "$here/crossbible/CrossBible.Desktop.dll" ]; then
    kind=dotnet
elif [ -f "$here/crossbible/CrossBible.Desktop" ]; then
    kind=native
else
    echo "The folder crossbible with the program is not next to install.sh: run it from the folder of the unpacked package." >&2
    exit 1
fi

# The runtime is only looked for: a machine can get it later (and DOTNET_ROOT may tell where it is), so it is a warning.
if [ "$kind" = "dotnet" ]; then
    if ! command -v dotnet >/dev/null 2>&1 && [ ! -x "${DOTNET_ROOT:-/nonexistent}/dotnet" ]; then
        echo "Warning: the .NET runtime (version 11 or later) is not installed, or not in the PATH: CrossBible does not start without it." >&2
        echo "         See https://dotnet.microsoft.com/download (or the package manager of your system: dotnet-runtime-11.0)." >&2
    elif command -v dotnet >/dev/null 2>&1 && ! dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft\.NETCore\.App 1[1-9]\.'; then
        echo "Warning: no .NET runtime of version 11 or later is installed (dotnet --list-runtimes): CrossBible needs it." >&2
    fi
fi

mkdir -p "$libdir" "$bindir" "$sharedir/applications" "$sharedir/pixmaps" "$sharedir/doc/crossbible" 2>/dev/null || {
    echo "Cannot write to $prefix: run it as root (sudo ./install.sh), or install for yourself with ./install.sh --user" >&2
    exit 1
}

# What an earlier version left is replaced, not added to.
rm -rf "$libdir"
mkdir -p "$libdir"
cp -R "$here/crossbible/." "$libdir/"
find "$libdir" -type d -exec chmod 755 {} +
find "$libdir" -type f -exec chmod 644 {} +
[ "$kind" = "native" ] && chmod 755 "$libdir/CrossBible.Desktop"

# The command. For the .NET program DOTNET_ROOT is honoured: the runtime does not have to be in the PATH.
if [ "$kind" = "native" ]; then
    cat > "$bindir/crossbible" <<EOF
#!/bin/sh
exec "$libdir/CrossBible.Desktop" "\$@"
EOF
else
    cat > "$bindir/crossbible" <<EOF
#!/bin/sh
if [ -n "\$DOTNET_ROOT" ] && [ -x "\$DOTNET_ROOT/dotnet" ]; then
    exec "\$DOTNET_ROOT/dotnet" "$libdir/CrossBible.Desktop.dll" "\$@"
fi
exec dotnet "$libdir/CrossBible.Desktop.dll" "\$@"
EOF
fi
chmod 755 "$bindir/crossbible"

cp "$here/crossbible.png" "$sharedir/pixmaps/crossbible.png"
sed -e "s|^Exec=.*|Exec=$bindir/crossbible|" -e "s|^Icon=.*|Icon=$sharedir/pixmaps/crossbible.png|" "$here/crossbible.desktop" > "$sharedir/applications/crossbible.desktop"
for doc in README copyright ChangeLog; do
    [ -f "$here/$doc" ] && cp "$here/$doc" "$sharedir/doc/crossbible/$doc"
done
chmod -R go+rX "$sharedir/doc/crossbible" 2>/dev/null || true
refresh_desktop_files

echo "CrossBible is installed in $prefix: run $bindir/crossbible (or find it in the menu of your desktop)"
