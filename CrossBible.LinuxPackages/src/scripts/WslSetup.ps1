# Makes sure the WSL distribution that builds the packages is there: WslSetup.ps1 -Distro Ubuntu|Alpine
#   Ubuntu: installed from the Microsoft Store catalog of WSL (wsl --install), with root as its default user (no question for a user name).
#   Alpine: it is not in that catalog, so its minimal root file system is downloaded from alpinelinux.org and imported (wsl --import).
# An Ubuntu that is installed already is used (Ubuntu, or Ubuntu-<version>: the one with the highest version), not a second one made.
# The name of the distribution to use is the last line of the output (DISTRO=<name>); the exit code is 0 when it can be used.
param([Parameter(Mandatory)][ValidateSet('Ubuntu', 'Alpine')][string]$Distro)

$ErrorActionPreference = 'Stop'
$env:WSL_UTF8 = '1'   # wsl.exe writes its lists in UTF-16 otherwise

if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    Write-Error 'WSL is not installed. Run "wsl --install --no-distribution" in an administrator terminal, restart Windows, and try again.'
}

function Find-Distro([string]$kind)
{
    $installed = @(& wsl.exe --list --quiet 2>$null) | ForEach-Object { "$_".Trim() } | Where-Object { $_ }
    if ($kind -eq 'Alpine') { return $installed | Where-Object { $_ -eq 'Alpine' } | Select-Object -First 1 }
    # Ubuntu, Ubuntu-24.04, Ubuntu-26.04 (not Ubuntu-26.04-Test and the like); the newest first.
    return $installed | Where-Object { $_ -match '^Ubuntu(-\d+\.\d+)?$' } |
        Sort-Object { if ($_ -match '(\d+)\.(\d+)') { [int]$Matches[1] * 100 + [int]$Matches[2] } else { 0 } } -Descending |
        Select-Object -First 1
}

$found = Find-Distro $Distro
if ($found) {
    Write-Host "WSL distribution $found found."
    Write-Output "DISTRO=$found"
    exit 0
}

Write-Host "WSL distribution $Distro is not installed. Installing it..."

if ($Distro -eq 'Ubuntu') {
    & wsl.exe --install -d Ubuntu --no-launch
    if ($LASTEXITCODE -ne 0) { Write-Error 'wsl --install failed (an administrator terminal and a restart of Windows may be needed).' }

    # "ubuntu install --root" sets the distribution up with root as the default user, without the question for a user name and a password.
    $launcher = Get-Command ubuntu.exe -ErrorAction SilentlyContinue
    if (-not $launcher) { Write-Error 'ubuntu.exe is not found: start Ubuntu once from the Start menu, then run make.bat again.' }
    & $launcher.Source install --root
    if ($LASTEXITCODE -ne 0) { Write-Error 'ubuntu install --root failed.' }
}
else {
    $folder = Join-Path $env:LOCALAPPDATA 'CrossBible\wsl\Alpine'
    New-Item -ItemType Directory -Force $folder | Out-Null

    $base = 'https://dl-cdn.alpinelinux.org/alpine/latest-stable/releases/x86_64'
    $releases = (Invoke-WebRequest "$base/latest-releases.yaml" -UseBasicParsing).Content
    $name = [regex]::Match($releases, 'file:\s*(alpine-minirootfs-[0-9.]+-x86_64\.tar\.gz)').Groups[1].Value
    if (-not $name) { Write-Error 'The minimal root file system of Alpine is not found in latest-releases.yaml.' }

    $archive = Join-Path $env:TEMP $name
    Write-Host "Downloading $base/$name"
    Invoke-WebRequest "$base/$name" -OutFile $archive -UseBasicParsing
    & wsl.exe --import Alpine $folder $archive --version 2
    if ($LASTEXITCODE -ne 0) { Write-Error 'wsl --import failed.' }
    Remove-Item $archive -ErrorAction SilentlyContinue
}

$found = Find-Distro $Distro
if (-not $found) { Write-Error "$Distro is not in the list of WSL distributions after the installation." }
Write-Host "WSL distribution $found installed."
Write-Output "DISTRO=$found"
