[CmdletBinding()]
param(
    [ValidateRange(0, 49)] [int]$ExpectedCityId = 0,
    [ValidateSet('Commerce')] [string]$ExpectedCommand = 'Commerce',
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
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing Release binary: $path" }
    [void][Reflection.Assembly]::LoadFrom($path)
}
if ($OfflineValidate) { Write-Output 'COMMAND_CLICK_CAPTURE_OFFLINE_VALIDATION: PASS'; exit 0 }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class San9CommandClickNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
}
'@

function Get-San9ClickSample([long]$window) {
    $point = New-Object San9CommandClickNative+POINT
    if (-not [San9CommandClickNative]::GetCursorPos([ref]$point)) { throw 'GetCursorPos failed.' }
    $screenX=$point.X; $screenY=$point.Y
    if (-not [San9CommandClickNative]::ScreenToClient([IntPtr]$window,[ref]$point)) { throw 'ScreenToClient failed.' }
    $foreground=[San9CommandClickNative]::GetForegroundWindow();[uint32]$foregroundPid=0
    [void][San9CommandClickNative]::GetWindowThreadProcessId($foreground,[ref]$foregroundPid)
    $buttons=0
    foreach($pair in @(@(1,1),@(2,2),@(4,4),@(5,8),@(6,16))){if(([San9CommandClickNative]::GetAsyncKeyState($pair[0])-band 0x8000)-ne 0){$buttons=$buttons-bor $pair[1]}}
    $modifiers=0
    if(([San9CommandClickNative]::GetAsyncKeyState(16)-band 0x8000)-ne 0){$modifiers=$modifiers-bor 1}
    if(([San9CommandClickNative]::GetAsyncKeyState(17)-band 0x8000)-ne 0){$modifiers=$modifiers-bor 2}
    if(([San9CommandClickNative]::GetAsyncKeyState(18)-band 0x8000)-ne 0){$modifiers=$modifiers-bor 4}
    if((([San9CommandClickNative]::GetAsyncKeyState(91)-band 0x8000)-ne 0)-or(([San9CommandClickNative]::GetAsyncKeyState(92)-band 0x8000)-ne 0)){$modifiers=$modifiers-bor 8}
    [pscustomobject]@{ScreenX=$screenX;ScreenY=$screenY;ClientX=$point.X;ClientY=$point.Y;ForegroundWindowHandle=$foreground.ToInt64();ForegroundProcessId=[int]$foregroundPid;Buttons=$buttons;Modifiers=$modifiers}
}

function Get-VerifiedCity($report) {
    $field=$report.Fields|Where-Object{$_.Name-eq'VerifiedCurrentCityId'}|Select-Object -First 1
    if($null-ne$field-and$field.State.ToString()-eq'Verified'){return [int]$field.Value}
    return -1
}

$adapter=New-Object San9AutoDomestic.Adapter.San9Pk101.San9Pk101Adapter
$initial=$adapter.ReadUiObservation()
if(-not$initial.ReadSucceeded-or-not$initial.StableAbc-or$initial.Layer.ToString()-ne'DomesticCommandMenu'-or(Get-VerifiedCity $initial)-ne$ExpectedCityId){throw 'Open the expected city domestic command menu first.'}
$rect=New-Object San9CommandClickNative+RECT
if(-not[San9CommandClickNative]::GetWindowRect([IntPtr][long]$initial.MainWindowHandle,[ref]$rect)){throw 'GetWindowRect failed.'}

$form=New-Object Windows.Forms.Form;$form.Text='San9 Manual Commerce Click Capture';$form.StartPosition='Manual';$form.Size=New-Object Drawing.Size(540,220)
$form.Location=New-Object Drawing.Point(($rect.Left+[Math]::Max(0,(($rect.Right-$rect.Left-540)/2))),($rect.Top+[Math]::Max(0,(($rect.Bottom-$rect.Top-220)/2))))
$form.TopMost=$true;$form.FormBorderStyle='FixedDialog';$form.MaximizeBox=$false;$form.MinimizeBox=$false
$label=New-Object Windows.Forms.Label;$label.Location=New-Object Drawing.Point(20,18);$label.Size=New-Object Drawing.Size(495,115)
$label.Text="Manual ONE-click capture.`r`n`r`nClick START and wait for the sound. Then click COMMERCE once.`r`nDo not touch any other input. Stop after the click."
$form.Controls.Add($label)
$start=New-Object Windows.Forms.Button;$start.Text='START CAPTURE';$start.DialogResult='OK';$start.Location=New-Object Drawing.Point(295,145);$start.Size=New-Object Drawing.Size(115,32);$form.Controls.Add($start);$form.AcceptButton=$start
$cancel=New-Object Windows.Forms.Button;$cancel.Text='CANCEL';$cancel.DialogResult='Cancel';$cancel.Location=New-Object Drawing.Point(415,145);$cancel.Size=New-Object Drawing.Size(95,32);$form.Controls.Add($cancel);$form.CancelButton=$cancel
$choice=$form.ShowDialog();$form.Dispose();if($choice-ne'OK'){Write-Output 'COMMAND_CLICK_CAPTURE_CANCELLED';exit 3}

$window=[long]$initial.MainWindowHandle;$pidExpected=[int]$initial.ProcessId
$armDeadline=[Diagnostics.Stopwatch]::StartNew();$cleanSince=$null;$armed=$false
while($armDeadline.ElapsedMilliseconds-lt120000){
    $sample=Get-San9ClickSample $window
    if($sample.ForegroundWindowHandle-eq$window-and$sample.ForegroundProcessId-eq$pidExpected-and$sample.Buttons-eq0-and$sample.Modifiers-eq0){
        if($null-eq$cleanSince){$cleanSince=$armDeadline.ElapsedMilliseconds}
        if(($armDeadline.ElapsedMilliseconds-$cleanSince)-ge500){$armed=$true;break}
    }else{$cleanSince=$null}
    Start-Sleep -Milliseconds 2
}
if(-not$armed){throw 'Exact game foreground did not arm.'}

$before=$adapter.ReadUiObservation()
if(-not$before.ReadSucceeded-or-not$before.StableAbc-or$before.Layer.ToString()-ne'DomesticCommandMenu'-or(Get-VerifiedCity $before)-ne$ExpectedCityId `
    -or$before.ProcessId-ne$initial.ProcessId-or$before.ProcessCreationFileTimeUtc-ne$initial.ProcessCreationFileTimeUtc-or$before.MainWindowHandle-ne$initial.MainWindowHandle){throw 'Fresh pre-click menu gate rejected.'}
[System.Media.SystemSounds]::Asterisk.Play()

$clock=[Diagnostics.Stopwatch]::StartNew();$events=New-Object Collections.ArrayList;$previous=Get-San9ClickSample $window;$down=$false;$up=$false;$quietAt=0
while($clock.ElapsedMilliseconds-lt60000){
    $sample=Get-San9ClickSample $window;$now=$clock.ElapsedTicks
    if($sample.ForegroundWindowHandle-ne$window-or$sample.ForegroundProcessId-ne$pidExpected){throw 'Foreground changed during manual click capture.'}
    if(($sample.Buttons-band(-bnot 1))-ne0-or$sample.Modifiers-ne0){throw 'Other input was observed during manual click capture.'}
    $wasLeft=($previous.Buttons-band 1)-ne0;$isLeft=($sample.Buttons-band 1)-ne0
    if(-not$wasLeft-and$isLeft){if($down-or$up){throw 'Extra left click observed.'};$down=$true;[void]$events.Add([ordered]@{kind='LeftButtonDown';ticks=$now;clientX=$sample.ClientX;clientY=$sample.ClientY;screenX=$sample.ScreenX;screenY=$sample.ScreenY;foregroundWindowHandle=$sample.ForegroundWindowHandle;foregroundProcessId=$sample.ForegroundProcessId})}
    elseif($wasLeft-and-not$isLeft){if(-not$down-or$up){throw 'Invalid left-button sequence.'};$up=$true;$quietAt=$clock.ElapsedMilliseconds;[void]$events.Add([ordered]@{kind='LeftButtonUp';ticks=$now;clientX=$sample.ClientX;clientY=$sample.ClientY;screenX=$sample.ScreenX;screenY=$sample.ScreenY;foregroundWindowHandle=$sample.ForegroundWindowHandle;foregroundProcessId=$sample.ForegroundProcessId})}
    $previous=$sample
    if($up-and($clock.ElapsedMilliseconds-$quietAt)-ge500){break}
    Start-Sleep -Milliseconds 2
}
if(-not$down-or-not$up){throw 'No complete manual left click was captured.'}

$after=$adapter.ReadUiObservation();$afterCity=Get-VerifiedCity $after;$allowed=@('DomesticOuterDialog','OfficerSelector','DomesticConfirmation')
$accepted=$after.ReadSucceeded-and$after.StableAbc-and($allowed-contains$after.Layer.ToString())-and$afterCity-eq$ExpectedCityId `
    -and$after.ProcessId-eq$before.ProcessId-and$after.ProcessCreationFileTimeUtc-eq$before.ProcessCreationFileTimeUtc-and$after.MainWindowHandle-eq$before.MainWindowHandle
$session=[Guid]::NewGuid();$eventJson=$events|ConvertTo-Json -Compress
$sha=[Security.Cryptography.SHA256]::Create();try{$eventHash=([BitConverter]::ToString($sha.ComputeHash((New-Object Text.UTF8Encoding($false)).GetBytes($eventJson)))).Replace('-','')}finally{$sha.Dispose()}
$document=[ordered]@{schema='san9-manual-command-click-v1';source='ManualObserved';sessionId=$session.ToString('D');accepted=[bool]$accepted;command=$ExpectedCommand;cityId=$ExpectedCityId;processId=$before.ProcessId;processCreationFileTimeUtc=$before.ProcessCreationFileTimeUtc;mainWindowHandle=$before.MainWindowHandle;startLayer=$before.Layer.ToString();startWindowObservationToken=$before.WindowObservationToken;finalLayer=$after.Layer.ToString();finalCityId=$afterCity;finalWindowObservationToken=$after.WindowObservationToken;stopwatchFrequency=[Diagnostics.Stopwatch]::Frequency;eventsPayloadSha256=$eventHash;events=$events}
$json=$document|ConvertTo-Json -Depth 5 -Compress;$directory=[IO.Path]::GetFullPath($OutputDirectory);[IO.Directory]::CreateDirectory($directory)|Out-Null;$path=Join-Path $directory("S9CT-{0}.json"-f$session.ToString('N'))
$encoding=New-Object Text.UTF8Encoding($false);$bytes=$encoding.GetBytes($json);$stream=New-Object IO.FileStream($path,'CreateNew','Write','None');try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
[pscustomobject]@{Accepted=$accepted;Command=$ExpectedCommand;Click="($($events[0].clientX),$($events[0].clientY))";FinalLayer=$after.Layer.ToString();FinalCityId=$afterCity;EvidencePath=$path}|Format-List
if(-not$accepted){exit 2}
