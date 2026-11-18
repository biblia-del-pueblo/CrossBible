# Build Setup

For this project to fully build you need to have a WSL distro with rpmbuild installed. If not, the Fedora rpm package won't be built. This has been tested with Ubuntu & Fedora Remix.

To install WSL Ubuntu run:

wsl --install Ubuntu

and inside Ubuntu run

sudo apt install rpm rpmlint

To install Fedora Remix run:

winget install -e --id whitewaterfoundry.fedora-remix-for-wsl

and inside Fedora run

sudo dnf install rpmdevtools rpmlint

# Structure of the Folders

* The content of the Debian package resides in the Debian folder.
* The content of the RPM package resides in the Fedora folder. For Fedora, the .specs file must reside in
  the Fedora\SPECS folder.
* In the src folder there are the some common files that will be copied to the Debian and Fedora
  folder before build.

# Build the Packages

The packages hold the native AOT build of CrossBible (no .NET runtime is needed to run them). Native AOT does not build for another
operating system, so the packages are built on Linux, in WSL on Windows. Run

    make.bat [msbuild arguments, as -p:FileVersion=<Version>]

It builds both: the packages for Debian, Fedora and Arch Linux in Ubuntu, then the packages for Alpine in Alpine (the musl builds only work there).
`make.bat ubuntu` or `make.bat alpine` builds only one of them. What is missing is installed: the WSL distribution (an Ubuntu that is installed already is
used, Alpine is imported from alpinelinux.org: `src\scripts\WslSetup.ps1`) and, in it, the tools (`make.sh`: .NET SDK, `clang`, `zlib1g-dev`, `rpm`, `zstd`,
PowerShell, the cross compilers for ARM; on Alpine `clang`, `build-base`, `tar` and the .NET SDK). The Windows part needs WSL itself: `wsl --install --no-distribution`
in an administrator terminal, once.

Close Visual Studio while the packages are built: it restores the projects in the background and overwrites `project.assets.json` with its own NuGet
folders, which stops the build in WSL.

There are packages for three architectures:

| .NET runtime | Debian (.deb) | Fedora (.rpm) | Arch Linux (.pkg.tar.zst) |
|--------------|---------------|---------------|---------------------------|
| linux-x64    | amd64         | x86_64        | x86_64                    |
| linux-arm64  | arm64         | aarch64       | aarch64                   |
| linux-arm    | armhf         | armv7hl       | armv7h                    |

The packages (`crossbible_<Version>-1_<arch>.deb`, `crossbible-<Version>-1.<arch>.rpm`, `crossbible-<Version>-1-<arch>.pkg.tar.zst`) and a `.tar.gz` of
each architecture are written to the bin folder. Each package is put together in its own folder in staging (Debian and Fedora are the templates), the AOT
build of each runtime is in `..\publish`. The packages are not signed (install the Arch Linux one with `pacman -U`). `-t:PublishDesktopAot` only publishes
the `.tar.gz` files, with the musl ones too.

Without Windows, `sh make.sh [msbuild arguments]` does the same in a Linux shell.

# Alpine Linux

Alpine uses musl, so its packages (`crossbible-<Version>-r0-<arch>.apk`, for x86_64, aarch64 and armv7) are made from the musl runtimes. Native AOT for musl
needs a musl toolchain: build on an Alpine machine or container (`apk add tar clang build-base zlib-dev`, and the .NET SDK for musl), with

    sh make.sh -t:PackageAlpine -p:FileVersion=<Version>

or publish the musl runtimes there (`-t:PublishDesktopAot`), copy the `publish` folder to the machine that makes the packages, and run

    sh make.sh -t:PackageAlpine -p:SkipPublish=true -p:FileVersion=<Version>

Install a package with `apk add --allow-untrusted crossbible-<Version>-r0-<arch>.apk` (it is not signed).
