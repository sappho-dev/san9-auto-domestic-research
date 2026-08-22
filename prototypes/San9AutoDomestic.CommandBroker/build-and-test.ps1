[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$prototypeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$msbuildPath = Join-Path $frameworkDirectory 'MSBuild.exe'
$libraryProject = Join-Path $prototypeRoot 'src\San9AutoDomestic.CommandBroker.csproj'
$testProject = Join-Path $prototypeRoot 'tests\San9AutoDomestic.CommandBroker.SelfTest.csproj'
$libraryPath = Join-Path $prototypeRoot 'src\bin\Release\San9AutoDomestic.CommandBroker.dll'
$testExecutable = Join-Path $prototypeRoot 'tests\bin\Release\San9AutoDomestic.CommandBroker.SelfTest.exe'

if (-not (Test-Path -LiteralPath $msbuildPath -PathType Leaf)) {
    throw "System .NET Framework MSBuild was not found at '$msbuildPath'."
}

$msbuildArguments = @(
    '/nologo',
    '/verbosity:minimal',
    '/property:Configuration=Release',
    '/property:Platform=x86'
)

$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$referenceAssemblyDirectory = Join-Path $programFilesX86 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$localRuntimeFallback = -not (Test-Path -LiteralPath $referenceAssemblyDirectory -PathType Container)
if ($localRuntimeFallback) {
    $msbuildArguments += "/property:FrameworkPathOverride=$frameworkDirectory"
}

& $msbuildPath $libraryProject @msbuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "CommandBroker library build failed with exit code $LASTEXITCODE."
}

& $msbuildPath $testProject @msbuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "CommandBroker self-test build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $libraryPath -PathType Leaf)) {
    throw "CommandBroker library was not produced at '$libraryPath'."
}

if (-not (Test-Path -LiteralPath $testExecutable -PathType Leaf)) {
    throw "CommandBroker self-test executable was not produced at '$testExecutable'."
}

& $testExecutable
if ($LASTEXITCODE -ne 0) {
    throw "CommandBroker offline self-test failed with exit code $LASTEXITCODE."
}

$libraryHash = (Get-FileHash -LiteralPath $libraryPath -Algorithm SHA256).Hash
$selfTestHash = (Get-FileHash -LiteralPath $testExecutable -Algorithm SHA256).Hash
Write-Host "CommandBroker DLL SHA256=$libraryHash"
Write-Host "CommandBroker SelfTest SHA256=$selfTestHash"

if ($localRuntimeFallback) {
    Write-Host 'BUILD MODE: local runtime/GAC fallback; verification only, never a publishing build.'
}

Write-Host 'live_authorized=false; process_accessed=false; pinvoke=false; native_callbacks=false; transport=in_memory_only'
Write-Host 'COMMAND BROKER RESULT: OFFLINE-ONLY GO; LIVE/PROCESS/BRIDGE/NATIVE EXECUTION NO-GO.'
