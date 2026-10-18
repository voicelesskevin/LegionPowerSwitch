$ErrorActionPreference = 'Stop'
$report = [ordered]@{ Passed=$false; GpuSwitchRequested=$false; Restarted=$false }
try {
    foreach ($name in 'Actual','Generic') {
        $folder = Join-Path $PSScriptRoot "TestRuns\$name"
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        foreach ($file in 'LegionPowerSwitch.exe','app-icon.png') {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $folder -Force
        }
        $exe=Join-Path $folder 'LegionPowerSwitch.exe'
        $probe=Start-Process -FilePath $exe -ArgumentList '--probe' -Wait -PassThru -WindowStyle Hidden
        if ($probe.ExitCode -ne 0) { throw "Probe failed: $name" }
        $diagnostic=Get-Content -LiteralPath (Join-Path $folder 'diagnostic.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $report["${name}Capabilities"]=$diagnostic.Capabilities
        if ($name -eq 'Actual' -and !$diagnostic.Display) { throw 'Automatic internal-panel identification did not succeed.' }
        $argument=if($name -eq 'Actual'){'--self-test'}else{'--self-test-generic'}
        $test=Start-Process -FilePath $exe -ArgumentList $argument -Wait -PassThru -WindowStyle Hidden
        $result=Get-Content -LiteralPath (Join-Path $folder 'self-test.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $report["${name}Result"]=$result
        if ($test.ExitCode -ne 0 -or !$result.Passed -or !$result.Restored) { throw "Runtime test failed: $name" }
        if ($name -eq 'Generic' -and ($result.TravelState.Fan -ne -1 -or $result.TravelState.Gsync -ne -1)) { throw 'Generic mode accessed a Lenovo-only state.' }
        $after=Start-Process -FilePath $exe -ArgumentList '--probe' -Wait -PassThru -WindowStyle Hidden
        if ($after.ExitCode -ne 0) { throw "Post-test probe failed: $name" }
        $final=Get-Content -LiteralPath (Join-Path $folder 'diagnostic.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($final.Snapshot.Gsync -ne $diagnostic.Snapshot.Gsync -or $final.Snapshot.Igpu -ne $diagnostic.Snapshot.Igpu -or $final.Snapshot.Fan -ne $diagnostic.Snapshot.Fan) { throw "Hardware settings were not restored: $name" }
    }
    $report.Passed=$true
} catch {
    $report.Error=$_.Exception.ToString()
} finally {
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime-tests-private.json') -Encoding UTF8
}
if (!$report.Passed) { exit 1 }
