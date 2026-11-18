# Fills in the control file of one Debian package folder: ConfigureDebian.ps1 <version> <folder> <architecture>
# <architecture> is the Debian one (amd64, arm64 or armhf).
param([string]$version, [string]$folder, [string]$architecture)

$control_file = Join-Path $folder "DEBIAN/control"
$size = ((Get-ChildItem $folder -Recurse -File | Measure-Object -Sum Length).Sum / 1024).ToString("f0")
$control = Get-Content $control_file -Raw
$control = [Regex]::Replace($control, "^Installed-Size:.*$`n?", "", [Text.RegularExpressions.RegexOptions]::Multiline)
$control = [Regex]::Replace($control, "(?<=^Version:\s*)[0-9.:]*?$", $version, [Text.RegularExpressions.RegexOptions]::Multiline)
$control = [Regex]::Replace($control, "(?<=^Architecture:)[^\r\n]*", " $architecture", [Text.RegularExpressions.RegexOptions]::Multiline)
$control = [String]::Concat($control.TrimEnd(), "`nInstalled-Size: $size`n")
$Utf8NoBomEncoding = New-Object System.Text.UTF8Encoding $False
[IO.File]::WriteAllText((Resolve-Path $control_file).Path, $control, $Utf8NoBomEncoding)
