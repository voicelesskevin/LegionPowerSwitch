$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /codepage:65001 "/out:$PSScriptRoot\BuildIcon.exe" /reference:System.Drawing.dll "$PSScriptRoot\BuildIcon.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon build failed' }
& "$PSScriptRoot\BuildIcon.exe" $PSScriptRoot
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ "/out:$PSScriptRoot\LegionPowerSwitch.exe" "/win32icon:$PSScriptRoot\app.ico" "/win32manifest:$PSScriptRoot\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll "$PSScriptRoot\PowerSwitch.cs" "$PSScriptRoot\Hardware.cs" "$PSScriptRoot\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
