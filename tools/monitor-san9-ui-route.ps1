[CmdletBinding()]
param(
    [ValidateRange(0, 49)] [int]$ExpectedTargetCityId = 46,
    [ValidateRange(15, 600)] [int]$DurationSeconds = 180,
    [ValidateRange(50, 2000)] [int]$PollMilliseconds = 100,
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4) { throw 'Use 32-bit Windows PowerShell (SysWOW64).' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'artifacts\input-traces'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$binaryDirectory = Join-Path $repositoryRoot 'src\San9AutoDomestic.Input.Win32\bin\Release'
foreach ($name in @('San9AutoDomestic.Core.dll', 'San9AutoDomestic.Adapter.San9Pk101.dll')) {
    $path = Join-Path $binaryDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing Release binary: $path" }
    [void][Reflection.Assembly]::LoadFrom($path)
}

function Get-FieldValue($report, [string]$name) {
    $field = $report.Fields | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if ($null -eq $field) { return '' }
    return [string]$field.Value
}

function Get-VerifiedCityId($report) {
    $field = $report.Fields | Where-Object { $_.Name -eq 'VerifiedCurrentCityId' } | Select-Object -First 1
    if ($null -eq $field -or $field.State.ToString() -ne 'Verified') { return -1 }
    return [int]$field.Value
}

$adapter = New-Object San9AutoDomestic.Adapter.San9Pk101.San9Pk101Adapter
$baseline = $adapter.ReadUiObservation()
if (-not $baseline.ReadSucceeded -or -not $baseline.StableAbc) {
    throw "Initial UI observation failed: $($baseline.DiagnosticCode) $($baseline.Message)"
}

$expectedPid = [int]$baseline.ProcessId
$expectedCreation = [long]$baseline.ProcessCreationFileTimeUtc
$expectedHwnd = [long]$baseline.MainWindowHandle
$sessionId = [Guid]::NewGuid()
$entries = New-Object Collections.ArrayList
$clock = [Diagnostics.Stopwatch]::StartNew()
$lastSignature = ''
$confirmationStable = 0
$unstableSince = $null

for ($beepIndex = 0; $beepIndex -lt 3; $beepIndex++) {
    [System.Media.SystemSounds]::Exclamation.Play()
    try { [Console]::Beep(880, 240) } catch { }
    Start-Sleep -Milliseconds 180
}
Write-Output ("MONITOR_STARTED session={0} pid={1} generation={2} hwnd=0x{3:X8}" -f $sessionId.ToString('N'), $expectedPid, $expectedCreation, $expectedHwnd)

while ($clock.Elapsed.TotalSeconds -lt $DurationSeconds) {
    $report = $adapter.ReadUiObservation()
    if (-not $report.ReadSucceeded -or -not $report.StableAbc) {
        if ($null -eq $unstableSince) {
            $unstableSince = $clock.ElapsedMilliseconds
            Write-Output ("TRANSIENT_UNSTABLE t={0}ms code={1}" -f $clock.ElapsedMilliseconds, $report.DiagnosticCode)
        }
        if (($clock.ElapsedMilliseconds - $unstableSince) -ge 5000) {
            throw "UI observation remained unstable for five seconds: $($report.DiagnosticCode) $($report.Message)"
        }
        Start-Sleep -Milliseconds $PollMilliseconds
        continue
    }
    $unstableSince = $null
    if ([int]$report.ProcessId -ne $expectedPid -or
        [long]$report.ProcessCreationFileTimeUtc -ne $expectedCreation -or
        [long]$report.MainWindowHandle -ne $expectedHwnd) {
        throw 'PID, process generation, or HWND changed during route.'
    }

    $layer = $report.Layer.ToString()
    $cityId = Get-VerifiedCityId $report
    $targetCity = Get-FieldValue $report 'DomesticTargetCityId'
    $outer = Get-FieldValue $report 'DomesticOuterDialog'
    $selector = Get-FieldValue $report 'OfficerSelector'
    $confirmation = Get-FieldValue $report 'DomesticConfirmation'
    $hover = Get-FieldValue $report 'HoveredCommand'
    $selected = Get-FieldValue $report 'SelectedOfficerIds'
    $candidateOrder = Get-FieldValue $report 'CandidateSourceOrder'
    $signature = "$layer|$cityId|$targetCity|$outer|$selector|$confirmation|$hover|$selected"

    if ($signature -ne $lastSignature) {
        $entry = [ordered]@{
            elapsedMilliseconds = [long]$clock.ElapsedMilliseconds
            observedUtc = [DateTime]::UtcNow.ToString('o')
            layer = $layer
            verifiedCurrentCityId = $cityId
            domesticTargetCityId = $targetCity
            domesticOuterDialog = $outer
            officerSelector = $selector
            domesticConfirmation = $confirmation
            hoveredCommand = $hover
            selectedOfficerIds = $selected
            candidateSourceOrder = $candidateOrder
            windowObservationToken = [string]$report.WindowObservationToken
        }
        [void]$entries.Add($entry)
        Write-Output ("STATE t={0}ms layer={1} city={2} target={3} outer={4} selector={5} confirmation={6} selected=[{7}]" -f `
            $entry.elapsedMilliseconds, $layer, $cityId, $targetCity, $outer, $selector, $confirmation, $selected)
        $lastSignature = $signature
    }

    if ($layer -eq 'DomesticConfirmation' -and $cityId -eq $ExpectedTargetCityId) {
        $confirmationStable++
        if ($confirmationStable -ge 3) { break }
    } else {
        $confirmationStable = 0
    }
    Start-Sleep -Milliseconds $PollMilliseconds
}

$accepted = $confirmationStable -ge 3
$document = [ordered]@{
    schema = 'san9-readonly-ui-route-v1'
    source = 'ReadOnlyObserved'
    sessionId = $sessionId.ToString('D')
    accepted = [bool]$accepted
    expectedTargetCityId = $ExpectedTargetCityId
    processId = $expectedPid
    processCreationFileTimeUtc = $expectedCreation
    mainWindowHandle = $expectedHwnd
    durationMilliseconds = [long]$clock.ElapsedMilliseconds
    entries = $entries
}
$directory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$path = Join-Path $directory ("S9UR-{0}.json" -f $sessionId.ToString('N'))
$json = $document | ConvertTo-Json -Depth 7 -Compress
$encoding = New-Object Text.UTF8Encoding($false)
$bytes = $encoding.GetBytes($json)
$stream = New-Object IO.FileStream($path, 'CreateNew', 'Write', 'None')
try {
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush($true)
} finally {
    $stream.Dispose()
}

[pscustomobject]@{
    Accepted = [bool]$accepted
    EntryCount = $entries.Count
    FinalLayer = if ($entries.Count -gt 0) { $entries[$entries.Count - 1].layer } else { '' }
    EvidencePath = $path
} | Format-List

if (-not $accepted) { exit 2 }
