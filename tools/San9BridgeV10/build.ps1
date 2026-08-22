[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$sourceRoot = Join-Path $repoRoot 'native\San9BridgeV10'
$v51Root = Join-Path $repoRoot 'native\San9BridgeV51'
$shaSource = Join-Path $v51Root 'src\sha256.c'
$shaHeader = Join-Path $v51Root 'src\sha256.h'
$expectedShaSourceHash = 'B40A27F59E986A3D80BC5F4221A965813497C71F280A3196816DF667A1D307D5'
$expectedShaHeaderHash = 'B4CA08DF1867B625027968DAE8409549D829F5B661BDBC4D66AD6B49B770C3B0'
$artifactRoot = Join-Path $repoRoot 'tools\artifacts\San9BridgeV10\offline'
$dllPath = Join-Path $artifactRoot 'San9BridgeV10.dll'
$selfTestPath = Join-Path $artifactRoot 'San9BridgeV10SelfTest.exe'
$auditPath = Join-Path $repoRoot 'tools\re\san9_v10_native_contract_audit.py'
$zig = (Get-Command zig.exe -ErrorAction Stop).Source
$python = (Get-Command python.exe -ErrorAction Stop).Source
$expectedZigVersion = '0.16.0'
$expectedZigSha256 = '086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864'

$zigVersion = ((& $zig version) | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "zig version failed with exit code $LASTEXITCODE"
}
$zigSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zig).Hash
if (-not [string]::Equals($zigVersion, $expectedZigVersion, [System.StringComparison]::Ordinal) `
    -or -not [string]::Equals($zigSha256, $expectedZigSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Zig identity rejected: version=$zigVersion sha256=$zigSha256"
}
if (-not [string]::Equals(
        (Get-FileHash -Algorithm SHA256 -LiteralPath $shaSource).Hash,
        $expectedShaSourceHash,
        [System.StringComparison]::OrdinalIgnoreCase) `
    -or -not [string]::Equals(
        (Get-FileHash -Algorithm SHA256 -LiteralPath $shaHeader).Hash,
        $expectedShaHeaderHash,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Pinned V51 SHA-256 implementation identity rejected.'
}

$forbiddenSourceTokens = @(
    'OpenProcess',
    'ReadProcessMemory',
    'WriteProcessMemory',
    'VirtualAllocEx',
    'VirtualProtectEx',
    'CreateRemoteThread',
    'SetWindowsHookEx',
    'SendInput',
    'PostMessage',
    'SendMessage',
    'CreateToolhelp32Snapshot'
)
$sourceFiles = @(
    @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
        Where-Object { $_.Extension -in @('.c', '.h', '.def') })
    @(Get-Item -LiteralPath $shaSource)
    @(Get-Item -LiteralPath $shaHeader)
)
foreach ($token in $forbiddenSourceTokens) {
    $matches = $sourceFiles | Select-String -SimpleMatch -Pattern $token
    if ($matches) {
        throw "Forbidden capability token '$token' found in V10 source."
    }
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$common = @(
    'cc',
    '-target', 'x86-windows-gnu',
    '-std=c11',
    '-O2',
    '-Wall',
    '-Wextra',
    '-Werror',
    '-I', (Join-Path $sourceRoot 'include'),
    '-I', (Join-Path $v51Root 'src'),
    $shaSource,
    (Join-Path $sourceRoot 'src\native_contract.c')
)

Write-Host 'Building x86 offline V10 contract DLL...'
& $zig @common @(
    '-shared',
    (Join-Path $sourceRoot 'San9BridgeV10.def'),
    '-o', $dllPath
)
if ($LASTEXITCODE -ne 0) {
    throw "V10 DLL build failed with exit code $LASTEXITCODE"
}

Write-Host 'Building x86 offline V10 self-test...'
& $zig @common @(
    '-DSAN9_V10_NO_EXPORTS',
    (Join-Path $sourceRoot 'src\selftest_main.c'),
    '-o', $selfTestPath
)
if ($LASTEXITCODE -ne 0) {
    throw "V10 self-test build failed with exit code $LASTEXITCODE"
}

Write-Host 'Running bounded offline V10 tests...'
& $selfTestPath '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "V10 self-test failed with exit code $LASTEXITCODE"
}

Write-Host 'Auditing offline V10 PE artifacts...'
& $python $auditPath '--dll' $dllPath '--selftest' $selfTestPath
if ($LASTEXITCODE -ne 0) {
    throw "V10 PE audit failed with exit code $LASTEXITCODE"
}

Write-Host "V10 offline contract verified: $artifactRoot"
