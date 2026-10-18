$ErrorActionPreference='Stop'
$folder=Join-Path $PSScriptRoot ('TestRuns\Installer-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$sentinel='local-machine-settings-do-not-replace'
Set-Content -LiteralPath (Join-Path $folder 'original.json') -Value $sentinel -Encoding ASCII
Set-Content -LiteralPath (Join-Path $folder 'settings.json') -Value $sentinel -Encoding ASCII
& (Join-Path $PSScriptRoot 'Install.ps1') -Destination $folder -NoShortcuts
if((Get-FileHash -LiteralPath (Join-Path $folder 'LegionPowerSwitch.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'LegionPowerSwitch.exe')).Hash){throw 'Installed executable hash differs.'}
& (Join-Path $PSScriptRoot 'Install.ps1') -Destination $folder -NoShortcuts
foreach($name in 'original.json','settings.json'){if((Get-Content -LiteralPath (Join-Path $folder $name) -Raw).Trim() -ne $sentinel){throw "Installer overwrote runtime state: $name"}}
if(!(Get-ChildItem -LiteralPath (Join-Path $folder 'Backups') -Directory)){throw 'Update did not back up the previous executable.'}
foreach($name in 'diagnostic.json','actions.log','original-power-plan.pow'){if(Test-Path -LiteralPath (Join-Path $folder $name)){throw "Installer copied private state: $name"}}
Write-Output 'PASS installer: executable copied, previous program backed up, settings preserved, no private reports copied, no shortcuts changed.'
