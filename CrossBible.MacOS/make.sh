#!/bin/sh
# Builds CrossBible.app for macOS, for Apple silicon (arm64) and Intel (x64) (see README.md):
#   sh make.sh            both architectures
#   sh make.sh arm64      only Apple silicon
#   sh make.sh x64        only Intel
# On a Mac the app is compiled to machine code (native AOT), signed (ad hoc, or with CODESIGN_IDENTITY) and packed in a .dmg and a .zip.
# Native AOT does not build for another operating system, so on Linux (WSL on Windows: make.bat) the app is published self-contained with the .NET runtime
# instead, and packed in a .tar.gz (the .app has to be unpacked on the Mac, and is not signed: see README.md).
set -e
cd "$(dirname "$0")"
HERE="$(pwd)"
ROOT="$(cd .. && pwd)"
export PATH="$PATH:$HOME/.dotnet/tools:/usr/share/dotnet:/usr/local/share/dotnet"

case "${1:-both}" in
    both) ARCHS="arm64 x64" ;;
    arm64|x64) ARCHS="$1" ;;
    *) echo "Usage: sh make.sh [arm64|x64]" >&2; exit 1 ;;
esac

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -n 1)"
[ -n "$VERSION" ] || { echo "The version was not found in Directory.Build.props." >&2; exit 1; }

if ! command -v dotnet >/dev/null 2>&1; then
    echo "The .NET SDK (11.0) is not installed: https://dotnet.microsoft.com/download" >&2
    exit 1
fi

if [ "$(uname -s)" = "Darwin" ]; then MAC=1; else MAC=""; fi
if [ -n "$MAC" ]; then
    command -v xcrun >/dev/null 2>&1 && xcrun --find clang >/dev/null 2>&1 || {
        echo "Native AOT needs the Xcode command line tools: xcode-select --install" >&2
        exit 1
    }
fi

OUT="$HERE/bin"
mkdir -p "$OUT"
# The .app is made in a folder of the Linux/macOS file system (not on a Windows drive, which does not keep the permission to execute).
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

for ARCH in $ARCHS; do
    RID="osx-$ARCH"
    APP="$WORK/$ARCH/Cross Bible.app"
    echo "=== $RID ==="

    if [ -n "$MAC" ]; then AOT="-p:UseAot=true"; else AOT=""; fi
    dotnet publish "$ROOT/CrossBible.Desktop/CrossBible.Desktop.csproj" -c Release -r "$RID" --self-contained $AOT \
        -p:DebugType=None -p:DebugSymbols=false -o "$WORK/publish-$ARCH"

    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
    cp -R "$WORK/publish-$ARCH/." "$APP/Contents/MacOS/"
    rm -f "$APP/Contents/MacOS/"*.pdb "$APP/Contents/MacOS/"*.dbg
    cp "$ROOT/CrossBible.Desktop/Assets/logo.icns" "$APP/Contents/Resources/logo.icns"
    sed "s/@VERSION@/$VERSION/g" "$HERE/Info.plist" > "$APP/Contents/Info.plist"
    chmod 755 "$APP/Contents/MacOS/CrossBible.Desktop"

    NAME="CrossBible-$VERSION-macos-$ARCH"
    if [ -n "$MAC" ]; then
        # Ad hoc ("-") unless CODESIGN_IDENTITY is a certificate ("Developer ID Application: ..."): a Mac with Apple silicon does not run code that is not signed at all.
        IDENTITY="${CODESIGN_IDENTITY:--}"
        if [ "$IDENTITY" = "-" ]; then
            codesign --force --deep --sign - "$APP"
        else
            codesign --force --deep --timestamp --options runtime --entitlements "$HERE/Entitlements.plist" --sign "$IDENTITY" "$APP"
        fi
        codesign --verify --deep --strict "$APP"

        rm -f "$OUT/$NAME.zip" "$OUT/$NAME.dmg"
        ditto -c -k --keepParent "$APP" "$OUT/$NAME.zip"
        if [ -n "$NOTARY_PROFILE" ] && [ "$IDENTITY" != "-" ]; then
            # The profile is made once with: xcrun notarytool store-credentials <profile> ...
            xcrun notarytool submit "$OUT/$NAME.zip" --keychain-profile "$NOTARY_PROFILE" --wait
            xcrun stapler staple "$APP"
            rm -f "$OUT/$NAME.zip"
            ditto -c -k --keepParent "$APP" "$OUT/$NAME.zip"
        fi
        DMG="$WORK/dmg-$ARCH"
        mkdir -p "$DMG"
        cp -R "$APP" "$DMG/"
        ln -s /Applications "$DMG/Applications"
        hdiutil create -volname "Cross Bible" -srcfolder "$DMG" -ov -format UDZO "$OUT/$NAME.dmg"
        [ "$IDENTITY" = "-" ] || codesign --force --sign "$IDENTITY" "$OUT/$NAME.dmg"
        echo "Made $OUT/$NAME.dmg and $NAME.zip"
    else
        rm -f "$OUT/$NAME.tar.gz"
        tar -C "$WORK/$ARCH" -czf "$OUT/$NAME.tar.gz" "Cross Bible.app"
        echo "Made $OUT/$NAME.tar.gz (not signed, not AOT: see README.md)"
    fi
done

echo
echo "Done. The packages are in $OUT"
ls -1 "$OUT"
