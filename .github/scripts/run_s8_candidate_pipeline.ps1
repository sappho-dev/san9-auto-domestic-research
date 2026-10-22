[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ArtifactRoot = (Join-Path $env:RUNNER_TEMP 'san9-native'),

    [Parameter(Mandatory = $false)]
    [string]$CandidateZip = (Join-Path $env:RUNNER_TEMP 'San9-S8-cross-step-candidate.zip'),

    [Parameter(Mandatory = $false)]
    [string]$OutputFile = $env:GITHUB_OUTPUT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Branch = 'fix/s8-cross-step-bound-city'
$RepoRoot = (git rev-parse --show-toplevel).Trim()
Set-Location $RepoRoot

function Assert-LastCommand([string]$Message) {
    if (-not $?) { throw $Message }
}

function Write-OutputValue([string]$Name, [string]$Value) {
    if ([string]::IsNullOrWhiteSpace($OutputFile)) { return }
    "$Name=$Value" | Out-File -LiteralPath $OutputFile -Append -Encoding utf8
}

function Get-FileDigestMap([string[]]$Paths) {
    $result = [ordered]@{}
    foreach ($relative in $Paths) {
        if (-not (Test-Path -LiteralPath $relative -PathType Leaf)) {
            $result[$relative] = '<missing>'
        }
        else {
            $result[$relative] = (Get-FileHash -Algorithm SHA256 -LiteralPath $relative).Hash.ToLowerInvariant()
        }
    }
    return $result
}

function Assert-DigestMapsEqual($Before, $After, [string]$Context) {
    foreach ($key in $Before.Keys) {
        if (-not $After.Contains($key) -or $Before[$key] -ne $After[$key]) {
            throw "$Context changed critical path '$key': before=$($Before[$key]) after=$($After[$key])"
        }
    }
}

function Add-IfPresent([string[]]$Paths) {
    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path) { git add -- $path }
    }
}

function Commit-IfNeeded([string]$Message) {
    git diff --cached --check
    Assert-LastCommand 'git diff --cached --check failed'
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        git commit -m $Message
        Assert-LastCommand "Failed to commit: $Message"
        return $true
    }
    return $false
}

if ((git branch --show-current).Trim() -ne $Branch) {
    throw "S8 candidate pipeline must run on $Branch"
}
git config core.autocrlf false
git config core.safecrlf false
git config core.eol lf
if ((git rev-parse HEAD).Trim() -eq (git rev-parse origin/main).Trim()) {
    throw 'S8 candidate pipeline refuses to operate at origin/main'
}

Write-Host '== Apply/verify plaintext source patch =='
python .github/scripts/apply_s8_function_patch.py
Assert-LastCommand 'S8 plaintext source patch failed'
python -m py_compile `
    .github/scripts/apply_s8_fix.baseline_1549d62.py `
    .github/scripts/materialize_s8_function_patch_v3.py `
    .github/scripts/apply_s8_function_patch.py `
    .github/scripts/materialize_s8_postbuild_patch.py `
    .github/scripts/apply_s8_postbuild_patch_v2.py `
    tests/verify_s8_bound_path_matrix_v2.py
Assert-LastCommand 'Python patch/test source compilation failed'
git diff --check
Assert-LastCommand 'Source patch introduced whitespace errors'
git apply --check --reverse --recount --whitespace=nowarn `
    .github/patches/s8-cross-step-bound-city.function-context.patch
Assert-LastCommand 'Stored S8 function-context patch is not the reverse of the working tree'

Write-Host '== Verify bound reader A/B x Controller/Bridge =='
python tests/verify_s8_bound_path_matrix_v2.py `
    --report .github/audit/s8-bound-reader-consumer-matrix.json
Assert-LastCommand 'S8 four-path matrix verification failed'

Write-Host '== Create local source checkpoint for isolated postbuild worktrees =='
git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add -u
Add-IfPresent @(
    '.github/scripts/apply_s8_fix.baseline_1549d62.py',
    '.github/scripts/apply_s8_fix.recovery.txt',
    '.github/patches/s8-cross-step-bound-city.function-context.patch',
    '.github/audit/s8-bound-reader-consumer-matrix.json'
)
[void](Commit-IfNeeded 'test: local S8 source and path-matrix checkpoint [skip ci]')

Write-Host '== Bootstrap deterministic native artifacts =='
$NativeBootstrapLog = Join-Path $env:RUNNER_TEMP 'native-bootstrap.log'
$NativeFinalLog = Join-Path $env:RUNNER_TEMP 'native-final.log'
$RootBuildLog = Join-Path $env:RUNNER_TEMP 'root-build.log'
Remove-Item -Recurse -Force $ArtifactRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null
$BootstrapFailed = $false
$BootstrapError = $null
try {
    & .\native\San9BridgeP1EasyPingM2b\build.ps1 -ArtifactRoot $ArtifactRoot *>&1 |
        Tee-Object -FilePath $NativeBootstrapLog
    if (-not $?) { throw 'Native bootstrap returned failure' }
}
catch {
    $BootstrapFailed = $true
    $BootstrapError = $_
    $_ | Out-String | Tee-Object -FilePath $NativeBootstrapLog -Append
}

$RequiredNative = @(
    'offline.exe',
    'controller.exe',
    'bridge.dll',
    'bridge_apply_once.dll',
    'bridge_s6_cultivate_apply_once.dll',
    'bridge_s6_patrol_apply_once.dll',
    'bridge_s6_train_apply_once.dll',
    'bridge_s6_repair_apply_once.dll',
    'bridge_s8_basic_batch.dll',
    'bridge_s8_wealthy_batch.dll'
)
$MissingBootstrap = @($RequiredNative | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $ArtifactRoot $_) -PathType Leaf)
})
if ($MissingBootstrap.Count -ne 0) {
    throw "Native bootstrap artifacts missing: $($MissingBootstrap -join ', ')"
}
if ($BootstrapFailed) {
    $BootstrapText = [IO.File]::ReadAllText($NativeBootstrapLog)
    if ($BootstrapText -notmatch 'deterministic whole image rejected') {
        throw $BootstrapError
    }
    python .github/scripts/apply_s8_postbuild_patch_v2.py outputs --artifact-root $ArtifactRoot
    Assert-LastCommand 'Deterministic output-hash patch failed'
    git diff --check
    Assert-LastCommand 'Output-hash patch introduced whitespace errors'
}
else {
    Write-Host 'Existing deterministic hashes already match the pinned build.'
}

Write-Host '== Final native build and offline suite =='
Remove-Item -Recurse -Force $ArtifactRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null
& .\native\San9BridgeP1EasyPingM2b\build.ps1 -ArtifactRoot $ArtifactRoot *>&1 |
    Tee-Object -FilePath $NativeFinalLog
if (-not $?) { throw 'Final native build/offline test suite failed' }
foreach ($name in $RequiredNative) {
    $path = Join-Path $ArtifactRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Final native artifact missing: $name"
    }
    Write-Host "$name sha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash)"
}

Write-Host '== Full product build and regression suite =='
& .\build.ps1 *>&1 | Tee-Object -FilePath $RootBuildLog
if (-not $?) { throw 'Product build/regression suite failed' }

Write-Host '== Materialize state evidence =='
python .github/scripts/apply_s8_postbuild_patch_v2.py state `
    --artifact-root $ArtifactRoot `
    --native-log $NativeFinalLog `
    --root-log $RootBuildLog
Assert-LastCommand 'State evidence patch failed'
git diff --check
Assert-LastCommand 'State evidence introduced whitespace errors'

Write-Host '== Remove abandoned payload route and obsolete workflows =='
git rm -f --ignore-unmatch -- `
    .github/scripts/apply_s8_fix.py.gz.b64 `
    .github/scripts/apply_s8_fix.decoded.py `
    .github/scripts/decode_s8_patcher.py `
    .github/scripts/materialize_s8_function_patch.py `
    .github/scripts/apply_s8_postbuild_patch.py `
    .github/workflows/decode-s8-patcher.yml `
    .github/workflows/recover-s8-plaintext.yml `
    .github/workflows/apply-s8-fix.yml `
    .github/workflows/materialize-s8-source.yml `
    .github/workflows/materialize-s8-source-v2.yml `
    .github/workflows/materialize-s8-source-v3.yml `
    .github/workflows/materialize-s8-source-v4.yml `
    .github/workflows/s8-verify-and-package.yml `
    .github/workflows/s8-verify-and-package-v2.yml `
    tests/verify_s8_bound_path_matrix.py

Write-Host '== Commit final verified state locally =='
git add -u
Add-IfPresent @(
    '.github/patches/s8-deterministic-output-hashes.patch',
    '.github/patches/s8-state-evidence.patch',
    '.github/audit/s8-bound-reader-consumer-matrix.json'
)
[void](Commit-IfNeeded 'build: finalize verified S8 cross-step candidate [skip ci]')

$CriticalPaths = @(
    'build.ps1',
    'STATE.md',
    'native/San9BridgeP1EasyPingM2b/include/s5_current_context.h',
    'native/San9BridgeP1EasyPingM2b/src/s5_current_context.c',
    'native/San9BridgeP1EasyPingM2b/src/controller.c',
    'native/San9BridgeP1EasyPingM2b/src/bridge_dll.c',
    'native/San9BridgeP1EasyPingM2b/src/offline_selftest.c',
    'native/San9BridgeP1EasyPingM2b/build.ps1',
    'src/San9AutoDomestic.UI/NativeControllerClient.cs',
    'tests/San9AutoDomestic.UI.StateTests/UiStateTests.cs',
    'tests/verify_s8_bound_path_matrix_v2.py'
)
$BuiltDigests = Get-FileDigestMap $CriticalPaths

Write-Host '== Push final state without allowing source drift =='
$Pushed = $false
for ($attempt = 1; $attempt -le 4 -and -not $Pushed; $attempt++) {
    git fetch origin $Branch
    Assert-LastCommand 'Failed to fetch repair branch before push'
    $Behind = [int](git rev-list --count "HEAD..origin/$Branch")
    if ($Behind -gt 0) {
        git rebase "origin/$Branch"
        Assert-LastCommand 'Failed to rebase concurrent repair-branch commits'
        $AfterRebase = Get-FileDigestMap $CriticalPaths
        Assert-DigestMapsEqual $BuiltDigests $AfterRebase 'Concurrent rebase'
    }
    git push origin "HEAD:$Branch"
    if ($LASTEXITCODE -eq 0) {
        $Pushed = $true
    }
    else {
        Start-Sleep -Seconds (2 * $attempt)
    }
}
if (-not $Pushed) { throw 'Failed to push final verified repair state after retries' }
$FinalCommit = (git rev-parse HEAD).Trim()

Write-Host '== Assemble candidate from the exact final commit =='
$CandidateRoot = Join-Path $env:RUNNER_TEMP 'San9-S8-cross-step-candidate'
$NativeCandidate = Join-Path $CandidateRoot 'native'
$ProductCandidate = Join-Path $CandidateRoot 'product'
$EvidenceCandidate = Join-Path $CandidateRoot 'evidence'
Remove-Item -Recurse -Force $CandidateRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $NativeCandidate, $ProductCandidate, $EvidenceCandidate | Out-Null
Copy-Item -Path (Join-Path $ArtifactRoot '*') -Destination $NativeCandidate -Recurse -Force

Get-ChildItem -Path . -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -match '[\\/]bin[\\/]Release(?:[\\/]|$)' -and
        $_.FullName -notmatch '[\\/]obj[\\/]'
    } | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($RepoRoot, $_.FullName)
        $target = Join-Path $ProductCandidate $relative
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item -Path (Join-Path $_.FullName '*') -Destination $target -Recurse -Force
    }
foreach ($name in @('artifacts', 'dist', 'publish', 'out', 'release')) {
    if (Test-Path -LiteralPath $name -PathType Container) {
        Copy-Item -Path $name -Destination (Join-Path $ProductCandidate $name) -Recurse -Force
    }
}
foreach ($path in @(
    'STATE.md',
    '.github\audit\s8-bound-reader-consumer-matrix.json',
    '.github\scripts\apply_s8_fix.recovery.txt',
    '.github\patches\s8-cross-step-bound-city.function-context.patch',
    '.github\patches\s8-deterministic-output-hashes.patch',
    '.github\patches\s8-state-evidence.patch'
)) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Copy-Item -LiteralPath $path -Destination $EvidenceCandidate -Force
    }
}
Copy-Item -LiteralPath $NativeFinalLog -Destination $EvidenceCandidate -Force
Copy-Item -LiteralPath $RootBuildLog -Destination $EvidenceCandidate -Force

$ManifestFiles = @()
Get-ChildItem -Path $CandidateRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($CandidateRoot, $_.FullName).Replace('\', '/')
    $ManifestFiles += [ordered]@{
        path = $relative
        size = $_.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
    }
}
[ordered]@{
    branch = $Branch
    commit = $FinalCommit
    baseline = '1549d62'
    native_offline = 'passed'
    product_build = 'passed'
    path_matrix = 'bound reader A/B x Controller/Bridge passed'
    next_manual_gate = '巡察→商业'
    files = $ManifestFiles
} | ConvertTo-Json -Depth 7 |
    Set-Content -LiteralPath (Join-Path $CandidateRoot 'candidate-manifest.json') -Encoding utf8NoBOM

Remove-Item -Force $CandidateZip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $CandidateRoot '*') -DestinationPath $CandidateZip -CompressionLevel Optimal
$CandidateSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $CandidateZip).Hash.ToLowerInvariant()
Write-Host "candidate=$CandidateZip sha256=$CandidateSha commit=$FinalCommit"
Write-OutputValue 'candidate_zip' $CandidateZip
Write-OutputValue 'candidate_sha' $CandidateSha
Write-OutputValue 'final_commit' $FinalCommit
