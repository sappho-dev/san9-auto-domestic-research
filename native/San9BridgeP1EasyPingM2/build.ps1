[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ArtifactRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$wireRoot = Join-Path $repoRoot 'native\San9BridgeP1Wire'
$easyRoot = Join-Path $repoRoot 'native\San9BridgeP1EasyPing'
$manifestPath = Join-Path $repoRoot 'docs\easy-compatibility-manifest.json'
$generatorPath = Join-Path $repoRoot 'tools\re\generate_p1_native_easy_manifest.py'
$auditPath = Join-Path $PSScriptRoot 'audit_pe.py'
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repoRoot 'tools\artifacts\San9BridgeP1EasyPingM2'
}
$ArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
$generatedIncludeRoot = Join-Path $ArtifactRoot 'staging\include'
$generatedHeaderPath = Join-Path $generatedIncludeRoot 'san9_p1_easy_manifest.gen.h'
$selfTestPath = Join-Path $ArtifactRoot 'San9BridgeP1EasyPingM2SelfTest.exe'
$secondRoot = Join-Path $ArtifactRoot 'determinism-second-root'
$secondSelfTestPath = Join-Path $secondRoot 'San9BridgeP1EasyPingM2SelfTest.exe'

$expectedAuditSha256 = '0CEF442329574FE7CB05D437F5ABC8D3395604CA4F14974798A7EEF5D07EA2A4'
$expectedHeaderSha256 = 'CCA306579C23581A660CD6972CC37104DBE5339094DDD841FED8C91A40E98F7A'
$expectedSelfTestSha256 = '398DDAC22B75E4088548BF2954A902390F6DD45AB86B7DC5DD178947D31EA46E'
$expectedZigVersion = '0.16.0'
$expectedZigSha256 = '086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864'
$expectedPythonVersion = 'Python 3.13.0'
$expectedPythonSha256 = '62EBC90A2884BB63A0CD67E789CAFDD51E771EEE043587E2354327B4CCC9BB05'

$pinnedDependencies = @(
    @{ Path = (Join-Path $wireRoot 'include\san9_p1_wire.h'); Hash = 'FA109C829A09EB5D47034D1CD2329357C27C2A3969621E3AB04168AC5E4550B5' },
    @{ Path = (Join-Path $wireRoot 'src\p1_wire.c'); Hash = 'AE891EC63E3200430CF0759C36B44137065153831299FFE2A813802208A432B6' },
    @{ Path = (Join-Path $wireRoot 'src\sha256.h'); Hash = '07434B03911299F4A977C95BF7816D0FD82D7A48D2894D008FBD888E7849CEF1' },
    @{ Path = (Join-Path $wireRoot 'src\sha256.c'); Hash = 'C10F015DD3C60AB82AD1440752E69F977D11CB26F94DC1FAD883D020E77CF97D' },
    @{ Path = (Join-Path $easyRoot 'include\san9_p1_easy_gate.h'); Hash = 'AC74F16B23659162BA30229C4B3469FEBA603CB9E54CC986C75EC80531527B5C' },
    @{ Path = (Join-Path $easyRoot 'src\easy_gate.c'); Hash = 'E29BE243DF07FBF0E2878CD06C7553D2D1DF2A251FB10758734186A4A4C12744' },
    @{ Path = $generatorPath; Hash = '8006B8C556623C01FE505E7E6A1EAB23BAD655B17C3FA9DCF4C0C0DCEA43F4AA' },
    @{ Path = $manifestPath; Hash = '72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE' }
)

if (-not (Test-Path -LiteralPath $auditPath -PathType Leaf)) {
    throw "required M2a PE audit script not found: $auditPath"
}
$auditSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $auditPath).Hash
if (-not [string]::Equals($auditSha256, $expectedAuditSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "M2a PE audit identity rejected: actual=$auditSha256 expected=$expectedAuditSha256"
}
foreach ($dependency in $pinnedDependencies) {
    if (-not (Test-Path -LiteralPath $dependency.Path -PathType Leaf)) {
        throw "frozen dependency missing: $($dependency.Path)"
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $dependency.Path).Hash
    if (-not [string]::Equals($actual, $dependency.Hash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "frozen dependency identity rejected: path=$($dependency.Path) actual=$actual expected=$($dependency.Hash)"
    }
}

$zig = (Get-Command zig.exe -ErrorAction Stop).Source
$python = (Get-Command python.exe -ErrorAction Stop).Source
$zigVersion = ((& $zig version) | Out-String).Trim()
$zigSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zig).Hash
if (($LASTEXITCODE -ne 0) -or
    (-not [string]::Equals($zigVersion, $expectedZigVersion, [System.StringComparison]::Ordinal)) -or
    (-not [string]::Equals($zigSha256, $expectedZigSha256, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "Zig identity rejected: version=$zigVersion sha256=$zigSha256"
}
$pythonVersion = ((& $python '--version' 2>&1) | Out-String).Trim()
$pythonSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $python).Hash
if (($LASTEXITCODE -ne 0) -or
    (-not [string]::Equals($pythonVersion, $expectedPythonVersion, [System.StringComparison]::Ordinal)) -or
    (-not [string]::Equals($pythonSha256, $expectedPythonSha256, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "Python identity rejected: version=$pythonVersion sha256=$pythonSha256"
}

$forbiddenSourcePattern = @(
    '#\s*include\s*[<"]windows\.h[>"]',
    '\bOpenProcess\b',
    '\bReadProcessMemory\b',
    '\bWriteProcessMemory\b',
    '\bVirtualProtectEx\b',
    '\bVirtualAllocEx\b',
    '\bCreateRemoteThread\b',
    '\bQueueUserAPC\b',
    '\bSetWindowsHookEx\b',
    '\bSendInput\b',
    '\bCreateFileMapping\b',
    '\bOpenFileMapping\b',
    '\bMapViewOfFile\b',
    '\bCreateNamedPipe\b',
    '\bsocket\s*\(',
    '\bconnect\s*\('
) -join '|'
$sourceFiles = @(
    (Join-Path $PSScriptRoot 'include\san9_p1_m2.h'),
    (Join-Path $PSScriptRoot 'src\m2.c'),
    (Join-Path $PSScriptRoot 'src\selftest.c'),
    (Join-Path $wireRoot 'include\san9_p1_wire.h'),
    (Join-Path $wireRoot 'src\p1_wire.c'),
    (Join-Path $wireRoot 'src\sha256.h'),
    (Join-Path $wireRoot 'src\sha256.c'),
    (Join-Path $easyRoot 'include\san9_p1_easy_gate.h'),
    (Join-Path $easyRoot 'src\easy_gate.c')
)
$forbiddenHits = $sourceFiles | Select-String -Pattern $forbiddenSourcePattern -CaseSensitive
if ($forbiddenHits) {
    throw "M2a source isolation gate failed: $($forbiddenHits | Out-String)"
}

New-Item -ItemType Directory -Force -Path $generatedIncludeRoot,$secondRoot | Out-Null
Write-Host 'Running authenticated Easy manifest generator self-tests...'
& $python $generatorPath '--manifest' $manifestPath '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "M2a manifest generator self-test failed with exit code $LASTEXITCODE"
}
Write-Host 'Generating the private Easy table in the ignored M2a staging root...'
& $python $generatorPath '--manifest' $manifestPath '--output' $generatedHeaderPath
if ($LASTEXITCODE -ne 0) {
    throw "M2a manifest generation failed with exit code $LASTEXITCODE"
}
$headerSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedHeaderPath).Hash
if (-not [string]::Equals($headerSha256, $expectedHeaderSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "generated Easy header identity rejected: actual=$headerSha256 expected=$expectedHeaderSha256"
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
    '-I', (Join-Path $PSScriptRoot 'include'),
    '-I', (Join-Path $wireRoot 'include'),
    '-I', (Join-Path $wireRoot 'src'),
    '-I', (Join-Path $easyRoot 'include'),
    '-I', $generatedIncludeRoot,
    (Join-Path $PSScriptRoot 'src\m2.c'),
    (Join-Path $PSScriptRoot 'src\selftest.c'),
    (Join-Path $wireRoot 'src\p1_wire.c'),
    (Join-Path $wireRoot 'src\sha256.c'),
    (Join-Path $easyRoot 'src\easy_gate.c')
)

Write-Host "Building isolated x86 C11 M2a self-test in artifact root A with Zig $zigVersion..."
& $zig @arguments '-o' $selfTestPath
if ($LASTEXITCODE -ne 0) { throw "M2a build A failed with exit code $LASTEXITCODE" }
Write-Host 'Building identical sources independently in artifact root B...'
& $zig @arguments '-o' $secondSelfTestPath
if ($LASTEXITCODE -ne 0) { throw "M2a build B failed with exit code $LASTEXITCODE" }

$compiledSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
$secondSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $secondSelfTestPath).Hash
if (-not [string]::Equals($compiledSha256, $secondSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "M2a independent roots differ: A=$compiledSha256 B=$secondSha256"
}
if (-not [string]::Equals($compiledSha256, $expectedSelfTestSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "M2a whole offline image identity rejected: actual=$compiledSha256 expected=$expectedSelfTestSha256"
}
$pipelineStage = 'DUAL_BUILD_IDENTITY_FROZEN_UNAUDITED'
Write-Host "P1_M2A_STAGE $pipelineStage sha256=$compiledSha256"

Write-Host 'Auditing the M2a PE before any execution...'
& $python $auditPath '--exe' $selfTestPath '--self-test'
if ($LASTEXITCODE -ne 0) { throw "M2a pre-execution PE audit failed with exit code $LASTEXITCODE" }
$preExecutionSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals($compiledSha256, $preExecutionSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'M2a image changed during pre-execution audit'
}
$pipelineStage = 'PREEXEC_PE_AUDITED'
Write-Host "P1_M2A_STAGE $pipelineStage sha256=$preExecutionSha256"
if (-not [string]::Equals($pipelineStage, 'PREEXEC_PE_AUDITED',
        [System.StringComparison]::Ordinal)) {
    throw 'M2a pipeline marker does not authorize self-test execution'
}

Write-Host 'Running the audited offline M2a state-machine self-test...'
& $selfTestPath
if ($LASTEXITCODE -ne 0) { throw "M2a self-test failed with exit code $LASTEXITCODE" }
$postExecutionSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals($preExecutionSha256, $postExecutionSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'M2a image changed during self-test execution'
}
$pipelineStage = 'SELFTEST_EXECUTED_HASH_STABLE'
Write-Host "P1_M2A_STAGE $pipelineStage sha256=$postExecutionSha256"

Write-Host 'Re-auditing the identical M2a PE after execution...'
& $python $auditPath '--exe' $selfTestPath
if ($LASTEXITCODE -ne 0) { throw "M2a post-execution PE audit failed with exit code $LASTEXITCODE" }
$finalSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfTestPath).Hash
if (-not [string]::Equals($compiledSha256, $finalSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'M2a final image identity differs from the compiled/audited image'
}
$pipelineStage = 'POSTEXEC_PE_REAUDITED'
Write-Host "P1_M2A_STAGE $pipelineStage sha256=$finalSha256"
Write-Host "P1_M2A_BUILD PASS stage=$pipelineStage header_sha256=$headerSha256 exe_sha256=$finalSha256 artifact=$selfTestPath"
