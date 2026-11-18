# Fills in the spec file of one Fedora package folder: ConfigureFedora.ps1 <version> <folder> <architecture>
# <architecture> is the RPM one (x86_64, aarch64 or armv7hl).
param([string]$version, [string]$folder, [string]$architecture)

$spec_file = Join-Path $folder "SPECS/crossbible.spec"
$spec = Get-Content $spec_file -Raw
$spec = [Regex]::Replace($spec, "(?<=^Version:\s*)[0-9.:]*?$", $version, [Text.RegularExpressions.RegexOptions]::Multiline)
$spec = [Regex]::Replace($spec, "(?<=^BuildArch:)[^\r\n]*", " $architecture", [Text.RegularExpressions.RegexOptions]::Multiline)
$Utf8NoBomEncoding = New-Object System.Text.UTF8Encoding $False
[IO.File]::WriteAllText((Resolve-Path $spec_file).Path, $spec, $Utf8NoBomEncoding)
