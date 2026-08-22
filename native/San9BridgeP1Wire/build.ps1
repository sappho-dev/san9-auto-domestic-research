[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ArtifactRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$includeRoot = Join-Path $PSScriptRoot 'include'
$sourceRoot = Join-Path $PSScriptRoot 'src'
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repoRoot 'tools\artifacts\San9BridgeP1Wire\native'
}
$ArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
$selfTestPath = Join-Path $ArtifactRoot 'San9BridgeP1WireSelfTest.exe'
$auditPath = Join-Path $PSScriptRoot 'audit_pe.py'
$expectedAuditSha256 = 'EDAB5158DBD1ACF0E0391E70B395734C6872162BAF6E4A1B0164509D902B0DC8'
$expectedSelfTestSha256 = '354D9AA96D6CF62E3997556FC28C0A2B6D75CA505F2907763E3AC931B5A6530F'
$zig = (Get-Command zig.exe -ErrorAction Stop).Source
$python = (Get-Command python.exe -ErrorAction Stop).Source
$expectedZigVersion = '0.16.0'
$expectedZigSha256 = '086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864'
$expectedPythonVersion = '3.13.0'
$expectedPythonSha256 = '62EBC90A2884BB63A0CD67E789CAFDD51E771EEE043587E2354327B4CCC9BB05'
$expectedPythonRuntimeSha256 = '0C0A66505093B6A4BB3475F716BD3D9552095776F6A124709C13B3F9552C7D99'

if (-not (Test-Path -LiteralPath $auditPath -PathType Leaf)) {
    throw "required PE audit script not found: $auditPath"
}
$auditSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $auditPath).Hash
if (-not [string]::Equals(
        $auditSha256,
        $expectedAuditSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "PE audit script identity rejected: sha256=$auditSha256 expected=$expectedAuditSha256"
}

$zigVersion = ((& $zig version) | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "zig version failed with exit code $LASTEXITCODE"
}
$zigSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zig).Hash
if (-not [string]::Equals($zigVersion, $expectedZigVersion, [System.StringComparison]::Ordinal) -or
    -not [string]::Equals($zigSha256, $expectedZigSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw (
        "Zig identity rejected: path=$zig version=$zigVersion sha256=$zigSha256; " +
        "required version=$expectedZigVersion sha256=$expectedZigSha256"
    )
}

$pythonVersion = (& $python -I -S -c 'import platform; print(platform.python_version())' | Out-String).Trim()
$pythonSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $python).Hash
$pythonRuntime = Join-Path (Split-Path -Parent $python) 'python313.dll'
if (-not (Test-Path -LiteralPath $pythonRuntime -PathType Leaf)) {
    throw "required Python runtime DLL not found: $pythonRuntime"
}
$pythonRuntimeSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $pythonRuntime).Hash
if ($LASTEXITCODE -ne 0 -or
    -not [string]::Equals($pythonVersion, $expectedPythonVersion, [System.StringComparison]::Ordinal) -or
    -not [string]::Equals($pythonSha256, $expectedPythonSha256, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals(
        $pythonRuntimeSha256,
        $expectedPythonRuntimeSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw (
        "Python identity rejected: path=$python version=$pythonVersion sha256=$pythonSha256 " +
        "runtime_sha256=$pythonRuntimeSha256; required version=$expectedPythonVersion " +
        "sha256=$expectedPythonSha256 runtime_sha256=$expectedPythonRuntimeSha256"
    )
}

$forbiddenSourcePattern = @(
    '#\s*include\s*[<\"]windows\.h[>\"]',
    '\bOpenProcess\b',
    '\bReadProcessMemory\b',
    '\bWriteProcessMemory\b',
    '\bCreateRemoteThread\b',
    '\bVirtualAllocEx\b',
    '\bSendInput\b',
    '\bSetCursorPos\b',
    '\bCreateFileMapping',
    '\bOpenFileMapping',
    '\bMapViewOfFile\b',
    '\bCreateNamedPipe',
    '\bLoadLibrary',
    '\bGetProcAddress\b',
    '\bsocket\s*\(',
    '\bconnect\s*\(',
    '\bWinHttp',
    '\b_Thread_local\b',
    '\bthread_local\b',
    '\b__thread\b',
    '__declspec\s*\(\s*thread\s*\)'
) -join '|'
$sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.c', '.h') }
$forbiddenHits = $sourceFiles | Select-String -Pattern $forbiddenSourcePattern -CaseSensitive
if ($forbiddenHits) {
    throw "P1 wire source isolation gate failed: $($forbiddenHits | Out-String)"
}

New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null
$arguments = @(
    'cc',
    '-target', 'x86-windows-gnu',
    '-std=c11',
    '-O2',
    '-Wall',
    '-Wextra',
    '-Werror',
    '-s',
    '-I', $includeRoot,
    '-I', $sourceRoot,
    (Join-Path $sourceRoot 'sha256.c'),
    (Join-Path $sourceRoot 'p1_wire.c'),
    (Join-Path $sourceRoot 'selftest.c'),
    '-o', $selfTestPath
)

Write-Host "Building isolated x86 P1 wire self-test with Zig $zigVersion..."
& $zig @arguments
if ($LASTEXITCODE -ne 0) {
    throw "native P1 wire build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $selfTestPath -PathType Leaf)) {
    throw "native P1 wire self-test output missing: $selfTestPath"
}
$preRunSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals(
        $preRunSha256,
        $expectedSelfTestSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "native P1 wire self-test identity rejected before execution: sha256=$preRunSha256 expected=$expectedSelfTestSha256"
}

Write-Host 'Auditing the native PE boundary before executing the offline self-test...'
& $python -B $auditPath '--exe' $selfTestPath
if ($LASTEXITCODE -ne 0) {
    throw "native P1 wire PE audit failed with exit code $LASTEXITCODE"
}

Write-Host 'Running native offline contract/mutation/replay tests...'
& $selfTestPath '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "native P1 wire self-test failed with exit code $LASTEXITCODE"
}

$postRunSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals(
        $postRunSha256,
        $preRunSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "native P1 wire self-test changed during execution: before=$preRunSha256 after=$postRunSha256"
}
& $python -B $auditPath '--exe' $selfTestPath
if ($LASTEXITCODE -ne 0) {
    throw "native P1 wire post-execution PE audit failed with exit code $LASTEXITCODE"
}

Write-Host "P1WIRE_NATIVE_BUILD PASS sha256=$postRunSha256 artifact=$selfTestPath"
