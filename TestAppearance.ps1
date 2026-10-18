$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'LegionPowerSwitch.exe'))
$engineType = $assembly.GetType('LegionPowerSwitch.Engine', $true)
$engineType.GetField('Root').SetValue($null,$PSScriptRoot)
$profile = [Activator]::CreateInstance($assembly.GetType('LegionPowerSwitch.HardwareProfile', $true))
$profile.Manufacturer = 'LENOVO'
$profile.Model = 'Preview'
$profile.LenovoFan = $profile.LenovoGpu = $true
$engineType.GetProperty('Hardware').SetValue($null,$profile,$null)
$windowType = $assembly.GetType('LegionPowerSwitch.MainWindow', $true)
$constructor = $windowType.GetConstructor([Reflection.BindingFlags]'Instance,Public,NonPublic', $null, [Type[]]@([bool]), $null)
$results = @()
foreach ($scale in 1.0,1.5,2.0) {
    $form = $constructor.Invoke([object[]]@($true))
    try {
        $form.Opacity = 0
        $form.ShowInTaskbar = $false
        $form.Show()
        if ($scale -ne 1.0) { $form.Scale([Drawing.SizeF]::new($scale,$scale)) }
        [Windows.Forms.Application]::DoEvents()
        $form.PerformLayout()
        [Windows.Forms.Application]::DoEvents()
        $controls = @()
        function CollectControls($parent) {
            foreach ($control in $parent.Controls) {
                $script:controls += $control
                CollectControls $control
            }
        }
        CollectControls $form
        if ($form.Text -cne 'LegionPowerSwitch') { throw 'Window title mismatch.' }
        $heading = @($controls | Where-Object { $_ -is [Windows.Forms.Label] -and $_.Text -ceq 'LegionPowerSwitch' })
        if ($heading.Count -ne 1) { throw 'Main heading mismatch.' }
        $headingSize = [Windows.Forms.TextRenderer]::MeasureText($heading[0].Text,$heading[0].Font)
        if ($headingSize.Width -gt $heading[0].ClientSize.Width -or $headingSize.Height -gt $heading[0].ClientSize.Height) { throw 'Main heading clipped.' }
        $power = $windowType.GetField('power',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($form)
        $updatePower = $windowType.GetMethod('UpdatePowerStatus',[Reflection.BindingFlags]'Instance,NonPublic')
        $updatePower.Invoke($form,[object[]]@($true,[single]0.8))
        $gpuLabel = $windowType.GetField('gpu',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($form)
        $updateGpu = $windowType.GetMethod('UpdateGpuStatus',[Reflection.BindingFlags]'Instance,NonPublic')
        $updateGpu.Invoke($form,[object[]]@('Current integrated GPU',0))
        if ($gpuLabel.ForeColor.ToArgb() -ne [Drawing.Color]::FromArgb(74,222,128).ToArgb()) { throw 'IGPU color mismatch.' }
        $updateGpu.Invoke($form,[object[]]@('Current discrete GPU',1))
        if ($gpuLabel.ForeColor.ToArgb() -ne [Drawing.Color]::FromArgb(251,146,60).ToArgb()) { throw 'DGPU color mismatch.' }
        $resultLabel = $windowType.GetField('result',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($form)
        $cancel = $windowType.GetField('cancel',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($form)
        $restart = $windowType.GetField('restart',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($form)
        $cancel.Enabled = $restart.Enabled = $false
        $resultLabel.Text = @('Lenovo Quiet mode','Power saving / CPU maximum 80% / CPU boost disabled','Internal display 60Hz','Internal display brightness 40%','GPU: no pending restart',('Long diagnostic message: ' + ('sample text ' * 30))) -join [Environment]::NewLine
        $form.PerformLayout()
        [Windows.Forms.Application]::DoEvents()
        $needed = [Windows.Forms.TextRenderer]::MeasureText($resultLabel.Text,$resultLabel.Font,[Drawing.Size]::new($resultLabel.ClientSize.Width,[int]::MaxValue),[Windows.Forms.TextFormatFlags]'WordBreak,TextBoxControl,NoPrefix')
        if ($resultLabel.ClientSize.Height -lt $needed.Height) { throw 'Result content clipped.' }
        if ($resultLabel.Bottom -gt $cancel.Parent.Top) { throw 'Results overlap action buttons.' }
        $disabledImage = [Drawing.Bitmap]::new($cancel.Width,$cancel.Height)
        try {
            $cancel.DrawToBitmap($disabledImage,[Drawing.Rectangle]::new(0,0,$cancel.Width,$cancel.Height))
            $readablePixels = 0
            for ($y=2;$y -lt $disabledImage.Height-2;$y++) {
                for ($x=2;$x -lt $disabledImage.Width-2;$x++) {
                    $pixel = $disabledImage.GetPixel($x,$y)
                    if ($pixel.R -gt 120 -and $pixel.G -gt 120 -and $pixel.B -gt 120) { $readablePixels++ }
                }
            }
            if ($readablePixels -lt 10) { throw 'Disabled button text is not readable.' }
        } finally { $disabledImage.Dispose() }
        if ($power.ForeColor.ToArgb() -ne [Drawing.Color]::FromArgb(74,222,128).ToArgb()) { throw 'AC status color mismatch.' }
        $updatePower.Invoke($form,[object[]]@($false,[single]0.8))
        if ($power.ForeColor.ToArgb() -ne [Drawing.Color]::FromArgb(251,146,60).ToArgb()) { throw 'Battery status color mismatch.' }
        if ($scale -eq 1.0) {
            $batteryImage = [Drawing.Bitmap]::new($form.Width,$form.Height)
            try {
                $form.DrawToBitmap($batteryImage,[Drawing.Rectangle]::new(0,0,$form.Width,$form.Height))
                $batteryImage.Save((Join-Path $PSScriptRoot 'preview-battery.png'))
            } finally { $batteryImage.Dispose() }
        }
        $updatePower.Invoke($form,[object[]]@($true,[single]0.8))
        $eco = @($controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'ECO' })
        $norm = @($controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'NORM' })
        if ($eco.Count -ne 1 -or $norm.Count -ne 1) { throw 'Mode buttons missing.' }
        if ($eco[0].BackColor.B -le $eco[0].BackColor.R) { throw 'ECO button is not blue.' }
        if ($norm[0].BackColor.R -lt 230 -or $norm[0].ForeColor.R -gt 40) { throw 'NORM contrast mismatch.' }
        $outside = @($controls | Where-Object { $_.Visible -and ($_.Left -lt 0 -or $_.Top -lt 0 -or $_.Right -gt $_.Parent.DisplayRectangle.Right+1 -or $_.Bottom -gt $_.Parent.DisplayRectangle.Bottom+1) } | Select-Object Text,Bounds,@{n='ParentSize';e={$_.Parent.DisplayRectangle}})
        if ($outside.Count) { throw ($outside | ConvertTo-Json -Depth 4) }
        $viewport = $form.Controls[0]
        if ($viewport.VerticalScroll.Visible) {
            $viewport.ScrollControlIntoView($cancel.Parent)
            [Windows.Forms.Application]::DoEvents()
            $buttonPoint = $viewport.PointToClient($cancel.PointToScreen([Drawing.Point]::Empty))
            if ($buttonPoint.Y -lt 0 -or $buttonPoint.Y+$cancel.Height -gt $viewport.ClientSize.Height) { throw 'Action buttons cannot be scrolled into view.' }
            $viewport.AutoScrollPosition = [Drawing.Point]::Empty
        }
        $bitmap = [Drawing.Bitmap]::new($form.Width,$form.Height)
        try {
            $form.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$form.Width,$form.Height))
            $bitmap.Save((Join-Path $PSScriptRoot ('preview-dark-{0}.png' -f [int]($scale*100))))
        } finally { $bitmap.Dispose() }
        $results += [ordered]@{ Scale=$scale; Width=$form.Width; Height=$form.Height; ControlsWithinParents=$true; ModeColorsPassed=$true; TitlesPassed=$true; PowerColorsPassed=$true; GpuColorsPassed=$true; ResultsUnclipped=$true; DisabledTextReadable=$true }
    } finally { $form.Close();$form.Dispose() }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'appearance-tests.json') -Encoding UTF8
foreach($generic in $false,$true) {
    $profile.LenovoFan=$profile.LenovoGpu=!$generic
    $engineType.GetProperty('Hardware').SetValue($null,$profile,$null)
    $form=$constructor.Invoke([object[]]@($true))
    try {
        $form.Opacity=0;$form.ShowInTaskbar=$false;$form.Show()
        [Windows.Forms.Application]::DoEvents()
        $include=$windowType.GetField('include',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($form)
        if($include.Enabled -eq $generic){throw 'GPU capability gating mismatch.'}
        if($generic -and $include.Checked){throw 'Unsupported GPU control was selected.'}
        $bitmap=[Drawing.Bitmap]::new($form.Width,$form.Height)
        try {
            $form.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$form.Width,$form.Height))
            $name=if($generic){'preview-generic.png'}else{'controller-preview.png'}
            $bitmap.Save((Join-Path $PSScriptRoot $name))
        } finally {$bitmap.Dispose()}
    } finally {$form.Close();$form.Dispose()}
}
