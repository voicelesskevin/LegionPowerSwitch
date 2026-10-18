param([string]$Version='0.2.0-alpha.1')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-z]+\.\d+)?$'){throw 'Invalid version.'}
$dist=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$files='LegionPowerSwitch.exe','app-icon.png','app.ico','Install.ps1','Install.cmd','README.md','LICENSE','THIRD-PARTY-NOTICES.md'
$paths=@($files | ForEach-Object { $path=Join-Path $PSScriptRoot $_;if(!(Test-Path -LiteralPath $path)){throw "Missing package file: $_"};$path })
$zip=Join-Path $dist "LegionPowerSwitch-v$Version-win-x64.zip"
Compress-Archive -LiteralPath $paths -DestinationPath $zip -Force
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries=@($archive.Entries | ForEach-Object {$_.FullName})
    if($entries.Count -ne $files.Count -or @($entries | Where-Object {$_ -notin $files}).Count){throw 'Unexpected file in package.'}
} finally {$archive.Dispose()}
Get-Item -LiteralPath $zip | Select-Object FullName,Length
