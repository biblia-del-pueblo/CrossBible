@echo off
rem Builds the Microsoft Store package of CrossBible for x64 (amd64) and ARM64: one .msixbundle and the .msixupload for Partner Center.
rem
rem   make.bat           builds (Release)
rem   make.bat clean     deletes everything that was built before, then builds (use it after the identity or version in
rem                      Package.appxmanifest changed: an older package of the other architecture would not fit the bundle)
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

if /i "%~1"=="clean" (
    echo Deleting the packages and layouts that were built before...
    for %%a in (x64 arm64) do (
        if exist "%HERE%bin\%%a" rmdir /s /q "%HERE%bin\%%a"
        if exist "%HERE%obj\%%a" rmdir /s /q "%HERE%obj\%%a"
    )
) else (
    rem The layout for the Store upload is not made again by itself when the manifest changed: a stale one fails the build.
    if exist "%HERE%bin\x64\Release\Upload" rmdir /s /q "%HERE%bin\x64\Release\Upload"
    if exist "%HERE%obj\x64\Release\Upload" rmdir /s /q "%HERE%obj\x64\Release\Upload"
    if exist "%HERE%obj\x64\Release\PackageUploadLayout" rmdir /s /q "%HERE%obj\x64\Release\PackageUploadLayout"
)

rem Platform x64 builds the bundle: AppxBundlePlatforms in CrossBible.Msix.wapproj has x64 and arm64, and the app is built for both.
"%MSBUILD%" "%HERE%CrossBible.Msix.wapproj" -restore -p:Configuration=Release -p:Platform=x64 -verbosity:minimal -nologo
if errorlevel 1 (
    echo.
    echo The build failed.
    exit /b 1
)

echo.
echo Done. The packages are in %HERE%AppPackages\
dir /b "%HERE%AppPackages\*.msixupload" "%HERE%AppPackages\*\*.msixbundle" 2>nul
exit /b 0
