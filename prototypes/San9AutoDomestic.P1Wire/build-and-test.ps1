[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ArtifactRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repoRoot 'tools\artifacts\San9BridgeP1Wire'
}
$ArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
$nativeArtifactRoot = Join-Path $ArtifactRoot 'native'
$goldenRoot = Join-Path $ArtifactRoot 'golden'
$testProject = Join-Path $PSScriptRoot 'tests\San9AutoDomestic.P1Wire.SelfTest.csproj'
$testExecutable = Join-Path $PSScriptRoot 'tests\bin\Release\San9AutoDomestic.P1Wire.SelfTest.exe'
$nativeBuild = Join-Path $repoRoot 'native\San9BridgeP1Wire\build.ps1'
$nativeExecutable = Join-Path $nativeArtifactRoot 'San9BridgeP1WireSelfTest.exe'
$expectedGoldenSha256 = 'ACADF1F1C5CF70672484632E32865871058E3193B84FFC5C233990985FBE80D5'
$expectedResponseGoldenSha256 = '2DCEF30E7C5B5219F255D97C7A260019748551D2279C868E62539CE204A0E87E'
$oracle = Join-Path $PSScriptRoot 'oracle\p1_wire_oracle.py'
$expectedOracleSha256 = '5D96F4EDDB578A510690AFAA3096CC150EBBD93431B3871DA306418E88CC0461'
$msbuild = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'
$expectedMsBuildFileVersion = '4.8.9221.0 built by: NET481REL1LAST_25H2'
$expectedMsBuildSha256 = '044E493F385DFCB5D5D65B0D36E07F7C76AADA8D2B9FF2DE0309CD277BE9FB6B'
$msbuildToolFiles = [ordered]@{
    'csc.exe' = '012E8CD8ADFF0C439A90FFD22C0E33EFEB6FBC8CF660D52B4858D310649382FA'
    'Microsoft.CSharp.targets' = '8F149E27BBE3E34ECC69FF3C5A9B79C619830CBA13FE06BCEEADDE51F79529BC'
    'Microsoft.Common.targets' = '23CAED7188D274AE69EEF62FA943199FBA30214F7944975D01B6D0CEF8AA1E1F'
}
$python = (Get-Command python.exe -ErrorAction Stop).Source
$expectedPythonVersion = '3.13.0'
$expectedPythonSha256 = '62EBC90A2884BB63A0CD67E789CAFDD51E771EEE043587E2354327B4CCC9BB05'
$expectedPythonRuntimeSha256 = '0C0A66505093B6A4BB3475F716BD3D9552095776F6A124709C13B3F9552C7D99'
$referencePackRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$referencePackFiles = [ordered]@{
    'mscorlib.dll' = '6B35530467B914B0B195146CA1D1485DDD219AFAAD6461103F16F54E952F75A9'
    'System.dll' = '2FE343569F794F2CA92EE14A41875571A9F21BF92637B8F8EE86306534209CCA'
    'System.Core.dll' = '94A1FC97EB0D36F13ACD209BB7207DA504C8BE10CAF7E5096B2C46E671DC6FE2'
    'System.Security.dll' = 'C867EE14FEF732C5803C67A5A00797EF62080B29CE71F6FB14E1AF325C8D2A3D'
    'RedistList\FrameworkList.xml' = 'E7F1F8A36F6DD41334A22A156F2822DCE0E6B66186A35C643C8DFD784F7C018A'
}

function Assert-ExactFileHash {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256,
        [Parameter(Mandatory = $true)][string]$Label
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label not found: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($actual, $ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label identity rejected: path=$Path sha256=$actual expected=$ExpectedSha256"
    }
}

if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
    throw "required .NET Framework MSBuild not found: $msbuild"
}
Assert-ExactFileHash -Path $msbuild -ExpectedSha256 $expectedMsBuildSha256 -Label 'MSBuild'
$msbuildFileVersion = (Get-Item -LiteralPath $msbuild).VersionInfo.FileVersion
if (-not [string]::Equals(
        $msbuildFileVersion,
        $expectedMsBuildFileVersion,
        [System.StringComparison]::Ordinal)) {
    throw "MSBuild version rejected: actual=$msbuildFileVersion expected=$expectedMsBuildFileVersion"
}
foreach ($relativePath in $msbuildToolFiles.Keys) {
    Assert-ExactFileHash `
        -Path (Join-Path (Split-Path -Parent $msbuild) $relativePath) `
        -ExpectedSha256 $msbuildToolFiles[$relativePath] `
        -Label "MSBuild tool $relativePath"
}

$pythonVersion = (& $python -I -S -c 'import platform; print(platform.python_version())' | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not [string]::Equals(
        $pythonVersion,
        $expectedPythonVersion,
        [System.StringComparison]::Ordinal)) {
    throw "Python version rejected: actual=$pythonVersion expected=$expectedPythonVersion"
}
Assert-ExactFileHash -Path $python -ExpectedSha256 $expectedPythonSha256 -Label 'Python executable'
$pythonRuntime = Join-Path (Split-Path -Parent $python) 'python313.dll'
Assert-ExactFileHash -Path $pythonRuntime -ExpectedSha256 $expectedPythonRuntimeSha256 -Label 'Python runtime DLL'

if (-not (Test-Path -LiteralPath $referencePackRoot -PathType Container)) {
    throw "required net48 reference pack not found: $referencePackRoot"
}
foreach ($relativePath in $referencePackFiles.Keys) {
    Assert-ExactFileHash `
        -Path (Join-Path $referencePackRoot $relativePath) `
        -ExpectedSha256 $referencePackFiles[$relativePath] `
        -Label "net48 reference pack $relativePath"
}
if (-not (Test-Path -LiteralPath $oracle -PathType Leaf)) {
    throw "independent Python oracle not found: $oracle"
}
Assert-ExactFileHash -Path $oracle -ExpectedSha256 $expectedOracleSha256 -Label 'independent Python oracle'

Write-Host 'Running independent Python stdlib request/response oracle...'
& $python -I -B $oracle '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "independent Python oracle failed with exit code $LASTEXITCODE"
}

$forbiddenManagedPattern = @(
    '\bDllImport\b',
    '\bLibraryImport\b',
    '\bstatic\s+extern\b',
    '\bSystem\.Diagnostics\.Process\b',
    '\bSystem\.IO\.MemoryMappedFiles\b',
    '\bSystem\.IO\.Pipes\b',
    '\bSystem\.Net\b',
    '\bSendKeys\b',
    '\bCursor\.Position\b'
) -join '|'
$managedFiles = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'), (Join-Path $PSScriptRoot 'tests') -Recurse -File -Filter '*.cs'
$forbiddenManagedHits = $managedFiles | Select-String -Pattern $forbiddenManagedPattern -CaseSensitive
if ($forbiddenManagedHits) {
    throw "managed P1 wire isolation gate failed: $($forbiddenManagedHits | Out-String)"
}

Write-Host 'Building net48/x86/C#5 P1 wire projects with warnings as errors...'
$referencePackArgument = $referencePackRoot.TrimEnd('\').Replace('\', '/') + '/'
& $msbuild $testProject '/t:Rebuild' '/p:Configuration=Release' '/p:Platform=x86' `
    '/p:TreatWarningsAsErrors=true' "/p:FrameworkPathOverride=$referencePackArgument" `
    '/nologo' '/verbosity:minimal'
if ($LASTEXITCODE -ne 0) {
    throw "managed P1 wire build failed with exit code $LASTEXITCODE"
}

Write-Host 'Running managed offline contract/mutation/replay tests...'
& $testExecutable '--self-test'
if ($LASTEXITCODE -ne 0) {
    throw "managed P1 wire self-test failed with exit code $LASTEXITCODE"
}

& $nativeBuild -ArtifactRoot $nativeArtifactRoot
if ($LASTEXITCODE -ne 0) {
    throw "native P1 wire build pipeline failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $goldenRoot | Out-Null
$managedGolden = Join-Path $goldenRoot 'csharp-request.bin'
$nativeGolden = Join-Path $goldenRoot 'native-request.bin'
$pythonGolden = Join-Path $goldenRoot 'python-request.bin'
$managedResponseGolden = Join-Path $goldenRoot 'csharp-response.bin'
$nativeResponseGolden = Join-Path $goldenRoot 'native-response.bin'
$pythonResponseGolden = Join-Path $goldenRoot 'python-response.bin'
& $testExecutable '--write-golden' $managedGolden
if ($LASTEXITCODE -ne 0) {
    throw "managed golden generation failed with exit code $LASTEXITCODE"
}
& $nativeExecutable '--write-golden' $nativeGolden
if ($LASTEXITCODE -ne 0) {
    throw "native golden generation failed with exit code $LASTEXITCODE"
}
& $python -I -B $oracle '--write-request' $pythonGolden
if ($LASTEXITCODE -ne 0) {
    throw "Python request golden generation failed with exit code $LASTEXITCODE"
}
& $testExecutable '--write-response-golden' $managedResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "managed response golden generation failed with exit code $LASTEXITCODE"
}
& $nativeExecutable '--write-response-golden' $nativeResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "native response golden generation failed with exit code $LASTEXITCODE"
}
& $python -I -B $oracle '--write-response' $pythonResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "Python response golden generation failed with exit code $LASTEXITCODE"
}

& $testExecutable '--verify-golden' $nativeGolden
if ($LASTEXITCODE -ne 0) {
    throw "managed decoder rejected native golden with exit code $LASTEXITCODE"
}
& $nativeExecutable '--verify-golden' $managedGolden
if ($LASTEXITCODE -ne 0) {
    throw "native decoder rejected managed golden with exit code $LASTEXITCODE"
}
& $testExecutable '--verify-golden' $pythonGolden
if ($LASTEXITCODE -ne 0) {
    throw "managed decoder rejected Python request golden with exit code $LASTEXITCODE"
}
& $nativeExecutable '--verify-golden' $pythonGolden
if ($LASTEXITCODE -ne 0) {
    throw "native decoder rejected Python request golden with exit code $LASTEXITCODE"
}
& $python -I -B $oracle '--verify-request' $managedGolden
if ($LASTEXITCODE -ne 0) {
    throw "Python oracle rejected managed request golden with exit code $LASTEXITCODE"
}
& $python -I -B $oracle '--verify-request' $nativeGolden
if ($LASTEXITCODE -ne 0) {
    throw "Python oracle rejected native request golden with exit code $LASTEXITCODE"
}

& $testExecutable '--verify-response-golden' $nativeResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "managed decoder rejected native response golden with exit code $LASTEXITCODE"
}
& $testExecutable '--verify-response-golden' $pythonResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "managed decoder rejected Python response golden with exit code $LASTEXITCODE"
}
& $nativeExecutable '--verify-response-golden' $managedResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "native decoder rejected managed response golden with exit code $LASTEXITCODE"
}
& $nativeExecutable '--verify-response-golden' $pythonResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "native decoder rejected Python response golden with exit code $LASTEXITCODE"
}
& $python -I -B $oracle '--verify-response' $managedResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "Python oracle rejected managed response golden with exit code $LASTEXITCODE"
}
& $python -I -B $oracle '--verify-response' $nativeResponseGolden
if ($LASTEXITCODE -ne 0) {
    throw "Python oracle rejected native response golden with exit code $LASTEXITCODE"
}

$managedBytes = [System.IO.File]::ReadAllBytes($managedGolden)
$nativeBytes = [System.IO.File]::ReadAllBytes($nativeGolden)
$pythonBytes = [System.IO.File]::ReadAllBytes($pythonGolden)
$managedResponseBytes = [System.IO.File]::ReadAllBytes($managedResponseGolden)
$nativeResponseBytes = [System.IO.File]::ReadAllBytes($nativeResponseGolden)
$pythonResponseBytes = [System.IO.File]::ReadAllBytes($pythonResponseGolden)
if ($managedBytes.Length -ne 512 -or $nativeBytes.Length -ne 512 -or $pythonBytes.Length -ne 512 -or
    $managedResponseBytes.Length -ne 512 -or $nativeResponseBytes.Length -ne 512 -or
    $pythonResponseBytes.Length -ne 512) {
    throw 'one or more request/response golden vectors is not exactly 512 bytes'
}
if (-not [System.Linq.Enumerable]::SequenceEqual([byte[]]$managedBytes, [byte[]]$nativeBytes) -or
    -not [System.Linq.Enumerable]::SequenceEqual([byte[]]$managedBytes, [byte[]]$pythonBytes)) {
    throw 'managed, native, and independent Python request goldens are not byte-exact'
}
if (-not [System.Linq.Enumerable]::SequenceEqual(
        [byte[]]$managedResponseBytes, [byte[]]$nativeResponseBytes) -or
    -not [System.Linq.Enumerable]::SequenceEqual(
        [byte[]]$managedResponseBytes, [byte[]]$pythonResponseBytes)) {
    throw 'managed, native, and independent Python response goldens are not byte-exact'
}
$managedHash = (Get-FileHash -LiteralPath $managedGolden -Algorithm SHA256).Hash
$nativeHash = (Get-FileHash -LiteralPath $nativeGolden -Algorithm SHA256).Hash
$pythonHash = (Get-FileHash -LiteralPath $pythonGolden -Algorithm SHA256).Hash
$managedResponseHash = (Get-FileHash -LiteralPath $managedResponseGolden -Algorithm SHA256).Hash
$nativeResponseHash = (Get-FileHash -LiteralPath $nativeResponseGolden -Algorithm SHA256).Hash
$pythonResponseHash = (Get-FileHash -LiteralPath $pythonResponseGolden -Algorithm SHA256).Hash
if (-not [string]::Equals($managedHash, $expectedGoldenSha256, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($nativeHash, $expectedGoldenSha256, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($pythonHash, $expectedGoldenSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw (
        "request golden hash mismatch: managed=$managedHash native=$nativeHash " +
        "python=$pythonHash expected=$expectedGoldenSha256"
    )
}
if (-not [string]::Equals(
        $managedResponseHash, $expectedResponseGoldenSha256, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals(
        $nativeResponseHash, $expectedResponseGoldenSha256, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals(
        $pythonResponseHash, $expectedResponseGoldenSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw (
        "response golden hash mismatch: managed=$managedResponseHash native=$nativeResponseHash " +
        "python=$pythonResponseHash expected=$expectedResponseGoldenSha256"
    )
}

Write-Host (
    "P1WIRE_OFFLINE_PIPELINE PASS frame=512 oracle=independent-python-stdlib " +
    "request_sha256=$managedHash response_sha256=$managedResponseHash byte_exact=true " +
    'live_authorization=false root_integration=false'
)
