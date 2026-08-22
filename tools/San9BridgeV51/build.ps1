[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$nativeRoot = Join-Path $repoRoot 'native\San9BridgeV51'
$includeRoot = Join-Path $nativeRoot 'include'
$sourceRoot = Join-Path $nativeRoot 'src'
$artifactRoot = Join-Path $repoRoot 'tools\artifacts\San9BridgeV51'
$dllPath = Join-Path $artifactRoot 'San9BridgeV51.dll'
$controllerPath = Join-Path $artifactRoot 'San9BridgeV51SelfTest.exe'
$auditPath = Join-Path $repoRoot 'tools\re\san9_v51_native_audit.py'
$zig = (Get-Command zig.exe -ErrorAction Stop).Source
$python = (Get-Command python.exe -ErrorAction Stop).Source
$expectedZigVersion = '0.16.0'
$expectedZigSha256 = '086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864'

function Test-ExactZigIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Sha256
    )
    return [string]::Equals(
        $Version.Trim(),
        $expectedZigVersion,
        [System.StringComparison]::Ordinal
    ) -and [string]::Equals(
        $Sha256.Trim(),
        $expectedZigSha256,
        [System.StringComparison]::OrdinalIgnoreCase
    )
}

$zigVersion = ((& $zig version) | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "zig version failed with exit code $LASTEXITCODE"
}
$zigSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zig).Hash
$zigGateChecks = @(
    (Test-ExactZigIdentity -Version $expectedZigVersion -Sha256 $expectedZigSha256),
    (-not (Test-ExactZigIdentity -Version '0.15.2' -Sha256 $expectedZigSha256)),
    (-not (Test-ExactZigIdentity -Version '0.16.0-dev.1' -Sha256 $expectedZigSha256)),
    (-not (Test-ExactZigIdentity -Version '0.16.1' -Sha256 $expectedZigSha256)),
    (-not (Test-ExactZigIdentity -Version $expectedZigVersion -Sha256 ('0' * 64)))
)
if ($zigGateChecks -contains $false) {
    throw 'synthetic Zig identity gate self-test failed'
}
if (-not (Test-ExactZigIdentity -Version $zigVersion -Sha256 $zigSha256)) {
    throw (
        "Zig identity rejected: path=$zig version=$zigVersion sha256=$zigSha256; " +
        "required version=$expectedZigVersion sha256=$expectedZigSha256"
    )
}

Write-Host "Zig gate: 5/5, path=$zig, version=$zigVersion, sha256=$zigSha256"
Write-Host 'Checking Python/pefile actual and synthetic version gates...'
& $python $auditPath '--self-test-version-gates'
if ($LASTEXITCODE -ne 0) {
    throw "audit toolchain version gate failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$commonArguments = @(
    '-target', 'x86-windows-gnu',
    '-std=c11',
    '-O2',
    '-Wall',
    '-Wextra',
    '-Werror',
    '-I', $includeRoot,
    '-I', $sourceRoot,
    (Join-Path $sourceRoot 'sha256.c'),
    (Join-Path $sourceRoot 'protocol.c'),
    (Join-Path $sourceRoot 'selftest.c')
)

$dllArguments = @(
    'cc'
) + $commonArguments + @(
    '-shared',
    '-DSAN9_EXPORTS_VIA_DEF',
    (Join-Path $sourceRoot 'bridge_dll.c'),
    (Join-Path $nativeRoot 'San9BridgeV51.def'),
    '-o', $dllPath,
    '-luser32'
)

$controllerArguments = @(
    'cc'
) + $commonArguments + @(
    (Join-Path $sourceRoot 'controller.c'),
    '-o', $controllerPath
)

Write-Host 'Building x86 offline ping-bootstrap proof DLL...'
& $zig @dllArguments
if ($LASTEXITCODE -ne 0) {
    throw "DLL build failed with exit code $LASTEXITCODE"
}

Write-Host 'Building x86 offline self-test controller...'
& $zig @controllerArguments
if ($LASTEXITCODE -ne 0) {
    throw "controller build failed with exit code $LASTEXITCODE"
}

Write-Host 'Running isolated synthetic self-test (no game process access)...'
& $controllerPath '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "offline self-test failed with exit code $LASTEXITCODE"
}

Write-Host 'Auditing PE32 architecture, exports, and forbidden imports...'
& $python $auditPath '--dll' $dllPath '--controller' $controllerPath
if ($LASTEXITCODE -ne 0) {
    throw "native PE audit failed with exit code $LASTEXITCODE"
}

Write-Host "V5.1 offline artifacts verified: $artifactRoot"
