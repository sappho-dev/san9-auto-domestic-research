[CmdletBinding()]
param(
    [ValidateRange(0, 49)]
    [int]$ExpectedCityId = 0,

    [ValidateSet('Commerce', 'Cultivate', 'Repair')]
    [string]$ExpectedCommand = 'Commerce',

    [string]$OutputDirectory = '',

    [switch]$OfflineValidate
)

$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4) { throw 'Use 32-bit Windows PowerShell (SysWOW64).' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $PSScriptRoot 'artifacts\input-traces' }

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$binaryDirectory = Join-Path $repositoryRoot 'src\San9AutoDomestic.Input.Win32\bin\Release'
foreach ($name in @('San9AutoDomestic.Core.dll', 'San9AutoDomestic.Adapter.San9Pk101.dll')) {
    $path = Join-Path $binaryDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required Release binary is missing: $path" }
    [void][Reflection.Assembly]::LoadFrom($path)
}
if ($OfflineValidate) { Write-Output 'COMMAND_HOVER_CAPTURE_OFFLINE_VALIDATION: PASS'; exit 0 }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class San9HoverCaptureNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr window, ref POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
'@

$adapter = New-Object San9AutoDomestic.Adapter.San9Pk101.San9Pk101Adapter
$initial = $adapter.ReadUiObservation()
if (-not $initial.ReadSucceeded -or -not $initial.StableAbc -or -not $initial.MainWindowHandle) {
    throw 'The exact San9 UI observation is not ready.'
}
$targetRect = New-Object San9HoverCaptureNative+RECT
if (-not [San9HoverCaptureNative]::GetWindowRect([IntPtr][long]$initial.MainWindowHandle, [ref]$targetRect)) {
    throw 'GetWindowRect failed for San9.'
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'San9 Commerce Hover Capture'
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Size = New-Object System.Drawing.Size(540, 220)
$form.Location = New-Object System.Drawing.Point(
    ($targetRect.Left + [Math]::Max(0, (($targetRect.Right - $targetRect.Left - 540) / 2))),
    ($targetRect.Top + [Math]::Max(0, (($targetRect.Bottom - $targetRect.Top - 220) / 2))))
$form.TopMost = $true
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$label = New-Object System.Windows.Forms.Label
$label.Location = New-Object System.Drawing.Point(20, 18)
$label.Size = New-Object System.Drawing.Size(495, 115)
$label.Text = "Read-only hover capture.`r`n`r`nClick START, move the mouse onto the COMMERCE command,`r`nand keep it still until you hear the success sound.`r`nDo NOT click inside the game."
$form.Controls.Add($label)
$start = New-Object System.Windows.Forms.Button
$start.Text = 'START HOVER'
$start.DialogResult = [System.Windows.Forms.DialogResult]::OK
$start.Location = New-Object System.Drawing.Point(300, 145)
$start.Size = New-Object System.Drawing.Size(110, 32)
$form.Controls.Add($start)
$form.AcceptButton = $start
$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'CANCEL'
$cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$cancel.Location = New-Object System.Drawing.Point(415, 145)
$cancel.Size = New-Object System.Drawing.Size(95, 32)
$form.Controls.Add($cancel)
$form.CancelButton = $cancel
$choice = $form.ShowDialog()
$form.Dispose()
if ($choice -ne [System.Windows.Forms.DialogResult]::OK) { Write-Output 'HOVER_CAPTURE_CANCELLED'; exit 3 }

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $report = $adapter.ReadUiObservation()
    $cityField = $report.Fields | Where-Object { $_.Name -eq 'VerifiedCurrentCityId' } | Select-Object -First 1
    $foreground = [San9HoverCaptureNative]::GetForegroundWindow()
    [uint32]$foregroundPid = 0
    [void][San9HoverCaptureNative]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid)
    $match = $report.ReadSucceeded -and $report.StableAbc `
        -and $report.Layer.ToString() -eq 'DomesticCommandMenu' `
        -and $report.HoveredCommandState.ToString() -eq 'Verified' `
        -and $report.HoveredCommand -ceq $ExpectedCommand `
        -and $null -ne $cityField -and $cityField.State.ToString() -eq 'Verified' `
        -and [int]$cityField.Value -eq $ExpectedCityId `
        -and $foreground.ToInt64() -eq [long]$report.MainWindowHandle `
        -and [int]$foregroundPid -eq [int]$report.ProcessId
    if ($match) {
        $point = New-Object San9HoverCaptureNative+POINT
        if (-not [San9HoverCaptureNative]::GetCursorPos([ref]$point)) { throw 'GetCursorPos failed.' }
        $screenX = $point.X; $screenY = $point.Y
        if (-not [San9HoverCaptureNative]::ScreenToClient([IntPtr][long]$report.MainWindowHandle, [ref]$point)) { throw 'ScreenToClient failed.' }
        if ($point.X -lt 0 -or $point.X -ge 1024 -or $point.Y -lt 0 -or $point.Y -ge 768) { throw 'Verified hover cursor is outside the exact client.' }
        $session = [Guid]::NewGuid()
        $document = [ordered]@{
            schema = 'san9-manual-command-hover-v1'
            source = 'ManualObserved'
            sessionId = $session.ToString('D')
            capturedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            command = $ExpectedCommand
            cityId = $ExpectedCityId
            processId = $report.ProcessId
            processCreationFileTimeUtc = $report.ProcessCreationFileTimeUtc
            mainWindowHandle = $report.MainWindowHandle
            layer = $report.Layer.ToString()
            windowObservationToken = $report.WindowObservationToken
            clientX = $point.X
            clientY = $point.Y
            screenX = $screenX
            screenY = $screenY
            foregroundWindowHandle = $foreground.ToInt64()
            foregroundProcessId = [int]$foregroundPid
        }
        $json = $document | ConvertTo-Json -Compress
        $directory = [IO.Path]::GetFullPath($OutputDirectory)
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $outputPath = Join-Path $directory ("S9HT-{0}.json" -f $session.ToString('N'))
        $encoding = New-Object Text.UTF8Encoding($false)
        $bytes = $encoding.GetBytes($json)
        $stream = New-Object IO.FileStream($outputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        [System.Media.SystemSounds]::Asterisk.Play()
        [pscustomobject]@{Status='ACCEPTED';Command=$ExpectedCommand;CityId=$ExpectedCityId;Client="($($point.X),$($point.Y))";EvidencePath=$outputPath}|Format-List
        exit 0
    }
    Start-Sleep -Milliseconds 250
}

Write-Output 'HOVER_CAPTURE_TIMEOUT'
exit 2
