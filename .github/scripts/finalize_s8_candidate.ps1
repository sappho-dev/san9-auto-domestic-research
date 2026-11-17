[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Build', 'Commit')]
    [string]$Phase
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Root

function Assert-LastExitCode {
    param([Parameter(Mandatory = $true)][string]$Operation)
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation exited with code $LASTEXITCODE"
    }
}

function Get-WorktreeDiffHash {
    $hash = git diff --binary -- . `
        ':!.github/workflows/finalize-s8-cross-step-candidate.yml' `
        ':!.github/workflows/finalize-s8-cross-step-candidate-v2.yml' `
        ':!.github/workflows/finalize-s8-cross-step-candidate-v3.yml' |
        git hash-object --stdin
    Assert-LastExitCode 'Hashing the S8 worktree diff'
    return ($hash | Select-Object -First 1).Trim()
}

function Invoke-PythonChecked {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Operation
    )
    & python @Arguments
    Assert-LastExitCode $Operation
}

function Invoke-BuildPhase {
    $prepareLog = Join-Path $env:RUNNER_TEMP 'prepare-s8.log'
    & python '.github/scripts/prepare_s8_function_patch_checked.py' *>&1 |
        Tee-Object -FilePath $prepareLog
    Assert-LastExitCode 'Preparing the audited S8 function-context patch'

    Invoke-PythonChecked -Operation 'Compiling S8 patch scripts' -Arguments @(
        '-m', 'py_compile',
        '.github/scripts/apply_s8_fix.py',
        '.github/scripts/apply_s8_fix_driver.py',
        '.github/scripts/apply_s8_fix_legacy_1549d62.py',
        '.github/scripts/prepare_s8_function_patch.py',
        '.github/scripts/prepare_s8_function_patch_checked.py'
    )
    & git diff --check
    Assert-LastExitCode 'Checking generated S8 patch whitespace'

    Invoke-PythonChecked -Operation 'First LF S8 source application' -Arguments @(
        '.github/scripts/apply_s8_fix.py', 'source'
    )
    $firstLf = Get-WorktreeDiffHash
    Invoke-PythonChecked -Operation 'Second LF S8 source application' -Arguments @(
        '.github/scripts/apply_s8_fix.py', 'source'
    )
    $secondLf = Get-WorktreeDiffHash
    if ($firstLf -ne $secondLf) {
        throw "LF S8 source application is not idempotent: first=$firstLf second=$secondLf"
    }

    $crlfRoot = Join-Path $env:RUNNER_TEMP 's8-crlf-replay'
    & git worktree remove --force $crlfRoot 2>$null
    if (Test-Path -LiteralPath $crlfRoot) {
        Remove-Item -LiteralPath $crlfRoot -Recurse -Force
    }
    & git worktree add --detach $crlfRoot HEAD
    Assert-LastExitCode 'Creating the CRLF replay worktree'
    try {
        foreach ($relative in @(
            '.github/scripts/apply_s8_fix.py',
            '.github/scripts/apply_s8_fix_legacy_1549d62.py',
            '.github/patches/s8-cross-step-bound-city.patch',
            '.github/patches/s8-cross-step-bound-city.manifest.json'
        )) {
            $source = Join-Path $Root $relative
            $target = Join-Path $crlfRoot $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            Copy-Item -LiteralPath $source -Destination $target -Force
        }

        $env:S8_CRLF_ROOT = $crlfRoot
        $crlfFixtureScript = @'
import json
import os
from pathlib import Path

root = Path(os.environ['S8_CRLF_ROOT'])
manifest = json.loads(
    (root / '.github/patches/s8-cross-step-bound-city.manifest.json').read_text(encoding='utf-8')
)
for item in manifest['files']:
    path = root / item['path']
    data = path.read_bytes().replace(b'\r\n', b'\n').replace(b'\r', b'\n')
    path.write_bytes(data.replace(b'\n', b'\r\n'))
'@
        $crlfFixtureScript | python -
        Assert-LastExitCode 'Constructing the synthetic Windows CRLF preimage'

        Push-Location $crlfRoot
        try {
            Invoke-PythonChecked -Operation 'First CRLF S8 source application' -Arguments @(
                '.github/scripts/apply_s8_fix.py', 'source'
            )
            $firstCrlf = (git diff --binary | git hash-object --stdin | Select-Object -First 1).Trim()
            Assert-LastExitCode 'Hashing first CRLF replay'
            Invoke-PythonChecked -Operation 'Second CRLF S8 source application' -Arguments @(
                '.github/scripts/apply_s8_fix.py', 'source'
            )
            $secondCrlf = (git diff --binary | git hash-object --stdin | Select-Object -First 1).Trim()
            Assert-LastExitCode 'Hashing second CRLF replay'
            if ($firstCrlf -ne $secondCrlf) {
                throw "CRLF S8 source application is not idempotent: first=$firstCrlf second=$secondCrlf"
            }
            & git diff --check
            Assert-LastExitCode 'Checking CRLF replay whitespace'
        }
        finally {
            Pop-Location
        }
    }
    finally {
        & git worktree remove --force $crlfRoot
        & git worktree prune
    }

    $nativeOut = Join-Path $env:RUNNER_TEMP 'san9-native'
    $nativeBootstrapLog = Join-Path $env:RUNNER_TEMP 'native-bootstrap.log'
    $bootstrapError = $null
    try {
        & '.\native\San9BridgeP1EasyPingM2b\build.ps1' -ArtifactRoot $nativeOut *>&1 |
            Tee-Object -FilePath $nativeBootstrapLog
    }
    catch {
        $bootstrapError = $_
        $_ | Out-String | Tee-Object -FilePath $nativeBootstrapLog -Append | Out-Null
    }

    $requiredNative = @(
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
    $missingNative = @(
        $requiredNative | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $nativeOut $_) -PathType Leaf)
        }
    )
    $bootstrapText = if (Test-Path -LiteralPath $nativeBootstrapLog -PathType Leaf) {
        [IO.File]::ReadAllText($nativeBootstrapLog)
    }
    else {
        ''
    }
    if ($missingNative.Count -ne 0 -or $bootstrapText -notmatch 'deterministic whole image rejected') {
        if ($null -ne $bootstrapError) {
            throw $bootstrapError
        }
        throw "Native bootstrap failed before expected deterministic hash gate. Missing=$($missingNative -join ', ')"
    }

    Invoke-PythonChecked -Operation 'Updating deterministic native output hashes' -Arguments @(
        '.github/scripts/apply_s8_fix.py',
        'outputs',
        '--artifact-root',
        $nativeOut
    )
    & git diff --check
    Assert-LastExitCode 'Checking deterministic hash update whitespace'

    $nativeFinalLog = Join-Path $env:RUNNER_TEMP 'native-final.log'
    & '.\native\San9BridgeP1EasyPingM2b\build.ps1' -ArtifactRoot $nativeOut *>&1 |
        Tee-Object -FilePath $nativeFinalLog
    Assert-LastExitCode 'Complete deterministic native build and embedded offline audit'

    $explicitOfflineLog = Join-Path $env:RUNNER_TEMP 'offline-explicit.log'
    Push-Location $nativeOut
    try {
        & '.\offline.exe' *>&1 | Tee-Object -FilePath $explicitOfflineLog
        Assert-LastExitCode 'Explicit native offline suite'
    }
    finally {
        Pop-Location
    }

    $rootBuildLog = Join-Path $env:RUNNER_TEMP 'root-build.log'
    & '.\build.ps1' *>&1 | Tee-Object -FilePath $rootBuildLog
    Assert-LastExitCode 'Complete product build and managed regression suite'

    Invoke-PythonChecked -Operation 'Updating audited STATE truth' -Arguments @(
        '.github/scripts/apply_s8_fix.py',
        'state',
        '--artifact-root',
        $nativeOut,
        '--native-log',
        $nativeFinalLog,
        '--root-log',
        $rootBuildLog
    )
    & git diff --check
    Assert-LastExitCode 'Checking final state whitespace'

    $immutableAuditScript = @'
import hashlib
import json
from pathlib import Path

root = Path.cwd()
manifest = json.loads(
    (root / '.github/patches/s8-cross-step-bound-city.manifest.json').read_text(encoding='utf-8')
)
mutable_after_source = {
    'build.ps1',
    'native/San9BridgeP1EasyPingM2b/build.ps1',
    'STATE.md',
}
failures = []
audited = []
for item in manifest['files']:
    if item['path'] in mutable_after_source:
        continue
    data = (root / item['path']).read_bytes().replace(b'\r\n', b'\n').replace(b'\r', b'\n')
    actual = hashlib.sha256(data).hexdigest()
    if actual != item['after_sha256_canonical_lf']:
        failures.append(f"{item['path']}: {actual}")
    audited.append(item['path'])
if failures:
    raise SystemExit('Immutable S8 source postimage mismatch:\n' + '\n'.join(failures))
print(f"Verified {len(audited)} immutable canonical S8 postimages")
'@
    $immutableAuditScript | python -
    Assert-LastExitCode 'Verifying immutable S8 source postimages'

    $candidate = Join-Path $env:RUNNER_TEMP 'San9-S8-cross-step-candidate'
    $candidateZip = Join-Path $env:RUNNER_TEMP 'San9-S8-cross-step-candidate.zip'
    Remove-Item -LiteralPath $candidate -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $candidateZip -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $candidate | Out-Null

    Copy-Item -LiteralPath $nativeOut -Destination (Join-Path $candidate 'native') -Recurse -Force
    foreach ($relative in @('artifacts', 'dist', 'publish', 'out')) {
        $source = Join-Path $Root $relative
        if (Test-Path -LiteralPath $source -PathType Container) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $candidate $relative) -Recurse -Force
        }
    }

    $releaseRoots = Get-ChildItem -Path (Join-Path $Root 'src') -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'Release' -and $_.Parent.Name -eq 'bin' }
    foreach ($releaseRoot in $releaseRoots) {
        $relative = $releaseRoot.FullName.Substring($Root.Length).TrimStart('\', '/')
        $safe = $relative -replace '[:\\/]', '_'
        Copy-Item -LiteralPath $releaseRoot.FullName -Destination (Join-Path $candidate "managed_$safe") -Recurse -Force
    }

    Copy-Item -LiteralPath '.github\patches\s8-cross-step-bound-city.audit.json' `
        -Destination (Join-Path $candidate 's8-source-audit.json') -Force
    Copy-Item -LiteralPath '.github\patches\s8-cross-step-bound-city.manifest.json' `
        -Destination (Join-Path $candidate 's8-source-manifest.json') -Force

    $hashLines = Get-ChildItem -LiteralPath $candidate -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($candidate.Length + 1)
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    [IO.File]::WriteAllLines(
        (Join-Path $candidate 'SHA256SUMS.txt'),
        $hashLines,
        [Text.UTF8Encoding]::new($false)
    )
    Compress-Archive -Path (Join-Path $candidate '*') -DestinationPath $candidateZip -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $candidateZip -PathType Leaf)) {
        throw 'Candidate zip was not created'
    }
    "CANDIDATE_ZIP=$candidateZip" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append

    $ready = @"
# S8 cross-step candidate ready

- Historical plaintext baseline: `1549d62`
- Workflow run: `$env:GITHUB_RUN_ID`
- Candidate artifact: `San9-S8-cross-step-candidate-$env:GITHUB_RUN_ID`
- Source application: plaintext `git diff --function-context` patch with canonical-LF pre/post hashes
- Idempotence: LF and synthetic Windows CRLF worktrees each passed twice
- Native coverage: bound-reader A/B core, controller path, Bridge path, deterministic native build, explicit offline suite
- Product coverage: complete root build and managed regression suite
- Main branch: unchanged
- Remaining gate: one real-machine `巡察→商业` transition
"@
    [IO.File]::WriteAllText(
        (Join-Path $Root 'S8_CANDIDATE_READY.md'),
        $ready,
        [Text.UTF8Encoding]::new($false)
    )
}

function Invoke-CommitPhase {
    & git rm -f --ignore-unmatch -- `
        '.github/scripts/apply_s8_fix.py.gz.b64' `
        '.github/scripts/apply_s8_fix.decoded.py' `
        '.github/scripts/decode_s8_patcher.py' `
        '.github/workflows/apply-s8-fix.yml' `
        '.github/workflows/decode-s8-patcher.yml' `
        '.github/workflows/recover-s8-plaintext-from-1549d62.yml' `
        '.github/workflows/finalize-s8-cross-step-candidate.yml' `
        '.github/workflows/finalize-s8-cross-step-candidate-v2.yml' `
        '.github/workflows/finalize-s8-cross-step-candidate-v3.yml' `
        'S8_PIPELINE_FAILURE.md'

    if (Test-Path -LiteralPath 'S8_PIPELINE_FAILURE.md') {
        Remove-Item -LiteralPath 'S8_PIPELINE_FAILURE.md' -Force
    }

    & git add -A
    Assert-LastExitCode 'Staging the verified S8 repair'
    & git diff --cached --check
    Assert-LastExitCode 'Checking the staged S8 repair'
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        throw 'Verified S8 repair produced no staged changes'
    }

    & git config user.name 'github-actions[bot]'
    & git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
    & git commit -m 'Fix S8 cross-step frozen city binding [skip ci]'
    Assert-LastExitCode 'Committing the verified S8 repair'
    & git push origin "HEAD:$env:REPAIR_BRANCH"
    Assert-LastExitCode 'Pushing the verified S8 repair branch'
}

switch ($Phase) {
    'Build' { Invoke-BuildPhase }
    'Commit' { Invoke-CommitPhase }
    default { throw "Unsupported phase: $Phase" }
}
