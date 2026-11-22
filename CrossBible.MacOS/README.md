# CrossBible.MacOS

Builds `Cross Bible.app` for macOS, for Apple silicon (`arm64`) and Intel (`x64`). The bundle id is `org.estrellasdeesperanza.CrossBible`, the minimum
macOS is 13, and the version is the one of `Directory.Build.props`.

| Where you build | What you get | Code |
|---|---|---|
| a Mac: `sh make.sh [arm64\|x64]` | `publish/crossbible-<version>-macos-<arch>.dmg` (the `publish` folder at the root of the repository) | native AOT, signed |
| Windows: `make.bat [arm64\|x64]` (WSL) or Linux: `sh make.sh` | `publish/crossbible-<version>-macos-<arch>.tar.gz` | self-contained with the .NET runtime (JIT), not signed |

Native AOT does not build for another operating system, so only a Mac makes the AOT build. The `.tar.gz` keeps the permission to execute (a `.zip` made on
Windows would lose it): unpack it on the Mac, then sign it (below) or remove the quarantine flag (`xattr -dr com.apple.quarantine "Cross Bible.app"`).

## Needs

- the .NET SDK 11.0 (on a Mac also the Xcode command line tools for native AOT: `xcode-select --install`)
- `make.bat`: WSL; it sets up an Ubuntu distribution when there is none (`CrossBible.LinuxPackages/src/scripts/WslSetup.ps1`), the .NET SDK has to be in it

## Signing and notarization (on a Mac)

By default the app is signed ad hoc (Apple silicon does not run unsigned code, and an ad hoc signature is enough to run it on the Mac that built it; other Macs
show a warning for an app from the internet). For a release, sign it with a Developer ID certificate and notarize it:

```sh
CODESIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" NOTARY_PROFILE=my-profile sh make.sh
```

`NOTARY_PROFILE` is a profile that was made once with `xcrun notarytool store-credentials my-profile ...`. A signed app uses the hardened runtime with the
permissions of `Entitlements.plist`.
