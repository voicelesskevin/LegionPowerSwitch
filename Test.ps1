$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($suite in 'GpuRequestTests','CompatibilityTests') {
    & $compiler /nologo /target:exe /platform:x64 /codepage:65001 "/out:$PSScriptRoot\$suite.exe" "/main:$suite" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll "$PSScriptRoot\PowerSwitch.cs" "$PSScriptRoot\Hardware.cs" "$PSScriptRoot\$suite.cs"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $suite" }
    & (Join-Path $PSScriptRoot "$suite.exe")
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $suite" }
}
