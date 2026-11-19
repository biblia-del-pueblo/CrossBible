# Build Setup

For this project to fully build you need to have a WSL distro with rpmbuild installed. If not, the Fedora rpm package won't be built. This has been tested with Ubuntu & Fedora Remix.

To install WSL Ubuntu 22.04 (the oldest Ubuntu that is supported: the packages are built on it, so that they run on it and on newer ones) run:

wsl --install Ubuntu-22.04

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
* In the src folder there are some common files that will be copied to the Debian and Fedora
  folder before build.

# Build the Packages

The packages hold the native AOT build of CrossBible (no .NET runtime is needed to run them). Native AOT does not build for another
operating system, so the packages are built on Linux, in WSL on Windows. Run

    make.bat [msbuild arguments, as -p:FileVersion=<Version>]

It builds both: the packages for Debian, Fedora and Arch Linux in Ubuntu 22.04, then the packages for Alpine in Alpine (the musl builds only work there).
`make.bat ubuntu` or `make.bat alpine` builds only one of them. A native AOT program does not run on a system whose C library is older than the one of the machine that
built it, so the oldest systems that are still supported build the packages: Ubuntu 22.04 (`Ubuntu-22.04`), and the oldest Alpine release that is supported (read from
https://alpinelinux.org/releases.json, and not older than 3.21: 3.22 on 2026-11-19; the distribution is called `Alpine-3.22`, and a newer release takes its place
when the older one is no longer supported). Other Ubuntu or Alpine distributions that are installed are not used. What is missing is installed: the WSL distribution
(Ubuntu from the catalog of WSL, Alpine imported from alpinelinux.org: `src\scripts\WslSetup.ps1`) and, in it, the tools (`make.sh`: .NET SDK, `clang`, `zlib1g-dev`, `rpm`, `zstd`,
PowerShell, the cross compilers for ARM; on Alpine `clang`, `build-base`, `tar` and the .NET SDK). The Windows part needs WSL itself: `wsl --install --no-distribution`
in an administrator terminal, once.

The packages are not built in the folder of Windows (`/mnt/c`): `make.sh` copies the sources to `~/crossbible-build` in the file system of Linux (`rsync`, without
`bin`, `obj` and the projects that are not needed), builds there, and copies the packages (`bin`) and the builds (`publish`) back. That is faster, the files have
their execute permission, and Visual Studio, which restores the projects in the background and overwrites `project.assets.json` with the NuGet folders of Windows
(a build in the middle of it stops with "Unable to find fallback package folder"), cannot get in the way. `MAKE_INPLACE=1` builds in place.

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

Every `.tar.gz` (`crossbible-<Version>-<runtime>.tar.gz`, one for each runtime, and `crossbible-<Version>-noarch.tar.gz`) holds the program in the folder `crossbible` and
next to it an installer, `install.sh` (`sudo ./install.sh` into `/usr/local`, `--user` into `~/.local`, `--prefix <folder>`, and `--uninstall`; the sources of it and of the
two READMEs are in `src/install`). The native ones (the AOT builds) hold the program of one processor, which needs no .NET runtime. The generic one, `-noarch`, is the .NET program,
published without AOT (the `.dll`, and the native libraries of all the Linux runtimes): it runs on every processor that has the .NET runtime 11 or later, which `install.sh` looks for.

Without Windows, `sh make.sh [msbuild arguments]` does the same in a Linux shell.

# Alpine Linux

Alpine uses musl, so its packages (`crossbible-<Version>-r0-linux-<arch>.apk`, for x86_64, aarch64 and armv7) are made from the musl runtimes. Native AOT for musl
needs a musl toolchain, and the package is made by `apk mkpkg` (apk-tools 3, Alpine 3.21 or later), so the build runs in Alpine (`make.bat alpine`, or in an
Alpine machine or container, with `apk add tar clang build-base zlib-dev rsync` and the .NET SDK for musl):

    sh make.sh -p:FileVersion=<Version>

All three architectures are built. The ARM ones are cross-built on a x64 Alpine: `make.sh` makes a root file system of Alpine for each of them in
`/opt/sysroot/<arch>` (`src/scripts/MakeAlpineSysroot.sh`: the C library of the target, its start files, gcc's runtime, libstdc++, zlib) and Native AOT links with `lld`
against it. `-p:AlpineAllArchs=false` builds only the architecture of the machine; with `-p:SkipPublish=true` the packages are made from the builds in the `publish` folder
that other machines made.

Install a package with `apk add --allow-untrusted crossbible-<Version>-r0-linux-<arch>.apk` (it is not signed).
