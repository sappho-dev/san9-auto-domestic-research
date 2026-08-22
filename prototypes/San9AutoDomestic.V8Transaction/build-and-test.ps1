[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$prototypeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$msbuildPath = Join-Path $frameworkDirectory 'MSBuild.exe'
$libraryProject = Join-Path $prototypeRoot 'src\San9AutoDomestic.V8Transaction.csproj'
$testProject = Join-Path $prototypeRoot 'tests\San9AutoDomestic.V8Transaction.SelfTest.csproj'
$testExecutable = Join-Path $prototypeRoot 'tests\bin\Release\San9AutoDomestic.V8Transaction.SelfTest.exe'

if (-not (Test-Path -LiteralPath $msbuildPath -PathType Leaf)) {
    throw "System .NET Framework MSBuild was not found at '$msbuildPath'."
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
    throw "V8 library build failed with exit code $LASTEXITCODE."
}

& $msbuildPath $testProject @msbuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "V8 self-test build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $testExecutable -PathType Leaf)) {
    throw "V8 self-test executable was not produced at '$testExecutable'."
}

& $testExecutable
if ($LASTEXITCODE -ne 0) {
    throw "V8 offline self-test failed with exit code $LASTEXITCODE."
}

if ($localRuntimeFallback) {
    Write-Host 'BUILD MODE: local runtime/GAC fallback; verification only, never a publishing build.'
}

Write-Host 'V8 RESULT: OFFLINE-ONLY GO; LIVE/NATIVE BUSINESS EXECUTION NO-GO.'

