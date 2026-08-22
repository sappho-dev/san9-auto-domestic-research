[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$prototypeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$msbuildPath = Join-Path $frameworkDirectory 'MSBuild.exe'
$libraryProject = Join-Path $prototypeRoot 'src\San9AutoDomestic.SingleCommandSlice.csproj'
$testProject = Join-Path $prototypeRoot 'tests\San9AutoDomestic.SingleCommandSlice.SelfTest.csproj'
$libraryBinary = Join-Path $prototypeRoot 'src\bin\Release\San9AutoDomestic.SingleCommandSlice.dll'
$testExecutable = Join-Path $prototypeRoot 'tests\bin\Release\San9AutoDomestic.SingleCommandSlice.SelfTest.exe'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $prototypeRoot)
$coreBinary = Join-Path $repositoryRoot 'src\San9AutoDomestic.Core\bin\Release\San9AutoDomestic.Core.dll'
$v8Root = Join-Path $repositoryRoot 'prototypes\San9AutoDomestic.V8Transaction'
$v8BuildScript = Join-Path $v8Root 'build-and-test.ps1'
$v8Binary = Join-Path $v8Root 'src\bin\Release\San9AutoDomestic.V8Transaction.dll'
$expectedCoreSha256 = '64BFFA87A094834ED4440822B16AB6AE3401C66965374E44FD1BD5635C8FA6CF'
$expectedV8Sha256 = '06690240E44448748F3B2A1A7CAA2F9073253B0A32C3AE328DC1DE8B5B47DEEF'

if (-not (Test-Path -LiteralPath $msbuildPath -PathType Leaf)) {
    throw "System .NET Framework MSBuild was not found at '$msbuildPath'."
}

if (-not (Test-Path -LiteralPath $v8BuildScript -PathType Leaf)) {
    throw "Pinned V8 authoritative build script was not found at '$v8BuildScript'."
}

# The slice maps into V8 public request types, so dependency acceptance is not
# inferred from the slice's own tests.  Run the authoritative V8 offline suite
# first, then pin both production dependency binaries below.
& $v8BuildScript
if ($LASTEXITCODE -ne 0) {
    throw "Authoritative V8 offline suite failed with exit code $LASTEXITCODE."
}

$forbiddenPatterns = @(
    '\[DllImport',
    'DllImportAttribute',
    'LibraryImportAttribute',
    'System\.Diagnostics\.Process',
    'System\.IO\.Pipes',
    'System\.Net\.Sockets',
    'OpenProcess\s*\(',
    'ReadProcessMemory\s*\(',
    'WriteProcessMemory\s*\(',
    'CreateRemoteThread\s*\(',
    'SetWindowsHookEx',
    'San9AutoDomestic\.Adapter',
    'San9AutoDomestic\.Bridge'
)
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $prototypeRoot 'src') -File -Recurse |
    Where-Object { $_.Extension -eq '.cs' }
foreach ($pattern in $forbiddenPatterns) {
    $match = $sourceFiles | Select-String -Pattern $pattern -CaseSensitive
    if ($null -ne $match) {
        throw "Forbidden live/process/IPC pattern '$pattern' found in shadow source: $($match.Path):$($match.LineNumber)."
    }
}

$msbuildArguments = @(
    '/nologo',
    '/verbosity:minimal',
    '/property:Configuration=Release',
    '/property:Platform=AnyCPU'
)

$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$referenceAssemblyDirectory = Join-Path $programFilesX86 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$localRuntimeFallback = -not (Test-Path -LiteralPath $referenceAssemblyDirectory -PathType Container)
if ($localRuntimeFallback) {
    $msbuildArguments += "/property:FrameworkPathOverride=$frameworkDirectory"
}

& $msbuildPath $libraryProject @msbuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "Shadow slice library build failed with exit code $LASTEXITCODE."
}

$actualCoreSha256 = (Get-FileHash -LiteralPath $coreBinary -Algorithm SHA256).Hash
$actualV8Sha256 = (Get-FileHash -LiteralPath $v8Binary -Algorithm SHA256).Hash
if (-not [string]::Equals($actualCoreSha256, $expectedCoreSha256, [StringComparison]::Ordinal)) {
    throw "Core dependency drifted. Expected $expectedCoreSha256, got $actualCoreSha256."
}

if (-not [string]::Equals($actualV8Sha256, $expectedV8Sha256, [StringComparison]::Ordinal)) {
    throw "V8 dependency drifted. Expected $expectedV8Sha256, got $actualV8Sha256."
}

Write-Host "PINNED CORE SHA256: $actualCoreSha256"
Write-Host "PINNED V8 SHA256: $actualV8Sha256"

& $msbuildPath $testProject @msbuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "Shadow slice self-test build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $libraryBinary -PathType Leaf)) {
    throw "Shadow slice library was not produced at '$libraryBinary'."
}

if (-not (Test-Path -LiteralPath $testExecutable -PathType Leaf)) {
    throw "Shadow slice self-test was not produced at '$testExecutable'."
}

& $testExecutable
if ($LASTEXITCODE -ne 0) {
    throw "Shadow slice offline self-test failed with exit code $LASTEXITCODE."
}

$libraryHash = (Get-FileHash -LiteralPath $libraryBinary -Algorithm SHA256).Hash
$testHash = (Get-FileHash -LiteralPath $testExecutable -Algorithm SHA256).Hash
Write-Host "LIBRARY SHA256: $libraryHash"
Write-Host "SELFTEST SHA256: $testHash"

if ($localRuntimeFallback) {
    Write-Host 'BUILD MODE: local runtime/GAC fallback; verification only, never a publishing build.'
}

Write-Host 'SLICE RESULT: OFFLINE SHADOW-ONLY GO; LIVE/IPC/PROCESS/NATIVE CALLBACKS NO-GO.'
