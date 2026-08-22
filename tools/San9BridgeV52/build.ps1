[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$v52Root = Join-Path $repoRoot 'native\San9BridgeV52'
$v51Root = Join-Path $repoRoot 'native\San9BridgeV51'
$artifactRoot = Join-Path $repoRoot 'tools\artifacts\San9BridgeV52\offline'
$dllPath = Join-Path $artifactRoot 'San9BridgeV52.dll'
$controllerPath = Join-Path $artifactRoot 'San9BridgeV52SelfTest.exe'
$auditPath = Join-Path $repoRoot 'tools\re\san9_v52_native_audit.py'
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
& $python $auditPath '--self-test-version-gates'
if ($LASTEXITCODE -ne 0) {
    throw "audit toolchain version gate failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$commonArguments = @(
    'cc',
    '-target', 'x86-windows-gnu',
    '-std=c11',
    '-O2',
    '-Wall',
    '-Wextra',
    '-Werror',
    '-I', (Join-Path $v52Root 'include'),
    '-I', (Join-Path $v52Root 'src'),
    '-I', (Join-Path $v51Root 'include'),
    '-I', (Join-Path $v51Root 'src'),
    (Join-Path $v51Root 'src\sha256.c'),
    (Join-Path $v51Root 'src\protocol.c'),
    (Join-Path $v51Root 'src\selftest.c'),
    (Join-Path $v52Root 'src\bootstrap_protocol.c'),
    (Join-Path $v52Root 'src\lifecycle.c'),
    (Join-Path $v52Root 'src\synthetic.c'),
    (Join-Path $v52Root 'src\live_support.c')
)

Write-Host 'Building default x86 offline V5.2 DLL...'
& $zig @commonArguments @(
    '-shared',
    '-DSAN9_EXPORTS_VIA_DEF',
    (Join-Path $v52Root 'src\bridge_dll.c'),
    (Join-Path $v52Root 'src\idle_bridge.S'),
    (Join-Path $v52Root 'San9BridgeV52.def'),
    '-o', $dllPath,
    '-luser32'
)
if ($LASTEXITCODE -ne 0) {
    throw "offline DLL build failed with exit code $LASTEXITCODE"
}

Write-Host 'Building default x86 offline V5.2 self-test controller...'
& $zig @commonArguments @(
    (Join-Path $v52Root 'src\controller.c'),
    '-o', $controllerPath,
    '-luser32'
)
if ($LASTEXITCODE -ne 0) {
    throw "offline controller build failed with exit code $LASTEXITCODE"
}

Write-Host 'Running default synthetic self-test (no game access)...'
& $controllerPath '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "offline self-test failed with exit code $LASTEXITCODE"
}

Write-Host 'Running on-disk offline PE and wrapper audit...'
& $python $auditPath '--profile' 'offline' '--dll' $dllPath '--controller' $controllerPath
if ($LASTEXITCODE -ne 0) {
    throw "offline PE audit failed with exit code $LASTEXITCODE"
}

Write-Host "V5.2 default offline artifacts verified: $artifactRoot"
