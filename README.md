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
of all of them, and of the older versions too. The folder is not in git.

### Releases

A push to the branch `publish` makes a [GitHub release](.github/workflows/release.yml): it takes the files of the branch that begin with `crossbible` (the packages), finds the
newest version in their names and releases the files of that version as `v<version>` (when that release is there already, its files are replaced). So the branch only
has to hold the packages. Because the packages have nothing to do with the history of the sources, it is best a branch of its own, with a single commit:

```sh
git worktree add --orphan -b publish ../CrossBible-publish   # once (Git 2.42 or later)
cp publish/crossbible-1.0.4-* ../CrossBible-publish/         # the files of the version that is released
cd ../CrossBible-publish
git add -A && git commit -m "Cross Bible 1.0.4" && git push --force origin publish
```

GitHub does not accept a file of more than 100 MB in a branch. The workflow can also be started by hand (Actions > Release the publish branch), as a draft that you look at
before you publish it.

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
