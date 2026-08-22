[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ArtifactRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$manifestPath = Join-Path $repoRoot 'docs\easy-compatibility-manifest.json'
$generatorPath = Join-Path $repoRoot 'tools\re\generate_p1_native_easy_manifest.py'
$includeRoot = Join-Path $PSScriptRoot 'include'
$sourceRoot = Join-Path $PSScriptRoot 'src'
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repoRoot 'tools\artifacts\San9BridgeP1EasyPing'
}
$ArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
$generatedIncludeRoot = Join-Path $ArtifactRoot 'staging\include'
$generatedHeaderPath = Join-Path $generatedIncludeRoot 'san9_p1_easy_manifest.gen.h'
$selfTestPath = Join-Path $ArtifactRoot 'San9BridgeP1EasyPingSelfTest.exe'
$determinismRoot = Join-Path $ArtifactRoot 'determinism-second-root'
$secondSelfTestPath = Join-Path $determinismRoot 'San9BridgeP1EasyPingSelfTest.exe'
$auditPath = Join-Path $PSScriptRoot 'audit_pe.py'
$expectedAuditSha256 = 'B5748E658BFE8F96E29E3C7C44E08F4B7A593B1175147CF00D652C23B7947745'
$expectedSelfTestSha256 = 'E6BC0FEB88FA280A879072CCD48F39E9A0FF02238268580CFA9631F724BB65AF'
$zig = (Get-Command zig.exe -ErrorAction Stop).Source
$python = (Get-Command python.exe -ErrorAction Stop).Source

$expectedZigVersion = '0.16.0'
$expectedZigSha256 = '086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864'
$expectedPythonVersion = 'Python 3.13.0'
$expectedPythonSha256 = '62EBC90A2884BB63A0CD67E789CAFDD51E771EEE043587E2354327B4CCC9BB05'

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
if ($LASTEXITCODE -ne 0) { throw "zig version failed with exit code $LASTEXITCODE" }
$zigSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zig).Hash
if (-not [string]::Equals($zigVersion, $expectedZigVersion, [System.StringComparison]::Ordinal) -or
    -not [string]::Equals($zigSha256, $expectedZigSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Zig identity rejected: version=$zigVersion sha256=$zigSha256"
}
$pythonVersion = ((& $python '--version' 2>&1) | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "python version failed with exit code $LASTEXITCODE" }
$pythonSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $python).Hash
if (-not [string]::Equals($pythonVersion, $expectedPythonVersion, [System.StringComparison]::Ordinal) -or
    -not [string]::Equals($pythonSha256, $expectedPythonSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Python identity rejected: version=$pythonVersion sha256=$pythonSha256"
}

$forbiddenSourcePattern = @(
    '#\s*include\s*[<\"]windows\.h[>\"]',
    '\bOpenProcess\b',
    '\bReadProcessMemory\b',
    '\bWriteProcessMemory\b',
    '\bVirtualProtectEx\b',
    '\bVirtualAllocEx\b',
    '\bCreateRemoteThread\b',
    '\bQueueUserAPC\b',
    '\bSetWindowsHookEx\b',
    '\bSendInput\b',
    '\bmouse_event\b',
    '\bkeybd_event\b',
    '\bCreateFileMapping\b',
    '\bOpenFileMapping\b',
    '\bMapViewOfFile\b',
    '\bCreateNamedPipe\b',
    '\bsocket\s*\(',
    '\bconnect\s*\('
) -join '|'
$sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.c', '.h') }
$forbiddenHits = $sourceFiles | Select-String -Pattern $forbiddenSourcePattern -CaseSensitive
if ($forbiddenHits) {
    throw "P1 native Easy M1 source isolation gate failed: $($forbiddenHits | Out-String)"
}

New-Item -ItemType Directory -Force -Path $generatedIncludeRoot,$determinismRoot | Out-Null
Write-Host 'Running generator authentication/overflow/determinism self-tests...'
& $python $generatorPath '--manifest' $manifestPath '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "native Easy manifest generator self-test failed with exit code $LASTEXITCODE"
}
Write-Host 'Generating the private native table from authenticated manifest and game evidence...'
& $python $generatorPath '--manifest' $manifestPath '--output' $generatedHeaderPath
if ($LASTEXITCODE -ne 0) {
    throw "native Easy manifest generation failed with exit code $LASTEXITCODE"
}

$arguments = @(
    'cc',
    '-target', 'x86-windows-gnu',
    '-std=c11',
    '-O2',
    '-Wall',
    '-Wextra',
    '-Wpedantic',
    '-Werror',
    '-s',
    '-I', $includeRoot,
    '-I', $generatedIncludeRoot,
    (Join-Path $sourceRoot 'easy_gate.c'),
    (Join-Path $sourceRoot 'selftest.c')
)

Write-Host "Building isolated x86 C11 M1 self-test in artifact root A with Zig $zigVersion..."
& $zig @arguments '-o' $selfTestPath
if ($LASTEXITCODE -ne 0) {
    throw "native Easy M1 build A failed with exit code $LASTEXITCODE"
}
Write-Host 'Building the same sources independently in artifact root B...'
& $zig @arguments '-o' $secondSelfTestPath
if ($LASTEXITCODE -ne 0) {
    throw "native Easy M1 build B failed with exit code $LASTEXITCODE"
}

$compiledSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
$secondCompiledSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $secondSelfTestPath).Hash
if (-not [string]::Equals(
        $compiledSha256,
        $secondCompiledSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "independent artifact roots are not byte-identical: A=$compiledSha256 B=$secondCompiledSha256"
}
if (-not [string]::Equals(
        $compiledSha256,
        $expectedSelfTestSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "whole offline self-test identity rejected: actual=$compiledSha256 expected=$expectedSelfTestSha256"
}
$pipelineStage = 'DUAL_BUILD_IDENTITY_FROZEN_UNAUDITED'
Write-Host "P1_NATIVE_EASY_STAGE $pipelineStage sha256=$compiledSha256"
if (-not [string]::Equals(
        $pipelineStage,
        'DUAL_BUILD_IDENTITY_FROZEN_UNAUDITED',
        [System.StringComparison]::Ordinal)) {
    throw "internal pipeline marker mismatch before pre-execution PE audit"
}

Write-Host 'Auditing the x86 offline PE before any execution...'
& $python $auditPath '--exe' $selfTestPath '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "native Easy M1 pre-execution PE audit failed with exit code $LASTEXITCODE"
}
$preExecutionSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals(
        $compiledSha256,
        $preExecutionSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "self-test PE changed during pre-execution audit"
}
$pipelineStage = 'PREEXEC_PE_AUDITED'
Write-Host "P1_NATIVE_EASY_STAGE $pipelineStage sha256=$preExecutionSha256"
if (-not [string]::Equals($pipelineStage, 'PREEXEC_PE_AUDITED', [System.StringComparison]::Ordinal)) {
    throw "internal pipeline marker does not authorize offline self-test execution"
}

Write-Host 'Running offline buffer/callback/A-B/point/byte tests after PE authorization...'
& $selfTestPath
if ($LASTEXITCODE -ne 0) {
    throw "native Easy M1 self-test failed with exit code $LASTEXITCODE"
}
$postExecutionSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals(
        $preExecutionSha256,
        $postExecutionSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "self-test PE changed during execution"
}
$pipelineStage = 'SELFTEST_EXECUTED_HASH_STABLE'
Write-Host "P1_NATIVE_EASY_STAGE $pipelineStage sha256=$postExecutionSha256"

Write-Host 'Re-auditing the identical PE after execution...'
& $python $auditPath '--exe' $selfTestPath
if ($LASTEXITCODE -ne 0) {
    throw "native Easy M1 post-execution PE audit failed with exit code $LASTEXITCODE"
}
$finalSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals(
        $compiledSha256,
        $finalSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "self-test PE final identity differs from the compiled/audited image"
}
$pipelineStage = 'POSTEXEC_PE_REAUDITED'
Write-Host "P1_NATIVE_EASY_STAGE $pipelineStage sha256=$finalSha256"

$headerSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedHeaderPath).Hash
$exeSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals($pipelineStage, 'POSTEXEC_PE_REAUDITED', [System.StringComparison]::Ordinal)) {
    throw "internal pipeline did not reach the final audited marker"
}
Write-Host "P1_NATIVE_EASY_BUILD PASS stage=$pipelineStage header_sha256=$headerSha256 exe_sha256=$exeSha256 artifact=$selfTestPath"
