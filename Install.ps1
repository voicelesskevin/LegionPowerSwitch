param([string]$Destination,[switch]$NoShortcuts)
$ErrorActionPreference = 'Stop'
if (![Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { throw 'Windows x64 is required.' }
$framework=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
if (!$framework -or $framework.Release -lt 528040) { throw '.NET Framework 4.8 is required.' }
$files='LegionPowerSwitch.exe','app-icon.png','app.ico','README.md','LICENSE','THIRD-PARTY-NOTICES.md'
foreach($file in $files){if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $file))){throw "Missing package file: $file"}}
if (!$Destination) {
    $Destination=if(Test-Path -LiteralPath 'D:\'){'D:\software\LegionPowerSwitch'}else{Join-Path $env:LOCALAPPDATA 'Programs\LegionPowerSwitch'}
    try{New-Item -ItemType Directory -Path $Destination -Force | Out-Null}
    catch{$Destination=Join-Path $env:LOCALAPPDATA 'Programs\LegionPowerSwitch'}
}
$Destination=[IO.Path]::GetFullPath($Destination)
$executable=Join-Path $Destination 'LegionPowerSwitch.exe'
if(Get-Process -Name LegionPowerSwitch -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $executable}){throw 'Exit the running app from its tray menu first.'}
if(Test-Path -LiteralPath $executable){
    $backup=Join-Path $Destination ('Backups\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach($file in $files){$path=Join-Path $Destination $file;if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination $backup -Force}}
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach($file in $files){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $Destination $file) -Force}
# Never copy runtime settings or another machine's restoration backup.
if(!$NoShortcuts){
$shell=New-Object -ComObject WScript.Shell
foreach($folder in @([Environment]::GetFolderPath('Programs'),[Environment]::GetFolderPath('DesktopDirectory'))){
    $shortcut=$shell.CreateShortcut((Join-Path $folder 'LegionPowerSwitch.lnk'))
    $shortcut.TargetPath=$executable
    $shortcut.WorkingDirectory=$Destination
    $shortcut.IconLocation=(Join-Path $Destination 'app.ico')+',0'
    $shortcut.Description='ECO / NORM'
    $shortcut.Save()
}
}
Write-Output "Installed: $Destination"
Write-Output 'Open the desktop or Start menu shortcut. Windows may request administrator approval.'
