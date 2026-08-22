[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManualEvidencePath,

    [string]$OutputDirectory = '',

    [string]$Confirmation = '',

    [switch]$OfflineValidate
)

$ErrorActionPreference = 'Stop'

if ([IntPtr]::Size -ne 4) {
    throw 'This replay runner must use 32-bit Windows PowerShell (SysWOW64).'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'artifacts\input-traces'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$binaryDirectory = Join-Path $repositoryRoot 'src\San9AutoDomestic.Input.Win32\bin\Release'
foreach ($name in @('San9AutoDomestic.Core.dll', 'San9AutoDomestic.Adapter.San9Pk101.dll', 'San9AutoDomestic.Input.Win32.dll')) {
    $path = Join-Path $binaryDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required Release binary is missing: $path" }
    [void][Reflection.Assembly]::LoadFrom($path)
}

$loaded = [San9AutoDomestic.Input.Win32.ManualInputTraceEvidenceReader]::Load([IO.Path]::GetFullPath($ManualEvidencePath))
if (-not $loaded.IsValid -or $null -eq $loaded.Evidence) {
    throw "Manual evidence rejected: $($loaded.Code): $($loaded.Message)"
}
$evidence = $loaded.Evidence

if ($OfflineValidate) {
    [pscustomobject]@{
        Status = 'OFFLINE_VALID'
        ManualSessionId = $evidence.SessionId
        TargetCityId = $evidence.TargetCityId
        ClientX = $evidence.ClientX
        ClientY = $evidence.ClientY
        EventsPayloadSha256 = $evidence.EventsPayloadSha256
        ProcessAccessed = $false
        InputSent = $false
    } | Format-List
    exit 0
}

if ($Confirmation -cne 'I_AUTHORIZE_ONE_TRACE_REPLAY') {
    throw 'Live replay requires -Confirmation I_AUTHORIZE_ONE_TRACE_REPLAY.'
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class San9ReplayPromptNative {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);
}
'@
$targetRect = New-Object San9ReplayPromptNative+RECT
if (-not [San9ReplayPromptNative]::GetWindowRect([IntPtr][long]$evidence.MainWindowHandle, [ref]$targetRect)) {
    throw 'GetWindowRect failed for the trace-bound San9 window.'
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'San9 One-Click Trace Replay'
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
$label.Text = "ONE program click is ready.`r`n`r`nBefore START, keep the exact facilities list open behind this box.`r`nSTART will re-check PID, generation, HWND and UI tokens, then`r`nreplay client ($($evidence.ClientX),$($evidence.ClientY)) once. Do not touch input."
$form.Controls.Add($label)

$startButton = New-Object System.Windows.Forms.Button
$startButton.Text = 'START ONE REPLAY'
$startButton.DialogResult = [System.Windows.Forms.DialogResult]::OK
$startButton.Location = New-Object System.Drawing.Point(280, 150)
$startButton.Size = New-Object System.Drawing.Size(130, 32)
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
    Write-Output 'TRACE_REPLAY_CANCELLED_BY_USER'
    exit 3
}

Start-Sleep -Milliseconds 1000
$hand = New-Object San9AutoDomestic.Input.Win32.San9Pk101TargetedInputHand
$authorization = $hand.CaptureFacilitiesMapAuthorization()
if (-not $authorization.CanAuthorize) {
    throw "Fresh map authorization rejected: $($authorization.FailureCode): $($authorization.FailureMessage)"
}
if ($authorization.ProcessId -ne $evidence.ProcessId `
    -or $authorization.ProcessCreationFileTimeUtc -ne $evidence.ProcessCreationFileTimeUtc `
    -or $authorization.MainWindowHandle -ne $evidence.MainWindowHandle) {
    throw 'Fresh PID, process generation, or HWND differs from the accepted manual trace.'
}
if ($authorization.WindowObservationToken -cne $evidence.StartWindowObservationToken `
    -or $authorization.StructuralToken -cne $evidence.StartStructuralToken) {
    throw 'Fresh facilities-map UI token differs from the accepted manual trace; no input was sent.'
}

$request = New-Object San9AutoDomestic.Input.Win32.FacilitiesRowSelectionRequest -ArgumentList @(
    [Guid]::NewGuid(),
    $authorization,
    $evidence.TargetCityId,
    $evidence.ClientX,
    $evidence.ClientY,
    [San9AutoDomestic.Input.Win32.TargetedClickMode]::SendInputStaged
)
$result = $hand.ExecuteFacilitiesRowSelection($request)
$dispatch = $hand.LastProgramDispatch
if ($null -eq $dispatch) { throw 'Program dispatch report is missing.' }

$success = $result.Status.ToString() -eq 'Completed' `
    -and $result.StateVerified `
    -and $result.ObservedCityId -eq $evidence.TargetCityId `
    -and $dispatch.MoveInserted -and $dispatch.DownInserted -and $dispatch.UpInserted

$replaySessionId = [Guid]::NewGuid()
$document = [ordered]@{
    schema = 'san9-program-input-trace-v1'
    source = 'ProgramDispatched'
    replaySessionId = $replaySessionId.ToString('D')
    manualSessionId = $evidence.SessionId.ToString('D')
    manualEventsPayloadSha256 = $evidence.EventsPayloadSha256
    manualEvidencePath = $evidence.EvidencePath
    exactSuccess = [bool]$success
    targetCityId = $evidence.TargetCityId
    processId = $dispatch.ProcessId
    processCreationFileTimeUtc = $dispatch.ProcessCreationFileTimeUtc
    mainWindowHandle = $dispatch.MainWindowHandle
    clientX = $dispatch.ClientX
    clientY = $dispatch.ClientY
    screenX = $dispatch.ScreenX
    screenY = $dispatch.ScreenY
    foregroundWindowHandleAtStart = $dispatch.ForegroundWindowHandleAtStart
    foregroundProcessIdAtStart = $dispatch.ForegroundProcessIdAtStart
    foregroundWindowHandleAtDown = $dispatch.ForegroundWindowHandleAtDown
    foregroundProcessIdAtDown = $dispatch.ForegroundProcessIdAtDown
    stopwatchFrequency = $dispatch.StopwatchFrequency
    startTimestamp = $dispatch.StartTimestamp
    moveTimestamp = $dispatch.MoveTimestamp
    downTimestamp = $dispatch.DownTimestamp
    upTimestamp = $dispatch.UpTimestamp
    moveInserted = $dispatch.MoveInserted
    downInserted = $dispatch.DownInserted
    upInserted = $dispatch.UpInserted
    attemptedInputCount = $dispatch.AttemptedInputCount
    dispatchStartedUtc = $dispatch.StartedUtc.ToUniversalTime().ToString('O')
    dispatchFinishedUtc = $dispatch.FinishedUtc.ToUniversalTime().ToString('O')
    dispatchError = $dispatch.Error
    resultStatus = $result.Status.ToString()
    resultCode = $result.Code
    resultMessage = $result.Message
    resultObservedCityId = $result.ObservedCityId
    resultBindingStable = $result.BindingStable
    resultForegroundStable = $result.ForegroundStable
    resultStateVerified = $result.StateVerified
}

$json = $document | ConvertTo-Json -Depth 4 -Compress
$directory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$programEvidencePath = Join-Path $directory ("S9PT-{0}.json" -f $replaySessionId.ToString('N'))
$bytes = New-Object Text.UTF8Encoding($false)
$payload = $bytes.GetBytes($json)
$stream = New-Object IO.FileStream($programEvidencePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $stream.Write($payload, 0, $payload.Length)
    $stream.Flush($true)
} finally {
    $stream.Dispose()
}

[pscustomobject]@{
    ExactSuccess = $success
    Status = $result.Status
    Code = $result.Code
    ObservedCityId = $result.ObservedCityId
    Client = "($($dispatch.ClientX),$($dispatch.ClientY))"
    ProgramEvidencePath = $programEvidencePath
} | Format-List

if (-not $success) { exit 2 }
