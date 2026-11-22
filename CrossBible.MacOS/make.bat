@echo off
rem Builds CrossBible.app for macOS (Apple silicon: arm64, and Intel: x64) in WSL (see README.md):
rem   make.bat           both
rem   make.bat arm64     only Apple silicon
rem   make.bat x64       only Intel
rem Native AOT only builds on a Mac, so here the app is self-contained with the .NET runtime (JIT), packed in a .tar.gz: unpack it on the Mac, and sign it there
rem (or build on the Mac with make.sh, which makes the AOT build and the .dmg). It needs the .NET SDK in the WSL distribution.
setlocal
set "DISTRO="
for /f "tokens=1,* delims==" %%a in ('powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\CrossBible.LinuxPackages\src\scripts\WslSetup.ps1" -Distro Ubuntu ^| findstr /b /c:"DISTRO="') do set "DISTRO=%%b"
if "%DISTRO%"=="" (
    echo Could not set up the WSL distribution.
    exit /b 1
)
echo === building in WSL distribution %DISTRO% ===
wsl -d %DISTRO% -u root --cd "%~dp0." --exec sh ./make.sh %*
exit /b %errorlevel%
