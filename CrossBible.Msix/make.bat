rem Builds the Microsoft Store package of CrossBible for x64 (amd64) and ARM64: one .msixbundle and the .msixupload for Partner Center.
rem The code is compiled to machine code ahead of time (native AOT): no .NET runtime in the package, and a smaller start-up.
rem
rem   make.bat           builds (Release), native AOT
rem   make.bat r2r       builds with ReadyToRun instead (the code is precompiled, but the .NET runtime is in the package: larger, and it needs no C++ tools)
rem   make.bat clean     deletes everything that was built before, then builds (use it after the identity or version in
rem                      Package.appxmanifest changed: an older package of the other architecture would not fit the bundle)
rem   (the words can be given together: make.bat clean r2r)
rem
rem Native AOT is linked by the C++ tools of Visual Studio: for the x64 package "MSVC build tools for x64/x86" and for the ARM64 package "MSVC build tools
rem for ARM64/ARM64EC" (Visual Studio Installer > Modify > Individual components), with the Windows SDK.
rem
rem The packaging targets belong to Visual Studio, so the project is built with the MSBuild of Visual Studio, not with "dotnet build".
rem The result is in AppPackages\

setlocal
set "HERE=%~dp0"
set "ROOT=%HERE%.."

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo vswhere.exe was not found: Visual Studio is not installed.
    exit /b 1
)

set "MSBUILD="
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do if not defined MSBUILD set "MSBUILD=%%i"
if not defined MSBUILD (
    echo MSBuild was not found in Visual Studio.
    exit /b 1
)

set "CLEAN="
set "MODE=aot"
for %%o in (%*) do (
    if /i "%%o"=="clean" set "CLEAN=1"
    if /i "%%o"=="r2r" set "MODE=r2r"
)

if "%MODE%"=="aot" (
    rem Fail at once, with what to do, instead of after minutes of work when the linker cannot find the libraries of the architecture.
    set "CPPX64="
    set "CPPARM64="
    for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "CPPX64=%%i"
    for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath`) do set "CPPARM64=%%i"
    if not defined CPPX64 (
        echo Native AOT needs the C++ tools of Visual Studio for x64: install "MSVC build tools for x64/x86 (latest)" with the Visual Studio Installer, or build with: make.bat r2r
        exit /b 1
    )
    if not defined CPPARM64 (
        echo Native AOT for the ARM64 package needs the C++ tools of Visual Studio for ARM64: install "MSVC build tools for ARM64/ARM64EC (latest)" with the Visual Studio Installer, or build with: make.bat r2r
        exit /b 1
    )
)

if defined CLEAN (
    echo Deleting the packages and layouts that were built before...
    for %%a in (x64 arm64) do (
        if exist "%HERE%bin\%%a" rmdir /s /q "%HERE%bin\%%a"
        if exist "%HERE%obj\%%a" rmdir /s /q "%HERE%obj\%%a"
    )
) else (
    rem The layout for the Store upload is not made again by itself when the manifest changed: a stale one fails the build, or worse, is packed with its old
    rem version (an ARM64 package with the version of the last release in a bundle of the new one: Partner Center refuses it as a duplicate of the published
    rem package). It is deleted for both architectures, the ARM64 layout is made for the bundle too.
    for %%a in (x64 arm64) do (
        if exist "%HERE%bin\%%a\Release\Upload" rmdir /s /q "%HERE%bin\%%a\Release\Upload"
        if exist "%HERE%obj\%%a\Release\Upload" rmdir /s /q "%HERE%obj\%%a\Release\Upload"
        if exist "%HERE%obj\%%a\Release\PackageUploadLayout" rmdir /s /q "%HERE%obj\%%a\Release\PackageUploadLayout"
    )
)

rem Platform x64 builds the bundle: AppxBundlePlatforms in CrossBible.Msix.wapproj has x64 and arm64, and the app is built for both.
rem How the code is compiled is set here, besides CrossBible.Desktop.csproj, so that the package is made this way whatever that file says: UseAot=true is
rem native AOT (see the switch in that file), PublishReadyToRun=true is ReadyToRun.
if "%MODE%"=="aot" (
    echo Building with native AOT...
    "%MSBUILD%" "%HERE%CrossBible.Msix.wapproj" -restore -p:Configuration=Release -p:Platform=x64 -p:UseAot=true -verbosity:minimal -nologo
) else (
    echo Building with ReadyToRun...
    "%MSBUILD%" "%HERE%CrossBible.Msix.wapproj" -restore -p:Configuration=Release -p:Platform=x64 -p:PublishReadyToRun=true -verbosity:minimal -nologo
)
if errorlevel 1 (
    echo.
    echo The build failed.
    exit /b 1
)

rem The packaging targets name the files after the project (CrossBible.Msix_<version>_...): rename the results to crossbible_<version>_..., lowercase. (Renaming
rem the project itself would break the Store upload layout.) Only the outer files are renamed, the packages inside the bundle keep their names.
powershell -NoProfile -Command "Get-ChildItem -LiteralPath '%HERE%AppPackages' -Recurse -File | Where-Object { $_.Extension -in '.msixupload','.msixbundle','.appxsym' } | ForEach-Object { $n = ($_.Name -replace '^CrossBible\.Msix_','crossbible_').ToLowerInvariant(); if ($n -cne $_.Name) { Move-Item -LiteralPath $_.FullName -Destination (Join-Path $_.DirectoryName $n) -Force } }"

echo.
echo Done. The packages are in %HERE%AppPackages\
dir /b "%HERE%AppPackages\*.msixupload" "%HERE%AppPackages\*\*.msixbundle" 2>nul
exit /b 0
