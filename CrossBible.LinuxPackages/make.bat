@echo off
rem Builds the Linux packages in WSL (see README.md):
rem   make.bat [msbuild arguments]          both: Ubuntu (Debian, Fedora and Arch Linux packages), then Alpine (Alpine packages)
rem   make.bat ubuntu [msbuild arguments]   only the packages of Ubuntu
rem   make.bat alpine [msbuild arguments]   only the packages of Alpine (the musl builds only work in Alpine)
rem The WSL distribution is installed when it is not there (an Ubuntu that is installed already is used), and so are the tools that are missing in it
rem (make.sh does it).
setlocal

set "WHAT=both"
set "ARGS=%*"
if /i "%~1"=="ubuntu" (
    set "WHAT=ubuntu"
    set "ARGS=%ARGS:~7%"
)
if /i "%~1"=="alpine" (
    set "WHAT=alpine"
    set "ARGS=%ARGS:~7%"
)

if /i not "%WHAT%"=="alpine" (
    call :build Ubuntu
    if errorlevel 1 exit /b 1
)
if /i not "%WHAT%"=="ubuntu" (
    call :build Alpine
    if errorlevel 1 exit /b 1
)
exit /b 0

:build
rem %1 is the kind of distribution. WslSetup.ps1 makes it when it is not there and says which one to use in its last line (DISTRO=<name>).
set "DISTRO="
for /f "tokens=1,* delims==" %%a in ('powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0src\scripts\WslSetup.ps1" -Distro %1 ^| findstr /b /c:"DISTRO="') do set "DISTRO=%%b"
if "%DISTRO%"=="" (
    echo Could not set up the WSL distribution for %1.
    exit /b 1
)
echo === %1: building in WSL distribution %DISTRO% ===
rem root: the distribution may have just been made, without a user. MAKE_YES: the tools that are missing are installed without a question.
wsl -d %DISTRO% -u root --cd "%~dp0." --exec env MAKE_YES=1 sh ./make.sh %ARGS%
exit /b %errorlevel%
