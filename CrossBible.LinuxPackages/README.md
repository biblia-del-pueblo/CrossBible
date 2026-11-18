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
operating system, so the packages are built on Linux, in WSL on Windows (`make.bat` calls `make.sh` in WSL). There are packages for three architectures:

| .NET runtime | Debian (.deb) | Fedora (.rpm) |
|--------------|---------------|---------------|
| linux-x64    | amd64         | x86_64        |
| linux-arm64  | arm64         | aarch64       |
| linux-arm    | armhf         | armv7hl       |

Install in the WSL distro: the .NET SDK, `clang`, `zlib1g-dev`, `rpm`, PowerShell (`pwsh`), and the cross compiler for the ARM
architectures (for linux-arm64: `gcc-aarch64-linux-gnu` and `binutils-aarch64-linux-gnu`; for linux-arm: the arm ones). Then run

    sh make.sh -p:FileVersion=<Version>

(`make.sh` checks that the tools are installed and runs `dotnet msbuild LinuxPackages.proj`.)

The packages (`crossbible_<Version>-1_<arch>.deb`, `crossbible-<Version>-1.<arch>.rpm`) and a `.tar.gz` of each architecture are written to the bin
folder. Each package is put together in its own folder in staging (Debian and Fedora are the templates), the AOT build of each runtime is in
`..\publish`. `-t:PublishDesktopAot` only publishes the `.tar.gz` files, with the musl ones (Alpine) too.
