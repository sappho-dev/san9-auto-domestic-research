[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfirmCompileOnly,

    [Parameter(Mandatory = $true)]
    [string]$ConfirmDynamicNoGo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-ExactBuildConfirmation {
    param(
        [Parameter(Mandatory = $true)][string]$CompileOnly,
        [Parameter(Mandatory = $true)][string]$DynamicNoGo
    )
    return [string]::Equals(
        $CompileOnly,
        'I_ACCEPT_COMPILE_ONLY_DO_NOT_RUN_LIVE_ARTIFACT',
        [System.StringComparison]::Ordinal
    ) -and [string]::Equals(
        $DynamicNoGo,
        'I_ACCEPT_DYNAMIC_LIVE_REMAINS_NO_GO',
        [System.StringComparison]::Ordinal
    )
}

$confirmationGateChecks = @(
    (Test-ExactBuildConfirmation `
        -CompileOnly 'I_ACCEPT_COMPILE_ONLY_DO_NOT_RUN_LIVE_ARTIFACT' `
        -DynamicNoGo 'I_ACCEPT_DYNAMIC_LIVE_REMAINS_NO_GO'),
    (-not (Test-ExactBuildConfirmation `
        -CompileOnly 'I_ACCEPT_COMPILE_ONLY_DO_NOT_RUN_LIVE_ARTIFACT ' `
        -DynamicNoGo 'I_ACCEPT_DYNAMIC_LIVE_REMAINS_NO_GO')),
    (-not (Test-ExactBuildConfirmation `
        -CompileOnly 'I_ACCEPT_COMPILE_ONLY_DO_NOT_RUN_LIVE_ARTIFACT' `
        -DynamicNoGo 'I_ACCEPT_DYNAMIC_LIVE'))
)
if ($confirmationGateChecks -contains $false) {
    throw 'synthetic live build confirmation gate self-test failed'
}
if (-not (Test-ExactBuildConfirmation `
        -CompileOnly $ConfirmCompileOnly `
        -DynamicNoGo $ConfirmDynamicNoGo)) {
    throw 'exact compile-only and dynamic-NO-GO confirmations are required'
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$v52Root = Join-Path $repoRoot 'native\San9BridgeV52'
$v51Root = Join-Path $repoRoot 'native\San9BridgeV51'
$artifactRoot = Join-Path $repoRoot 'tools\artifacts\San9BridgeV52\live-optin'
$generatedRoot = Join-Path $artifactRoot 'generated'
$keyHeader = Join-Path $generatedRoot 'san9_v52_build_key.h'
$dllPath = Join-Path $artifactRoot 'San9BridgeV52Live.dll'
$controllerPath = Join-Path $artifactRoot 'San9BridgeV52LiveController.exe'
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

& $python $auditPath '--self-test-version-gates'
if ($LASTEXITCODE -ne 0) {
    throw "audit toolchain version gate failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $generatedRoot | Out-Null
$buildKey = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($buildKey)
}
finally {
    $rng.Dispose()
}
$keyBytes = ($buildKey | ForEach-Object { '0x{0:x2}' -f $_ }) -join ', '
$header = @"
#ifndef SAN9_V52_BUILD_KEY_H
#define SAN9_V52_BUILD_KEY_H
#define SAN9_V52_BUILD_ROOT_KEY_BYTES { $keyBytes }
#endif
"@
Set-Content -LiteralPath $keyHeader -Value $header -Encoding Ascii
[Array]::Clear($buildKey, 0, $buildKey.Length)

$commonArguments = @(
    'cc',
    '-target', 'x86-windows-gnu',
    '-std=c11',
    '-O2',
    '-Wall',
    '-Wextra',
    '-Werror',
    '-DSAN9_V52_LIVE_ENABLED=1',
    '-I', $generatedRoot,
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

Write-Host 'Compiling x86 live-optin DLL; it will not be loaded or executed...'
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
    throw "live-optin DLL compile failed with exit code $LASTEXITCODE"
}

Write-Host 'Compiling x86 live-optin controller; it will not be run...'
& $zig @commonArguments @(
    (Join-Path $v52Root 'src\controller.c'),
    '-o', $controllerPath,
    '-luser32',
    '-lbcrypt'
)
if ($LASTEXITCODE -ne 0) {
    throw "live-optin controller compile failed with exit code $LASTEXITCODE"
}

Write-Host 'Running on-disk live-optin PE/machine-code audit only...'
& $python $auditPath '--profile' 'live-optin' '--dll' $dllPath '--controller' $controllerPath
if ($LASTEXITCODE -ne 0) {
    throw "live-optin on-disk audit failed with exit code $LASTEXITCODE"
}

Write-Host 'COMPILE-ONLY COMPLETE: neither live-optin artifact was loaded or executed.'
Write-Host 'DYNAMIC LIVE USE REMAINS NO-GO; no game process was accessed.'
Write-Host 'The generated controller is a console prototype, not the main UI; do not double-click it.'
