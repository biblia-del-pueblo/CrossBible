# Makes sure the WSL distribution that builds the packages is there: WslSetup.ps1 -Distro Ubuntu|Alpine
# Native AOT programs are linked against the C library of the machine that builds them, and they do not run on a system whose C library is older. So the packages are
# built on the oldest system that is still supported: the programs then run on it and on all the newer ones.
#   Ubuntu: Ubuntu 22.04 (the oldest Ubuntu with glibc that the packages support), installed from the Microsoft Store catalog of WSL (wsl --install), with root as its
#           default user (no question for a user name). Other Ubuntu versions that are installed are not used.
#   Alpine: the oldest Alpine release that is supported (by the end of support dates in https://alpinelinux.org/releases.json, and not older than 3.21: the packages are
#           made by "apk mkpkg" of apk-tools 3). It is not in the catalog of WSL, so its minimal root file system is downloaded from alpinelinux.org and imported
#           (wsl --import) as Alpine-<version>, for example Alpine-3.22. An Alpine of another version (or "Alpine") that is installed is not used.
# The name of the distribution to use is the last line of the output (DISTRO=<name>); the exit code is 0 when it can be used.
param([Parameter(Mandatory)][ValidateSet('Ubuntu', 'Alpine')][string]$Distro)

$ErrorActionPreference = 'Stop'
$env:WSL_UTF8 = '1'   # wsl.exe writes its lists in UTF-16 otherwise

if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    Write-Error 'WSL is not installed. Run "wsl --install --no-distribution" in an administrator terminal, restart Windows, and try again.'
}

$UbuntuRelease = '22.04'
$UbuntuName = "Ubuntu-$UbuntuRelease"
$AlpineMinimum = [version]'3.21'   # apk-tools 3 (apk mkpkg), which makes the packages
$AlpineFallback = '3.22'           # when the list of releases cannot be read

# The oldest Alpine release that is supported today, as "3.22".
function Get-OldestSupportedAlpine()
{
    try {
        $now = Get-Date
        $oldest = (Invoke-RestMethod 'https://alpinelinux.org/releases.json' -UseBasicParsing).release_branches |
            Where-Object { $_.rel_branch -match '^v(\d+\.\d+)$' -and $_.eol_date -and ([datetime]$_.eol_date) -gt $now } |
            ForEach-Object { [version]($_.rel_branch.Substring(1)) } |
            Where-Object { $_ -ge $AlpineMinimum } |
            Sort-Object | Select-Object -First 1
        if ($oldest) { return "$($oldest.Major).$($oldest.Minor)" }
    }
    catch { Write-Host "The list of the Alpine releases could not be read ($($_.Exception.Message))." }
    Write-Host "Using Alpine $AlpineFallback."
    return $AlpineFallback
}

$AlpineRelease = if ($Distro -eq 'Alpine') { Get-OldestSupportedAlpine } else { $null }
$AlpineName = "Alpine-$AlpineRelease"
if ($Distro -eq 'Alpine') { Write-Host "The oldest supported Alpine is $AlpineRelease." }

function Find-Distro([string]$kind)
{
    $installed = @(& wsl.exe --list --quiet 2>$null) | ForEach-Object { "$_".Trim() } | Where-Object { $_ }
    if ($kind -eq 'Alpine') { return $installed | Where-Object { $_ -eq $AlpineName } | Select-Object -First 1 }
    return $installed | Where-Object { $_ -eq $UbuntuName } | Select-Object -First 1
}

$found = Find-Distro $Distro
if ($found) {
    Write-Host "WSL distribution $found found."
    Write-Output "DISTRO=$found"
    exit 0
}

Write-Host "WSL distribution $Distro is not installed. Installing it..."

if ($Distro -eq 'Ubuntu') {
    & wsl.exe --install -d $UbuntuName --no-launch
    if ($LASTEXITCODE -ne 0) { Write-Error 'wsl --install failed (an administrator terminal and a restart of Windows may be needed).' }

    # "ubuntu2204 install --root" sets the distribution up with root as the default user, without the question for a user name and a password. Without the launcher
    # (the newer WSL has none) the distribution is still there, and make.bat starts it as root (wsl -u root).
    $launcher = Get-Command "ubuntu$($UbuntuRelease -replace '\.', '').exe" -ErrorAction SilentlyContinue
    if ($launcher) {
        & $launcher.Source install --root
        if ($LASTEXITCODE -ne 0) { Write-Error "$($launcher.Name) install --root failed." }
    }
}
else {
    $folder = Join-Path $env:LOCALAPPDATA "CrossBible\wsl\$AlpineName"
    New-Item -ItemType Directory -Force $folder | Out-Null

    # The latest minimal root file system of the release (3.22.2, not 3.22.0): it has the security fixes.
    $base = "https://dl-cdn.alpinelinux.org/alpine/v$AlpineRelease/releases/x86_64"
    $releases = (Invoke-WebRequest "$base/latest-releases.yaml" -UseBasicParsing).Content
    # The server sends the file with a type that PowerShell does not take for text: Content is then an array of bytes.
    if ($releases -is [byte[]]) { $releases = [Text.Encoding]::UTF8.GetString($releases) }
    $name =[regex]::Match($releases, 'file:\s*(alpine-minirootfs-[0-9.]+-x86_64\.tar\.gz)').Groups[1].Value
    if (-not $name) { Write-Error "The minimal root file system of Alpine $AlpineRelease is not found in latest-releases.yaml." }

    $archive = Join-Path $env:TEMP $name
    Write-Host "Downloading $base/$name"
    Invoke-WebRequest "$base/$name" -OutFile $archive -UseBasicParsing
    & wsl.exe --import $AlpineName $folder $archive --version 2
    if ($LASTEXITCODE -ne 0) { Write-Error 'wsl --import failed.' }
    Remove-Item $archive -ErrorAction SilentlyContinue
}

$found = Find-Distro $Distro
if (-not $found) { Write-Error "$Distro is not in the list of WSL distributions after the installation." }
Write-Host "WSL distribution $found installed."
Write-Output "DISTRO=$found"
