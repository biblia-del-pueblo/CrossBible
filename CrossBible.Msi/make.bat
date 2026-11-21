@echo off
rem Builds the Windows installers (.msi) of CrossBible: one for amd64 (x64) and one for arm64, both with the native AOT build of CrossBible.Desktop
rem (no .NET runtime is needed to run them).
rem
rem   make.bat           builds both
rem   make.bat x64       builds only the amd64 installer
rem   make.bat arm64     builds only the arm64 installer
rem
rem Native AOT is linked by the C++ tools of Visual Studio: for the amd64 installer "MSVC build tools for x64/x86" and for the arm64 installer "MSVC build tools
rem for ARM64/ARM64EC" (Visual Studio Installer > Modify > Individual components), with the Windows SDK. The installer itself is built by the WiX SDK
rem (CrossBible.Msi.wixproj), which dotnet restores as a NuGet package.
rem The result is in bin\<platform>\crossbible-<version>-windows-<platform>.msi, and each installer is copied to the publish folder at the root of the repository, with
rem the packages of the other make scripts.
setlocal
set "HERE=%~dp0"
set "PUBLISH=%HERE%..\publish"

set "PLATFORMS=x64 arm64"
if /i "%~1"=="x64" set "PLATFORMS=x64"
if /i "%~1"=="arm64" set "PLATFORMS=arm64"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo vswhere.exe was not found: Visual Studio is not installed.
    exit /b 1
)

rem Fail at once, with what to do, instead of after minutes of work when the linker cannot find the libraries of the architecture.
for %%p in (%PLATFORMS%) do (
    set "TOOLS="
    if /i "%%p"=="x64" for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "TOOLS=%%i"
    if /i "%%p"=="arm64" for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath`) do set "TOOLS=%%i"
    if not defined TOOLS (
        if /i "%%p"=="x64" echo Native AOT needs the C++ tools of Visual Studio for x64: install "MSVC build tools for x64/x86 ^(latest^)" with the Visual Studio Installer.
        if /i "%%p"=="arm64" echo Native AOT for the arm64 installer needs the C++ tools of Visual Studio for ARM64: install "MSVC build tools for ARM64/ARM64EC ^(latest^)" with the Visual Studio Installer.
        exit /b 1
    )
)

for %%p in (%PLATFORMS%) do (
    echo.
    echo Building the %%p installer...
    dotnet build "%HERE%CrossBible.Msi.wixproj" -c Release -p:InstallerPlatform=%%p -nologo -verbosity:minimal
    if errorlevel 1 (
        echo.
        echo The build of the %%p installer failed.
        exit /b 1
    )
    call :publish %%p
    if errorlevel 1 exit /b 1
)

echo.
echo Done. The installers are in %HERE%bin\^<platform^>\ and in %PUBLISH%\:
dir /b /s "%HERE%bin\*.msi"
exit /b 0

rem The installer of a platform (%1) also goes into the publish folder at the root of the repository, where the packages of all the make scripts are collected (all of
rem them in the folder itself, no subfolders). The newest one: bin\<platform> may still have the ones of older versions. (A subroutine: inside the parentheses of the
rem loop %INSTALLER% would be read before it is set.)
:publish
set "INSTALLER="
for /f "usebackq delims=" %%f in (`dir /b /a-d /o-d "%HERE%bin\%1\*.msi" 2^>nul`) do if not defined INSTALLER set "INSTALLER=%%f"
if not defined INSTALLER (
    echo No installer was made for %1.
    exit /b 1
)
if not exist "%PUBLISH%" mkdir "%PUBLISH%"
copy /y "%HERE%bin\%1\%INSTALLER%" "%PUBLISH%\" >nul
if errorlevel 1 (
    echo The installer could not be copied to %PUBLISH%.
    exit /b 1
)
exit /b 0
