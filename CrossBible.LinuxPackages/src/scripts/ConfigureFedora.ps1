cd .\Fedora
$size = ((ls -r | measure -s Length).Sum / 1024).ToString("f0")
$version = $args[0]
$spec = Get-Content .\SPECS\hostpanelpro.spec -Raw
$spec = [Regex]::Replace($spec, "(?<=^Version:\s*)[0-9.:]*?$", $version, [Text.RegularExpressions.RegexOptions]::Multiline)
[Environment]::CurrentDirectory = (Get-Location).Path
$Utf8NoBomEncoding = New-Object System.Text.UTF8Encoding $False
[IO.File]::WriteAllText(".\SPECS\hostpanelpro.spec", $spec, $Utf8NoBomEncoding)
cd ..