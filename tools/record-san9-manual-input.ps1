[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 49)]
    [int]$ExpectedTargetCityId,

    [string]$OutputDirectory = '',

    [ValidateRange(10, 120)]
    [int]$ArmTimeoutSeconds = 120,

    [switch]$OfflineValidate,

    [switch]$NoPrompt
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'artifacts\input-traces'
}

if ([IntPtr]::Size -ne 4) {
    throw 'This trace runner must use 32-bit Windows PowerShell (SysWOW64).'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$binaryDirectory = Join-Path $repositoryRoot 'src\San9AutoDomestic.Input.Win32\bin\Release'
$corePath = Join-Path $binaryDirectory 'San9AutoDomestic.Core.dll'
$adapterPath = Join-Path $binaryDirectory 'San9AutoDomestic.Adapter.San9Pk101.dll'
$inputPath = Join-Path $binaryDirectory 'San9AutoDomestic.Input.Win32.dll'

foreach ($path in @($corePath, $adapterPath, $inputPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Release binary is missing: $path"
    }
}

[void][Reflection.Assembly]::LoadFrom($corePath)
[void][Reflection.Assembly]::LoadFrom($adapterPath)
[void][Reflection.Assembly]::LoadFrom($inputPath)

$options = New-Object San9AutoDomestic.Input.Win32.ManualInputTraceOptions -ArgumentList @(
    $ExpectedTargetCityId,
    [IO.Path]::GetFullPath($OutputDirectory),
    ($ArmTimeoutSeconds * 1000),
    500,
    60000,
    500,
    1
)

if ($OfflineValidate) {
    Write-Output 'TRACE_RUNNER_OFFLINE_VALIDATION: PASS'
    exit 0
}

if (-not $NoPrompt) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class San9TracePromptNative {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);
}
'@
    $promptObservation = (New-Object San9AutoDomestic.Adapter.San9Pk101.San9Pk101Adapter).ReadUiObservation()
    if (-not $promptObservation.ReadSucceeded -or -not $promptObservation.MainWindowHandle) {
        throw 'Cannot locate the exact San9 window for the trace prompt.'
    }
    $targetRect = New-Object San9TracePromptNative+RECT
    if (-not [San9TracePromptNative]::GetWindowRect([IntPtr][long]$promptObservation.MainWindowHandle, [ref]$targetRect)) {
        throw 'GetWindowRect failed for the exact San9 window.'
    }

    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'San9 Manual Input Trace'
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Size = New-Object System.Drawing.Size(540, 230)
    $form.Location = New-Object System.Drawing.Point(
        ($targetRect.Left + [Math]::Max(0, (($targetRect.Right - $targetRect.Left - 540) / 2))),
        ($targetRect.Top + [Math]::Max(0, (($targetRect.Bottom - $targetRect.Top - 230) / 2))))
    $form.TopMost = $true
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false

    $label = New-Object System.Windows.Forms.Label
    $label.Location = New-Object System.Drawing.Point(20, 18)
    $label.Size = New-Object System.Drawing.Size(495, 120)
    $label.Text = "This box is centered over the LOCAL San9 window.`r`n`r`nClick START and wait 1 second. You then have 60 seconds to`r`nclick the requested target facility row exactly once.`r`nAfter the click, do not touch input for 3 seconds."
    $form.Controls.Add($label)

    $startButton = New-Object System.Windows.Forms.Button
    $startButton.Text = 'START'
    $startButton.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $startButton.Location = New-Object System.Drawing.Point(310, 150)
    $startButton.Size = New-Object System.Drawing.Size(95, 32)
    $form.Controls.Add($startButton)
    $form.AcceptButton = $startButton

    $cancelButton = New-Object System.Windows.Forms.Button
    $cancelButton.Text = 'CANCEL'
    $cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $cancelButton.Location = New-Object System.Drawing.Point(415, 150)
    $cancelButton.Size = New-Object System.Drawing.Size(95, 32)
    $form.Controls.Add($cancelButton)
    $form.CancelButton = $cancelButton

    $choice = $form.ShowDialog()
    $form.Dispose()
    if ($choice -ne [System.Windows.Forms.DialogResult]::OK) {
        Write-Output 'TRACE_RUNNER_CANCELLED_BY_USER'
        exit 3
    }
}

[System.Media.SystemSounds]::Asterisk.Play()
$recorder = New-Object San9AutoDomestic.Input.Win32.San9ManualInputTraceRecorder
$result = $recorder.Record($options)

[pscustomobject]@{
    Status = $result.Status.ToString()
    Code = $result.Code
    Message = $result.Message
    SessionId = $result.SessionId
    EvidencePath = $result.EvidencePath
    ReplayAuthorized = $result.ReplayAuthorized
    EventCount = $result.EventCount
    LeftClickCount = $result.LeftClickCount
    ObservedTargetCityId = $result.ObservedTargetCityId
} | Format-List

if (-not $result.ReplayAuthorized) {
    exit 2
}
