# Cross Bible

**Cross Bible** is a free, open source, cross platform Bible reader. It runs on Windows, Linux, macOS, Android and iOS, downloads free Bibles in many languages, and shows the text in columns, like a printed Bible.

Website: [www.crossbible.org](https://www.crossbible.org)

## Features

- **Columns like a print Bible**: the text is set in justified columns that you page through sideways, with chapter headings, verse numbers, poetry and
  footnotes. A single column on narrow screens.
- **Free Bibles in many languages**: a built-in library downloads Bibles from [ebible.org](https://ebible.org), [crosswire.org](https://crosswire.org) and
  Biblia Libre, with their Strong's dictionaries. The Bibles are stored on your device and work offline.
- **Strong's numbers**: tap a word to see its Hebrew or Greek root and the dictionary entry, and follow the links from entry to entry.
- **Hyphenation** for about 30 languages (German, English, Spanish, French, Russian, Greek, Hindi, Serbian in Latin and Cyrillic, ...), so that justified lines
  do not get wide gaps.
- **Study tools**: search the words of a Bible, footnote popups, history with back and forward, the words of Jesus (optionally marked) and verse numbers
  (optionally in green).
- **43 languages for the app** itself, including right-to-left languages (Arabic, Hebrew, Persian, Urdu, Pashto).
- **Readable**: dark and light theme, your choice of font and font size for the text, and separate fonts for Greek and Hebrew, interface scaling.

## Download and install

Packages for every platform are built from this repository (see [Building](#building)):

| Platform | Package | Built by |
|---|---|---|
| Windows (x64, ARM64) | `.msi` installer, native AOT | [CrossBible.Msi](CrossBible.Msi) |
| Windows (Microsoft Store) | `.msixbundle` for x64 and ARM64 | [CrossBible.Msix](CrossBible.Msix) |
| macOS (Apple silicon, Intel) | `Cross Bible.app` in a `.dmg` / `.zip` | [CrossBible.MacOS](CrossBible.MacOS) |
| Linux | `.deb`, `.rpm`, Arch Linux, Alpine (`.apk`)  and `.tar.gz` for x64, ARM64 and 32 bit ARM | [CrossBible.LinuxPackages](CrossBible.LinuxPackages) |
| Android | `.apk` / `.aab` | [CrossBible.Android](CrossBible.Android) |
| iOS | app | [CrossBible.iOS](CrossBible.iOS) |
| Browser (WebAssembly) | static web site | [CrossBible.Browser](CrossBible.Browser) |

The Windows, Linux and macOS packages contain the program compiled to machine code (native AOT, on macOS only when built on a Mac), so they need no .NET runtime.

## Building

You need the [.NET SDK 11.0](https://dotnet.microsoft.com/download). To run the desktop app from the source:

```sh
git clone --recurse-submodules https://github.com/simonegli8/CrossBible.git
cd CrossBible
dotnet run --project CrossBible.Desktop
```

The packages:

| Package | Command | Notes |
|---|---|---|
| Windows installers | `CrossBible.Msi\make.bat` | needs the C++ tools of Visual Studio for x64 and ARM64 (native AOT) and the [WiX Toolset](https://wixtoolset.org) 7 |
| Microsoft Store | `CrossBible.Msix\make.bat` | needs Visual Studio |
| Linux packages | `CrossBible.LinuxPackages\make.bat` (Windows, builds in WSL) or `sh make.sh` (Linux) | see its [README](CrossBible.LinuxPackages/README.md) |
| macOS | `sh CrossBible.MacOS/make.sh` (on a Mac) or `CrossBible.MacOS\make.bat` | see its [README](CrossBible.MacOS/README.md) |
| Android | `CrossBible.Android\make.bat` | needs the Android workload of .NET; signs with an upload key that is made on the first run, in `CrossBible.Android\publish` (never commit it) |

Native AOT only builds for the operating system it runs on: the Windows packages on Windows, the Linux packages on Linux (WSL on Windows), and the macOS app on a Mac.

All the make scripts put their packages into the `publish` folder at the root of the repository, directly in the folder (it has no subfolders), so it holds the packages
of all of them, and of the older versions too. The folder is not in git. The release workflow below builds the packages the same way, on GitHub.

### Releases

A push to the branch `publish` (for example `git push origin main:publish`, after you raised `Version` in `Directory.Build.props`) builds the packages and makes a
[GitHub release](.github/workflows/release.yml) of them, `v<Version>` (when that release is there already, its files are replaced):

| Built on | With | Packages |
|---|---|---|
| Windows | `CrossBible.Msi\make.bat` | the installers (x64 and ARM64) |
| macOS 15 (the oldest that GitHub has) | `CrossBible.MacOS/make.sh` | the app (Apple silicon and Intel), signed ad hoc |
| Ubuntu 22.04 | `CrossBible.LinuxPackages/make.sh` | Debian, Fedora, Arch Linux packages and `.tar.gz` |
| Alpine 3.23 (in Docker) | the same script | Alpine packages and the `.tar.gz` of the musl builds |
| Windows | `CrossBible.Android\make.bat` | the three `.apk` and the `.aab`, signed with your upload key (a secret, see below) |
| Windows | `CrossBible.Msix\make.bat` | the Store package (`.msixupload`, for Partner Center: it is uploaded there, not installed) |

The packages are built on the oldest systems that are still supported (not on the `-latest` images): a native AOT program does not run on a system whose C library
is older than the one that built it, and the macOS app is built against the system of the build machine. The release is only made when all of them were built, and every package has to have the version of the release.

The Store package has the version of `CrossBible.Msix\Package.appxmanifest` (the Store wants its own, raised for every upload), which has to be the `Version` of
`Directory.Build.props` with a `.0` added (`1.0.6` is `1.0.6.0`): the build stops, saying so, when it is not. Start the workflow by hand (Actions > Release) to make a draft,
which you look at before you publish it.

The Android packages are signed with your upload key, which is not in the repository: it is two secrets of the repository (Settings > Secrets and variables > Actions), and
the release stops, saying so, while they are not set. The key is the one that `CrossBible.Android\make.bat` makes on its first run, in `CrossBible.Android\publish` (the
same key as for the packages that you build yourself, so the updates of the app in the Play Store fit). With the [GitHub CLI](https://cli.github.com):

```powershell
gh secret set ANDROID_KEYSTORE_BASE64 --body ([Convert]::ToBase64String([IO.File]::ReadAllBytes("CrossBible.Android\publish\upload.keystore")))
gh secret set ANDROID_KEYSTORE_PASSWORD   # asks for the value: CB_PASS in CrossBible.Android\publish\signing.cmd
```

The password must only have letters and digits (the one that `make.bat` makes has). Keep a copy of the keystore and the password somewhere else as well: a secret of GitHub cannot
be read again.

## Repository

| Folder | What it is |
|---|---|
| `CrossBible.Library` | the app: views, Bible text control, downloads, hyphenation, translations (`Resources`) |
| `CrossBible.Desktop`, `CrossBible.Android`, `CrossBible.iOS`, `CrossBible.Browser` | the heads for each platform |
| `BibleMarkdown` | the Bible Markdown format and `bibmark`, the tool that converts USFM, Zefania XML, e-Sword and more to it (a submodule) |
| `CrossBible.Msi`, `CrossBible.Msix`, `CrossBible.MacOS`, `CrossBible.LinuxPackages` | packaging |
| `CrossBible.Test` | a test app for the text control |

It is written in C# with [Avalonia UI](https://avaloniaui.net).

## Contributing

Issues and pull requests are welcome. Translations of the app are `Strings.<culture>.resx` files in
[CrossBible.Library/Resources](CrossBible.Library/Resources): copy `Strings.resx`, translate the values, and add the language to the language list of the
settings view.

## License

[MIT](LICENSE) © 2026 Biblia del Pueblo. The Bible texts that the app downloads have the licenses of their publishers; the hyphenation patterns are from the
[TeX hyphenation patterns](https://github.com/hyphenation/tex-hyphen) project.
