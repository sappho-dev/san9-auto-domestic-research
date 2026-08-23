[CmdletBinding()]
param(
    [switch]$NoTests,
    [switch]$RunDiagnostics,
    [switch]$TransactionSelfTest,
    [switch]$LocalRuntimeFallback
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($LocalRuntimeFallback -and $NoTests) {
    throw '-LocalRuntimeFallback always runs every test and cannot be combined with -NoTests.'
}

if ($TransactionSelfTest -and ($NoTests -or $RunDiagnostics -or $LocalRuntimeFallback)) {
    throw '-TransactionSelfTest must be run by itself.'
}

$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$msbuild = Join-Path $frameworkDirectory 'MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
    throw "System .NET Framework MSBuild not found at '$msbuild'."
}

$artifactsDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'tools\artifacts'))
$publishedDirectoryName = 'bin'
$previousDirectoryName = 'bin.previous'
$journalFileName = '.publish-transaction'
$lockFileName = '.publish.lock'
$markerFileName = '.san9-build-id'

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $fullRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing filesystem operation outside '$fullRoot': '$fullPath'."
    }

    return $fullPath
}

function Get-PublishPaths {
    param([Parameter(Mandatory = $true)][string]$Root)

    $fullRoot = [IO.Path]::GetFullPath($Root)
    return @{
        Root = $fullRoot
        Published = Assert-ChildPath -Root $fullRoot -Path (Join-Path $fullRoot $publishedDirectoryName)
        Previous = Assert-ChildPath -Root $fullRoot -Path (Join-Path $fullRoot $previousDirectoryName)
        Journal = Assert-ChildPath -Root $fullRoot -Path (Join-Path $fullRoot $journalFileName)
        Lock = Assert-ChildPath -Root $fullRoot -Path (Join-Path $fullRoot $lockFileName)
    }
}

function Enter-PublishLock {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$BuildId
    )

    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Root)) | Out-Null
    $paths = Get-PublishPaths -Root $Root
    try {
        $stream = New-Object IO.FileStream(
            $paths.Lock,
            [IO.FileMode]::OpenOrCreate,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
    }
    catch {
        throw "Another build or publish transaction owns '$($paths.Lock)'. $($_.Exception.Message)"
    }

    try {
        $owner = "BuildId=$BuildId`r`nProcessId=$PID`r`nStartedUtc=$([DateTime]::UtcNow.ToString('o'))`r`n"
        $bytes = [Text.Encoding]::UTF8.GetBytes($owner)
        $stream.SetLength(0)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush()
        return $stream
    }
    catch {
        $stream.Dispose()
        throw
    }
}

function Write-BuildMarker {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$BuildId,
        [Parameter(Mandatory = $true)][ValidateSet('Verified', 'Unverified', 'LocalRuntimeVerified')][string]$Status
    )

    if (-not [IO.Directory]::Exists($Directory)) {
        throw "Cannot mark missing directory '$Directory'."
    }

    [IO.File]::WriteAllLines(
        (Join-Path $Directory $markerFileName),
        @('Version=1', "BuildId=$BuildId", "Status=$Status"),
        [Text.Encoding]::UTF8)
}

function Read-KeyValueFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [IO.File]::Exists($Path)) {
        return $null
    }

    $values = @{}
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $separator = $line.IndexOf('=')
        if ($separator -le 0) {
            throw "Invalid transaction metadata line in '$Path'."
        }

        $name = $line.Substring(0, $separator)
        if ($values.ContainsKey($name)) {
            throw "Duplicate transaction metadata field '$name' in '$Path'."
        }

        $values[$name] = $line.Substring($separator + 1)
    }

    return $values
}

function Get-BuildMarker {
    param([Parameter(Mandatory = $true)][string]$Directory)

    if (-not [IO.Directory]::Exists($Directory)) {
        return $null
    }

    return Read-KeyValueFile -Path (Join-Path $Directory $markerFileName)
}

function Test-Marker {
    param(
        [AllowNull()]$Marker,
        [Parameter(Mandatory = $true)][string]$BuildId,
        [Parameter(Mandatory = $true)][string]$Status
    )

    return $null -ne $Marker `
        -and $Marker.ContainsKey('Version') `
        -and $Marker['Version'] -eq '1' `
        -and $Marker.ContainsKey('BuildId') `
        -and $Marker['BuildId'] -eq $BuildId `
        -and $Marker.ContainsKey('Status') `
        -and $Marker['Status'] -eq $Status
}

function Test-OwnedPublishedMarker {
    param([AllowNull()]$Marker)

    return $null -ne $Marker `
        -and $Marker.ContainsKey('Version') `
        -and $Marker['Version'] -eq '1' `
        -and $Marker.ContainsKey('BuildId') `
        -and -not [string]::IsNullOrWhiteSpace($Marker['BuildId']) `
        -and $Marker.ContainsKey('Status') `
        -and $Marker['Status'] -eq 'Verified'
}

function Write-PublishJournal {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$BuildId,
        [Parameter(Mandatory = $true)][ValidateSet('Prepared', 'PreviousMoved', 'Published')][string]$State,
        [Parameter(Mandatory = $true)][bool]$HadPrevious,
        [Parameter(Mandatory = $true)][string]$StagingName
    )

    if ([IO.Path]::GetFileName($StagingName) -ne $StagingName) {
        throw "Journal staging name must not contain a path: '$StagingName'."
    }

    $paths = Get-PublishPaths -Root $Root
    $temporary = Assert-ChildPath -Root $paths.Root -Path ($paths.Journal + '.' + $BuildId + '.tmp')
    $backup = Assert-ChildPath -Root $paths.Root -Path ($paths.Journal + '.backup')
    [IO.File]::WriteAllLines(
        $temporary,
        @(
            'Version=1',
            "BuildId=$BuildId",
            "State=$State",
            "HadPrevious=$($HadPrevious.ToString())",
            "StagingName=$StagingName"
        ),
        [Text.Encoding]::UTF8)

    if ([IO.File]::Exists($paths.Journal)) {
        if ([IO.File]::Exists($backup)) {
            [IO.File]::Delete($backup)
        }

        [IO.File]::Replace($temporary, $paths.Journal, $backup, $true)
        if ([IO.File]::Exists($backup)) {
            [IO.File]::Delete($backup)
        }
    }
    else {
        [IO.File]::Move($temporary, $paths.Journal)
    }
}

function Clear-PublishJournal {
    param([Parameter(Mandatory = $true)][string]$Root)

    $paths = Get-PublishPaths -Root $Root
    foreach ($path in @($paths.Journal, ($paths.Journal + '.backup'))) {
        $checked = Assert-ChildPath -Root $paths.Root -Path $path
        if ([IO.File]::Exists($checked)) {
            [IO.File]::Delete($checked)
        }
    }
}

function Remove-OwnedStaging {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$StagingName,
        [Parameter(Mandatory = $true)][string]$BuildId
    )

    if ([IO.Path]::GetFileName($StagingName) -ne $StagingName) {
        throw "Refusing invalid staging name '$StagingName'."
    }

    $directory = Assert-ChildPath -Root $Root -Path (Join-Path $Root $StagingName)
    if (-not [IO.Directory]::Exists($directory)) {
        return
    }

    $marker = Get-BuildMarker -Directory $directory
    if (-not (Test-Marker -Marker $marker -BuildId $BuildId -Status 'Verified')) {
        throw "Refusing to delete staging directory without its verified ownership marker: '$directory'."
    }

    [IO.Directory]::Delete($directory, $true)
}

function Recover-PublishTransaction {
    param([Parameter(Mandatory = $true)][string]$Root)

    $paths = Get-PublishPaths -Root $Root
    $journal = Read-KeyValueFile -Path $paths.Journal
    $publishedExists = [IO.Directory]::Exists($paths.Published)
    $previousExists = [IO.Directory]::Exists($paths.Previous)

    if (-not $publishedExists -and $previousExists) {
        [IO.Directory]::Move($paths.Previous, $paths.Published)
        if ($null -ne $journal -and $journal.ContainsKey('BuildId') -and $journal.ContainsKey('StagingName')) {
            Remove-OwnedStaging -Root $paths.Root -StagingName $journal['StagingName'] -BuildId $journal['BuildId']
        }

        Clear-PublishJournal -Root $paths.Root
        Write-Host 'Recovered the previous published bin after an interrupted rename.'
        return
    }

    if ($publishedExists -and $previousExists) {
        if ($null -eq $journal `
            -or -not $journal.ContainsKey('BuildId') `
            -or -not $journal.ContainsKey('StagingName')) {
            throw "Both bin and bin.previous exist without an owned transaction journal; preserving both for inspection."
        }

        $publishedMarker = Get-BuildMarker -Directory $paths.Published
        if (-not (Test-Marker -Marker $publishedMarker -BuildId $journal['BuildId'] -Status 'Verified')) {
            throw "Both bin and bin.previous exist, but bin is not owned by the recorded build; preserving both."
        }

        [IO.Directory]::Delete($paths.Previous, $true)
        Remove-OwnedStaging -Root $paths.Root -StagingName $journal['StagingName'] -BuildId $journal['BuildId']
        Clear-PublishJournal -Root $paths.Root
        Write-Host "Completed cleanup for committed build '$($journal['BuildId'])'."
        return
    }

    if ($null -eq $journal) {
        return
    }

    foreach ($required in @('Version', 'BuildId', 'State', 'HadPrevious', 'StagingName')) {
        if (-not $journal.ContainsKey($required)) {
            throw "Publish journal is missing '$required'; preserving all artifacts."
        }
    }

    if ($publishedExists) {
        $publishedMarker = Get-BuildMarker -Directory $paths.Published
        if (Test-Marker -Marker $publishedMarker -BuildId $journal['BuildId'] -Status 'Verified') {
            Remove-OwnedStaging -Root $paths.Root -StagingName $journal['StagingName'] -BuildId $journal['BuildId']
            Clear-PublishJournal -Root $paths.Root
            return
        }

        if ($journal['State'] -eq 'Prepared') {
            Remove-OwnedStaging -Root $paths.Root -StagingName $journal['StagingName'] -BuildId $journal['BuildId']
            Clear-PublishJournal -Root $paths.Root
            Write-Host 'Discarded a prepared transaction; the previous bin was never moved.'
            return
        }

        throw "Publish journal claims '$($journal['State'])', but current bin is foreign; preserving all artifacts."
    }

    if ($journal['HadPrevious'] -eq 'True') {
        throw 'Publish journal says a previous bin existed, but neither bin nor bin.previous is present.'
    }

    Remove-OwnedStaging -Root $paths.Root -StagingName $journal['StagingName'] -BuildId $journal['BuildId']
    Clear-PublishJournal -Root $paths.Root
}

function Publish-VerifiedStaging {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$BuildId
    )

    $paths = Get-PublishPaths -Root $Root
    $staging = Assert-ChildPath -Root $paths.Root -Path $StagingDirectory
    $stagingName = [IO.Path]::GetFileName($staging)
    $marker = Get-BuildMarker -Directory $staging
    if (-not (Test-Marker -Marker $marker -BuildId $BuildId -Status 'Verified')) {
        throw "Only staging with a matching Verified marker can be published: '$staging'."
    }

    if ([IO.Directory]::Exists($paths.Previous)) {
        throw "Refusing publication while '$($paths.Previous)' still exists."
    }

    $hadPrevious = [IO.Directory]::Exists($paths.Published)
    if ($hadPrevious) {
        $previousMarker = Get-BuildMarker -Directory $paths.Published
        if (-not (Test-OwnedPublishedMarker -Marker $previousMarker)) {
            throw "Refusing to replace an existing published directory without a valid owned Verified marker: '$($paths.Published)'."
        }
    }

    Write-PublishJournal -Root $paths.Root -BuildId $BuildId -State 'Prepared' -HadPrevious $hadPrevious -StagingName $stagingName
    if ($hadPrevious) {
        [IO.Directory]::Move($paths.Published, $paths.Previous)
        Write-PublishJournal -Root $paths.Root -BuildId $BuildId -State 'PreviousMoved' -HadPrevious $true -StagingName $stagingName
    }

    try {
        [IO.Directory]::Move($staging, $paths.Published)
    }
    catch {
        if (-not [IO.Directory]::Exists($paths.Published) -and [IO.Directory]::Exists($paths.Previous)) {
            [IO.Directory]::Move($paths.Previous, $paths.Published)
            Clear-PublishJournal -Root $paths.Root
        }
        elseif (-not $hadPrevious -and -not [IO.Directory]::Exists($paths.Published)) {
            Clear-PublishJournal -Root $paths.Root
        }

        throw
    }

    $publishedMarker = Get-BuildMarker -Directory $paths.Published
    if (-not (Test-Marker -Marker $publishedMarker -BuildId $BuildId -Status 'Verified')) {
        $failed = Assert-ChildPath -Root $paths.Root -Path (Join-Path $paths.Root ('.failed-' + $BuildId))
        [IO.Directory]::Move($paths.Published, $failed)
        if ([IO.Directory]::Exists($paths.Previous)) {
            [IO.Directory]::Move($paths.Previous, $paths.Published)
        }

        Clear-PublishJournal -Root $paths.Root
        throw "Published directory failed its ownership marker check; previous bin was restored. Failed tree: '$failed'."
    }

    Write-PublishJournal -Root $paths.Root -BuildId $BuildId -State 'Published' -HadPrevious $hadPrevious -StagingName $stagingName
    try {
        if ([IO.Directory]::Exists($paths.Previous)) {
            [IO.Directory]::Delete($paths.Previous, $true)
        }

        Clear-PublishJournal -Root $paths.Root
    }
    catch {
        Write-Warning "Build '$BuildId' is committed, but previous-bin cleanup was deferred: $($_.Exception.Message)"
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "Transaction self-test failed: $Message"
    }
}

function Invoke-TransactionSelfTest {
    param([Parameter(Mandatory = $true)][string]$ParentRoot)

    [IO.Directory]::CreateDirectory($ParentRoot) | Out-Null
    $testRoot = Assert-ChildPath -Root $ParentRoot -Path (Join-Path $ParentRoot ('.transaction-selftest-' + [Guid]::NewGuid().ToString('N')))
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $passed = 0
    try {
        $lockCase = Join-Path $testRoot 'lock'
        $firstLock = Enter-PublishLock -Root $lockCase -BuildId 'lock-owner'
        try {
            $wasRejected = $false
            try {
                $secondLock = Enter-PublishLock -Root $lockCase -BuildId 'lock-intruder'
                $secondLock.Dispose()
            }
            catch {
                $wasRejected = $true
            }

            Assert-Condition -Condition $wasRejected -Message 'a second FileShare.None owner was accepted'
            $passed++
        }
        finally {
            $firstLock.Dispose()
        }

        $publishCase = Join-Path $testRoot 'publish'
        $publishPaths = Get-PublishPaths -Root $publishCase
        [IO.Directory]::CreateDirectory($publishPaths.Published) | Out-Null
        [IO.File]::WriteAllText((Join-Path $publishPaths.Published 'old.txt'), 'old')
        Write-BuildMarker -Directory $publishPaths.Published -BuildId 'previous-publish' -Status 'Verified'
        $publishBuildId = 'publish-build'
        $publishStage = Join-Path $publishCase '.staging-publish-build'
        [IO.Directory]::CreateDirectory($publishStage) | Out-Null
        [IO.File]::WriteAllText((Join-Path $publishStage 'new.txt'), 'new')
        Write-BuildMarker -Directory $publishStage -BuildId $publishBuildId -Status 'Verified'
        Publish-VerifiedStaging -Root $publishCase -StagingDirectory $publishStage -BuildId $publishBuildId
        Assert-Condition -Condition ([IO.File]::Exists((Join-Path $publishPaths.Published 'new.txt'))) -Message 'verified staging was not published'
        Assert-Condition -Condition (-not [IO.Directory]::Exists($publishPaths.Previous)) -Message 'previous bin survived successful cleanup'
        $passed++

        $oldMoveCase = Join-Path $testRoot 'recover-old-move'
        $oldMovePaths = Get-PublishPaths -Root $oldMoveCase
        [IO.Directory]::CreateDirectory($oldMovePaths.Published) | Out-Null
        [IO.File]::WriteAllText((Join-Path $oldMovePaths.Published 'old.txt'), 'old')
        $oldMoveStage = Join-Path $oldMoveCase '.staging-recover-old'
        [IO.Directory]::CreateDirectory($oldMoveStage) | Out-Null
        Write-BuildMarker -Directory $oldMoveStage -BuildId 'recover-old' -Status 'Verified'
        Write-PublishJournal -Root $oldMoveCase -BuildId 'recover-old' -State 'PreviousMoved' -HadPrevious $true -StagingName ([IO.Path]::GetFileName($oldMoveStage))
        [IO.Directory]::Move($oldMovePaths.Published, $oldMovePaths.Previous)
        Recover-PublishTransaction -Root $oldMoveCase
        Assert-Condition -Condition ([IO.File]::Exists((Join-Path $oldMovePaths.Published 'old.txt'))) -Message 'old bin was not restored after interrupted first rename'
        $passed++

        $newMoveCase = Join-Path $testRoot 'recover-new-move'
        $newMovePaths = Get-PublishPaths -Root $newMoveCase
        [IO.Directory]::CreateDirectory($newMovePaths.Published) | Out-Null
        [IO.File]::WriteAllText((Join-Path $newMovePaths.Published 'old.txt'), 'old')
        $newMoveStage = Join-Path $newMoveCase '.staging-recover-new'
        [IO.Directory]::CreateDirectory($newMoveStage) | Out-Null
        [IO.File]::WriteAllText((Join-Path $newMoveStage 'new.txt'), 'new')
        Write-BuildMarker -Directory $newMoveStage -BuildId 'recover-new' -Status 'Verified'
        Write-PublishJournal -Root $newMoveCase -BuildId 'recover-new' -State 'PreviousMoved' -HadPrevious $true -StagingName ([IO.Path]::GetFileName($newMoveStage))
        [IO.Directory]::Move($newMovePaths.Published, $newMovePaths.Previous)
        [IO.Directory]::Move($newMoveStage, $newMovePaths.Published)
        Recover-PublishTransaction -Root $newMoveCase
        Assert-Condition -Condition ([IO.File]::Exists((Join-Path $newMovePaths.Published 'new.txt'))) -Message 'committed new bin was not recognized during recovery'
        Assert-Condition -Condition (-not [IO.Directory]::Exists($newMovePaths.Previous)) -Message 'old bin was not cleaned after committed recovery'
        $passed++

        $unverifiedCase = Join-Path $testRoot 'unverified'
        $unverifiedPaths = Get-PublishPaths -Root $unverifiedCase
        [IO.Directory]::CreateDirectory($unverifiedPaths.Published) | Out-Null
        [IO.File]::WriteAllText((Join-Path $unverifiedPaths.Published 'old.txt'), 'old')
        $unverifiedStage = Join-Path $unverifiedCase '.staging-unverified'
        [IO.Directory]::CreateDirectory($unverifiedStage) | Out-Null
        Write-BuildMarker -Directory $unverifiedStage -BuildId 'unverified' -Status 'Unverified'
        $unverifiedRejected = $false
        try {
            Publish-VerifiedStaging -Root $unverifiedCase -StagingDirectory $unverifiedStage -BuildId 'unverified'
        }
        catch {
            $unverifiedRejected = $true
        }

        Assert-Condition -Condition $unverifiedRejected -Message 'unverified staging was accepted for publication'
        Assert-Condition -Condition ([IO.File]::Exists((Join-Path $unverifiedPaths.Published 'old.txt'))) -Message 'unverified attempt changed the old bin'
        $passed++

        $unownedCase = Join-Path $testRoot 'unowned-published-bin'
        $unownedPaths = Get-PublishPaths -Root $unownedCase
        [IO.Directory]::CreateDirectory($unownedPaths.Published) | Out-Null
        [IO.File]::WriteAllText((Join-Path $unownedPaths.Published 'foreign.txt'), 'foreign')
        $unownedStage = Join-Path $unownedCase '.staging-unowned-published'
        [IO.Directory]::CreateDirectory($unownedStage) | Out-Null
        Write-BuildMarker -Directory $unownedStage -BuildId 'unowned-attempt' -Status 'Verified'
        $unownedRejected = $false
        try {
            Publish-VerifiedStaging -Root $unownedCase -StagingDirectory $unownedStage -BuildId 'unowned-attempt'
        }
        catch {
            $unownedRejected = $true
        }

        Assert-Condition -Condition $unownedRejected -Message 'an unowned existing published bin was replaced'
        Assert-Condition -Condition ([IO.File]::Exists((Join-Path $unownedPaths.Published 'foreign.txt'))) -Message 'unowned existing published content was changed'
        $passed++

        $foreignCase = Join-Path $testRoot 'foreign-bin'
        $foreignPaths = Get-PublishPaths -Root $foreignCase
        [IO.Directory]::CreateDirectory($foreignPaths.Published) | Out-Null
        [IO.Directory]::CreateDirectory($foreignPaths.Previous) | Out-Null
        [IO.File]::WriteAllText((Join-Path $foreignPaths.Published 'foreign.txt'), 'foreign')
        [IO.File]::WriteAllText((Join-Path $foreignPaths.Previous 'old.txt'), 'old')
        $foreignStage = Join-Path $foreignCase '.staging-foreign'
        [IO.Directory]::CreateDirectory($foreignStage) | Out-Null
        Write-BuildMarker -Directory $foreignStage -BuildId 'foreign-record' -Status 'Verified'
        Write-PublishJournal -Root $foreignCase -BuildId 'foreign-record' -State 'PreviousMoved' -HadPrevious $true -StagingName ([IO.Path]::GetFileName($foreignStage))
        $foreignRejected = $false
        try {
            Recover-PublishTransaction -Root $foreignCase
        }
        catch {
            $foreignRejected = $true
        }

        Assert-Condition -Condition $foreignRejected -Message 'foreign bin caused deletion of the recorded previous bin'
        Assert-Condition -Condition ([IO.Directory]::Exists($foreignPaths.Previous)) -Message 'foreign recovery deleted bin.previous'
        $passed++

        Write-Host "Transaction self-test: $passed passed."
    }
    finally {
        $checkedTestRoot = Assert-ChildPath -Root $ParentRoot -Path $testRoot
        if ([IO.Directory]::Exists($checkedTestRoot)) {
            [IO.Directory]::Delete($checkedTestRoot, $true)
        }
    }
}

if ($TransactionSelfTest) {
    Invoke-TransactionSelfTest -ParentRoot $artifactsDirectory
    return
}

if ($LocalRuntimeFallback) {
    if (-not (Test-Path -LiteralPath (Join-Path $frameworkDirectory 'mscorlib.dll') -PathType Leaf)) {
        throw "The local CLR reference directory is incomplete: '$frameworkDirectory'."
    }
}
else {
    $programFilesX86 = ${env:ProgramFiles(x86)}
    if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
        throw 'ProgramFiles(x86) is unavailable; cannot locate .NET Framework reference assemblies.'
    }

    $referenceAssemblyDirectory = Join-Path $programFilesX86 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    $requiredReferenceFiles = @(
        'mscorlib.dll',
        'System.dll',
        'System.Windows.Forms.dll',
        'RedistList\FrameworkList.xml'
    )
    if ($requiredReferenceFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $referenceAssemblyDirectory $_) -PathType Leaf) }) {
        throw "The .NET Framework 4.8 targeting pack is missing at '$referenceAssemblyDirectory'. Refusing a runtime/GAC fallback release build. Use -LocalRuntimeFallback only for a non-publishing local verification, or -TransactionSelfTest for transaction tests."
    }
}

$diagnosticsProject = Join-Path $PSScriptRoot 'tools\San9AutoDomestic.V0Diagnostics\San9AutoDomestic.V0Diagnostics.csproj'
$v1DiagnosticsProject = Join-Path $PSScriptRoot 'tools\San9AutoDomestic.V1ReadDiagnostics\San9AutoDomestic.V1ReadDiagnostics.csproj'
$v2DiagnosticsProject = Join-Path $PSScriptRoot 'tools\San9AutoDomestic.V2AvailabilityDiagnostics\San9AutoDomestic.V2AvailabilityDiagnostics.csproj'
$selfTestProject = Join-Path $PSScriptRoot 'tools\San9AutoDomestic.V0SelfTest\San9AutoDomestic.V0SelfTest.csproj'
$v1SelfTestProject = Join-Path $PSScriptRoot 'tools\San9AutoDomestic.V1ReadSelfTest\San9AutoDomestic.V1ReadSelfTest.csproj'
$v2SelfTestProject = Join-Path $PSScriptRoot 'tools\San9AutoDomestic.V2AvailabilitySelfTest\San9AutoDomestic.V2AvailabilitySelfTest.csproj'
$v4RootTraceProject = Join-Path $PSScriptRoot 'tools\San9AutoDomestic.V4RootTrace\San9AutoDomestic.V4RootTrace.csproj'
$bridgeSelfTestProject = Join-Path $PSScriptRoot 'tools\San9AutoDomestic.Bridge.SelfTest\San9AutoDomestic.Bridge.SelfTest.csproj'
$coreProject = Join-Path $PSScriptRoot 'src\San9AutoDomestic.Core\San9AutoDomestic.Core.csproj'
$adapterProject = Join-Path $PSScriptRoot 'src\San9AutoDomestic.Adapter.San9Pk101\San9AutoDomestic.Adapter.San9Pk101.csproj'
$bridgeProtocolProject = Join-Path $PSScriptRoot 'src\San9AutoDomestic.Bridge.Protocol\San9AutoDomestic.Bridge.Protocol.csproj'
$coreTestProject = Join-Path $PSScriptRoot 'tests\San9AutoDomestic.Core.Tests\San9AutoDomestic.Core.Tests.csproj'
$uiProject = Join-Path $PSScriptRoot 'src\San9AutoDomestic.UI\San9AutoDomestic.UI.csproj'
$uiStateTestProject = Join-Path $PSScriptRoot 'tests\San9AutoDomestic.UI.StateTests\San9AutoDomestic.UI.StateTests.csproj'
$nativeM2bBuildScript = Join-Path $PSScriptRoot 'native\San9BridgeP1EasyPingM2b\build.ps1'
$nativeControllerLauncherPath = Join-Path $PSScriptRoot 'src\San9AutoDomestic.UI\NativeControllerClient.cs'
$nativeGameFocusHandoffPath = Join-Path $PSScriptRoot 'src\San9AutoDomestic.UI\NativeGameFocusHandoff.cs'
$nativeRuntimeExpectedHashes = [ordered]@{
    'controller.exe' = '4B9F79D4F0B258D4C4196EC4BDDBC99B6769EF26B2D943ED32C2B18FC4506764'
    'bridge_s8_basic_batch.dll' = '0BB305BC6E121ABDDD268E8213F093F20BD547CDB7A184EC8180806C34852A65'
    'bridge_s8_wealthy_batch.dll' = 'FC24FFB5472E955A8EF795DAD42DC447A26323002B533709E2057D4F1930420C'
}
$buildId = [Guid]::NewGuid().ToString('N')
$stagingPrefix = if ($LocalRuntimeFallback) { '.local-staging-' } else { '.staging-' }
$intermediatePrefix = if ($LocalRuntimeFallback) { '.local-intermediate-' } else { '.intermediate-' }
$stagingDirectory = Assert-ChildPath -Root $artifactsDirectory -Path (Join-Path $artifactsDirectory ($stagingPrefix + $buildId))
$intermediateDirectory = Assert-ChildPath -Root $artifactsDirectory -Path (Join-Path $artifactsDirectory ($intermediatePrefix + $buildId))
$nativeArtifactDirectory = Assert-ChildPath -Root $intermediateDirectory -Path (Join-Path $intermediateDirectory 'native-s8')
$unverifiedDirectory = Assert-ChildPath -Root $artifactsDirectory -Path (Join-Path $artifactsDirectory ('.unverified-' + $buildId))
$localVerifiedDirectory = Assert-ChildPath -Root $artifactsDirectory -Path (Join-Path $artifactsDirectory ('.local-verified-' + $buildId))
$publishLock = $null
$published = $false
$retainedUnverified = $false
$retainedLocalVerified = $false

function Invoke-FrameworkBuild {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [string]$Platform = 'x86'
    )

    $projectName = [IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $projectIntermediateDirectory = Assert-ChildPath `
        -Root $intermediateDirectory `
        -Path (Join-Path $intermediateDirectory $projectName)
    [IO.Directory]::CreateDirectory($projectIntermediateDirectory) | Out-Null
    # A quoted native argument whose final character is a backslash is
    # ambiguous to the Windows command-line parser when the path contains a
    # space.  MSBuild accepts forward slashes, so keep the required directory
    # terminator without escaping the closing quote.
    $projectIntermediateMsBuildPath = $projectIntermediateDirectory.TrimEnd('\').Replace('\', '/') + '/'
    $stagingMsBuildPath = $stagingDirectory.TrimEnd('\').Replace('\', '/') + '/'
    $baseIntermediateArgument = "/property:BaseIntermediateOutputPath=$projectIntermediateMsBuildPath"
    $projectIntermediateArgument = "/property:IntermediateOutputPath=$projectIntermediateMsBuildPath"
    $arguments = @(
        $ProjectPath,
        '/nologo',
        '/verbosity:minimal',
        '/maxcpucount:1',
        '/target:Rebuild',
        '/property:Configuration=Release',
        "/property:Platform=$Platform",
        '/property:TreatWarningsAsErrors=true',
        '/property:UseSharedCompilation=false',
        '/property:BuildProjectReferences=false',
        "/property:OutputPath=$stagingMsBuildPath",
        $baseIntermediateArgument,
        $projectIntermediateArgument
    )
    if ($LocalRuntimeFallback) {
        $arguments += "/property:FrameworkPathOverride=$frameworkDirectory"
    }

    & $msbuild $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed for '$ProjectPath' with exit code $LASTEXITCODE."
    }
}

function Assert-ProductionInputRetired {
    param(
        [Parameter(Mandatory = $true)][string[]]$ProductionRoots,
        [Parameter(Mandatory = $true)][string]$UiProjectPath,
        [Parameter(Mandatory = $true)][string]$AllowedControllerLauncherPath,
        [Parameter(Mandatory = $true)][string]$AllowedGameFocusHandoffPath
    )

    $forbiddenReferences = @(
        'San9AutoDomestic.Input.Win32',
        'San9AutoDomestic.SingleCommandRunner',
        'InputWin32DomesticPlanRunner',
        'SendKeys',
        'Cursor.Position',
        'Process.Start',
        'SendInput',
        'mouse_event',
        'keybd_event',
        'SetCursorPos',
        'SetForegroundWindow',
        'ShowWindow',
        'PostMessage',
        'SendMessage',
        'WriteProcessMemory',
        'NtWriteVirtualMemory',
        'VirtualAllocEx',
        'VirtualProtectEx',
        'CreateRemoteThread',
        'QueueUserAPC',
        'SetWindowsHookEx',
        'ProcessAllAccess',
        'ProcessAccessFlags.VmWrite',
        'ProcessAccessFlags.VmOperation'
    )
    $sourcePaths = @($UiProjectPath) + @(
        foreach ($productionRoot in $ProductionRoots) {
            Get-ChildItem -LiteralPath $productionRoot -File -Recurse |
                Where-Object { $_.Extension -eq '.cs' -or $_.Extension -eq '.csproj' } |
                Select-Object -ExpandProperty FullName
        })
    $sourcePaths = @($sourcePaths | Select-Object -Unique)
    $allowedLauncherFullPath = [IO.Path]::GetFullPath($AllowedControllerLauncherPath)
    $allowedFocusFullPath = [IO.Path]::GetFullPath($AllowedGameFocusHandoffPath)
    foreach ($sourcePath in $sourcePaths) {
        $source = [IO.File]::ReadAllText($sourcePath)
        $isAllowedControllerLauncher = [string]::Equals(
            [IO.Path]::GetFullPath($sourcePath),
            $allowedLauncherFullPath,
            [StringComparison]::OrdinalIgnoreCase)
        $isAllowedGameFocusHandoff = [string]::Equals(
            [IO.Path]::GetFullPath($sourcePath),
            $allowedFocusFullPath,
            [StringComparison]::OrdinalIgnoreCase)
        foreach ($forbiddenReference in $forbiddenReferences) {
            if ($isAllowedControllerLauncher -and $forbiddenReference -eq 'Process.Start') {
                continue
            }
            if ($isAllowedGameFocusHandoff `
                -and ($forbiddenReference -eq 'SetForegroundWindow' `
                    -or $forbiddenReference -eq 'ShowWindow')) {
                continue
            }
            if ($source.IndexOf($forbiddenReference, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Production UI input retirement check failed: '$sourcePath' contains '$forbiddenReference'."
            }
        }
    }
}

function Assert-NativeControllerLauncherClosed {
    param([Parameter(Mandatory = $true)][string]$LauncherPath)

    $source = [IO.File]::ReadAllText($LauncherPath)
    $requiredTokens = @(
        'Path.Combine(fullBase, "runtime")',
        'Path.Combine(runtimeDirectory, "controller.exe")',
        '--inspect',
        '--s8-basic-batch --confirm',
        'I_ACCEPT_BASIC_BATCH_COMMERCE_CULTIVATE_PINNED_UNTIL_GAME_RESTART',
        '--s8-wealthy-batch --confirm',
        'I_ACCEPT_WEALTHY_BATCH_PATROL_COMMERCE_CULTIVATE_TRAIN_REPAIR_PINNED_UNTIL_GAME_RESTART',
        '4B9F79D4F0B258D4C4196EC4BDDBC99B6769EF26B2D943ED32C2B18FC4506764',
        '0BB305BC6E121ABDDD268E8213F093F20BD547CDB7A184EC8180806C34852A65',
        'FC24FFB5472E955A8EF795DAD42DC447A26323002B533709E2057D4F1930420C',
        'UseShellExecute = false',
        'CreateNoWindow = true',
        'WindowStyle = ProcessWindowStyle.Hidden',
        'RedirectStandardOutput = true',
        'RedirectStandardError = true',
        'RedirectStandardInput = batch',
        'OPEN_CURRENT_CITY_MENU'
    )
    foreach ($requiredToken in $requiredTokens) {
        if ($source.IndexOf($requiredToken, [StringComparison]::Ordinal) -lt 0) {
            throw "Native controller launcher closure check failed: missing '$requiredToken'."
        }
    }
    $processStartCount = [regex]::Matches(
        $source,
        [regex]::Escape('Process.Start'),
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant).Count
    if ($processStartCount -ne 2) {
        throw "Native controller launcher closure check failed: expected exactly two Process.Start calls, found $processStartCount."
    }
    foreach ($forbiddenToken in @(
            'Environment.GetEnvironmentVariable',
            'CommandLineArgs',
            'San9AutoDomestic.Input.Win32',
            'San9AutoDomestic.SingleCommandRunner')) {
        if ($source.IndexOf($forbiddenToken, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Native controller launcher closure check failed: forbidden '$forbiddenToken'."
        }
    }
}

function Assert-NativeGameFocusHandoffClosed {
    param([Parameter(Mandatory = $true)][string]$HandoffPath)

    $source = [IO.File]::ReadAllText($HandoffPath)
    foreach ($requiredToken in @(
            'San9Pk101Target.ExpectedWindowClass',
            'expectedGeneration.IsSameGenerationAs(before)',
            'expectedGeneration.IsSameGenerationAs(after)',
            'nativeApi.GetForegroundWindow() != window',
            'NativeMethods.GetWindowThreadProcessId',
            'NativeMethods.ShowWindow(window, RestoreWindowCommand)',
            'NativeMethods.SetForegroundWindow(window)',
            'private const int RestoreWindowCommand = 9')) {
        if ($source.IndexOf($requiredToken, [StringComparison]::Ordinal) -lt 0) {
            throw "Native game focus handoff closure check failed: missing '$requiredToken'."
        }
    }

    foreach ($exactCall in @(
            'NativeMethods.ShowWindow(window, RestoreWindowCommand)',
            'NativeMethods.SetForegroundWindow(window)')) {
        $count = [regex]::Matches(
            $source,
            [regex]::Escape($exactCall),
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant).Count
        if ($count -ne 1) {
            throw "Native game focus handoff closure check failed: expected one '$exactCall' call, found $count."
        }
    }
    foreach ($nativeCallPattern in @(
            'NativeMethods\.ShowWindow\s*\(',
            'NativeMethods\.SetForegroundWindow\s*\(')) {
        $count = [regex]::Matches(
            $source,
            $nativeCallPattern,
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant).Count
        if ($count -ne 1) {
            throw "Native game focus handoff closure check failed: native call pattern '$nativeCallPattern' occurred $count times."
        }
    }

    foreach ($forbiddenToken in @(
            'Process.Start',
            'SendInput',
            'SendKeys',
            'mouse_event',
            'keybd_event',
            'SetCursorPos',
            'Cursor.Position',
            'PostMessage',
            'SendMessage',
            'WriteProcessMemory',
            'VirtualAllocEx',
            'CreateRemoteThread')) {
        if ($source.IndexOf($forbiddenToken, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Native game focus handoff closure check failed: forbidden '$forbiddenToken'."
        }
    }
}

function Invoke-NativeS8RuntimeBuild {
    param(
        [Parameter(Mandatory = $true)][string]$BuildScript,
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$ExpectedHashes
    )

    [IO.Directory]::CreateDirectory($ArtifactDirectory) | Out-Null
    & $BuildScript -ArtifactRoot $ArtifactDirectory
    if (-not $?) {
        throw "Native S8 build failed: '$BuildScript'."
    }

    $runtimeStagingDirectory = Join-Path $StagingDirectory 'runtime'
    [IO.Directory]::CreateDirectory($runtimeStagingDirectory) | Out-Null
    foreach ($fileName in $ExpectedHashes.Keys) {
        $sourcePath = Join-Path $ArtifactDirectory $fileName
        $destinationPath = Join-Path $runtimeStagingDirectory $fileName
        if (-not [IO.File]::Exists($sourcePath)) {
            throw "Native S8 output is missing: '$sourcePath'."
        }
        $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash
        if (-not [string]::Equals(
                $sourceHash,
                $ExpectedHashes[$fileName],
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Native S8 source hash mismatch for '$fileName': $sourceHash."
        }
        [IO.File]::Copy($sourcePath, $destinationPath, $false)
        $stagedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationPath).Hash
        if (-not [string]::Equals(
                $stagedHash,
                $ExpectedHashes[$fileName],
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Native S8 staged hash mismatch for '$fileName': $stagedHash."
        }
    }
}

function Assert-ProductionStagingAllowlist {
    param([Parameter(Mandatory = $true)][string]$StagingDirectory)

    $allowedRelativePaths = @(
        'config\default.json',
        'San9AutoDomestic.Adapter.San9Pk101.dll',
        'San9AutoDomestic.Adapter.San9Pk101.pdb',
        'San9AutoDomestic.Bridge.Protocol.dll',
        'San9AutoDomestic.Bridge.Protocol.pdb',
        'San9AutoDomestic.Bridge.SelfTest.exe',
        'San9AutoDomestic.Bridge.SelfTest.pdb',
        'San9AutoDomestic.Core.dll',
        'San9AutoDomestic.Core.pdb',
        'San9AutoDomestic.Core.Tests.exe',
        'San9AutoDomestic.Core.Tests.pdb',
        'San9AutoDomestic.exe',
        'San9AutoDomestic.pdb',
        'San9AutoDomestic.UI.StateTests.exe',
        'San9AutoDomestic.UI.StateTests.pdb',
        'San9AutoDomestic.V0Diagnostics.exe',
        'San9AutoDomestic.V0Diagnostics.pdb',
        'San9AutoDomestic.V0SelfTest.exe',
        'San9AutoDomestic.V0SelfTest.pdb',
        'San9AutoDomestic.V1ReadDiagnostics.exe',
        'San9AutoDomestic.V1ReadDiagnostics.pdb',
        'San9AutoDomestic.V1ReadSelfTest.exe',
        'San9AutoDomestic.V1ReadSelfTest.pdb',
        'San9AutoDomestic.V2AvailabilityDiagnostics.exe',
        'San9AutoDomestic.V2AvailabilitySelfTest.exe',
        'San9AutoDomestic.V4RootTrace.exe',
        'San9AutoDomestic.V4RootTrace.pdb',
        'runtime\controller.exe',
        'runtime\bridge_s8_basic_batch.dll',
        'runtime\bridge_s8_wealthy_batch.dll'
    )
    $fullStagingDirectory = [IO.Path]::GetFullPath($StagingDirectory).TrimEnd('\')
    $actualRelativePaths = @(
        Get-ChildItem -LiteralPath $fullStagingDirectory -File -Recurse |
            ForEach-Object {
                $_.FullName.Substring($fullStagingDirectory.Length + 1)
            })
    $unexpected = @($actualRelativePaths | Where-Object { $allowedRelativePaths -notcontains $_ })
    $missing = @($allowedRelativePaths | Where-Object { $actualRelativePaths -notcontains $_ })
    if ($unexpected.Count -ne 0 -or $missing.Count -ne 0) {
        throw "Production staging allowlist mismatch. Unexpected: $($unexpected -join ', '); missing: $($missing -join ', ')."
    }
}

$easyCompatibilityFrozenFiles = [ordered]@{
    'docs\easy-compatibility-manifest.json' = '72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE'
    'docs\easy-compatibility-p0.md' = '045DD35A68C728FA29F9FC3B6F073C117559BF422770CE244E73602CC3569CAA'
    'tools\re\san9_easy_manifest.py' = '7B8504BE5EE6E8E0A46D96C2A52EBADFE7ECAE1FF80E6A3DA6DD712925862D9B'
    'tools\re\san9_easy_manifest_selftest.py' = '79FEDA80F7239FFFE3F2593FA04D75986227825F8AD611710610FA7B0A97AD70'
}
$easyCompatibilityMinimumPython = [Version]'3.11'
$easyCompatibilityPefileVersion = '2024.8.26'
$easyCompatibilityCapstoneVersion = '5.0.7'

function Assert-EasyCompatibilityFrozenFiles {
    foreach ($relativePath in $easyCompatibilityFrozenFiles.Keys) {
        $fullPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relativePath))
        if (-not [IO.File]::Exists($fullPath)) {
            throw "Easy compatibility offline gate is missing frozen input '$relativePath'."
        }

        $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToUpperInvariant()
        $expectedHash = $easyCompatibilityFrozenFiles[$relativePath]
        if (-not $actualHash.Equals($expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Easy compatibility offline gate rejected '$relativePath': expected SHA-256 $expectedHash, got $actualHash."
        }
    }
}

function Resolve-EasyCompatibilityPython {
    $overridePath = [Environment]::GetEnvironmentVariable('SAN9_EASY_PYTHON')
    if ([string]::IsNullOrWhiteSpace($overridePath)) {
        $pythonCommand = Get-Command python.exe -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $pythonCommand) {
            throw 'Easy compatibility offline gate requires Python >= 3.11. Set SAN9_EASY_PYTHON to an exact python.exe path.'
        }

        $candidatePath = $pythonCommand.Source
    }
    else {
        $candidatePath = $overridePath
    }

    $pythonPath = [IO.Path]::GetFullPath($candidatePath)
    if (-not [IO.File]::Exists($pythonPath)) {
        throw "Easy compatibility Python was not found at '$pythonPath'."
    }

    try {
        $versionOutput = @(& $pythonPath --version 2>&1)
        $versionExitCode = $LASTEXITCODE
    }
    catch {
        throw "Easy compatibility Python version probe failed for '$pythonPath': $($_.Exception.Message)"
    }

    if ($versionExitCode -ne 0) {
        throw "Easy compatibility Python version probe failed with exit code ${versionExitCode}: $($versionOutput -join [Environment]::NewLine)"
    }

    $versionLine = [string]($versionOutput | Select-Object -Last 1)
    if ($versionLine -notmatch '^Python\s+(\d+\.\d+\.\d+)(?:\s|$)') {
        throw "Easy compatibility Python version probe returned an unexpected value: '$versionLine'."
    }

    try {
        $pythonVersion = [Version]$Matches[1]
    }
    catch {
        throw "Easy compatibility Python version probe returned an invalid version: '$($Matches[1])'."
    }

    if ($pythonVersion -lt $easyCompatibilityMinimumPython) {
        throw "Easy compatibility offline gate requires Python >= $easyCompatibilityMinimumPython; found $pythonVersion at '$pythonPath'."
    }

    $dependencyCode = 'import pefile,capstone;print(pefile.__version__,capstone.__version__,sep=chr(124))'
    try {
        $dependencyOutput = @(& $pythonPath -c $dependencyCode 2>&1)
        $dependencyExitCode = $LASTEXITCODE
    }
    catch {
        throw "Easy compatibility dependency import probe failed: $($_.Exception.Message)"
    }
    if ($dependencyExitCode -ne 0) {
        throw "Easy compatibility dependency import probe failed with exit code ${dependencyExitCode}: $($dependencyOutput -join [Environment]::NewLine)"
    }

    $dependencyLine = [string]($dependencyOutput | Select-Object -Last 1)
    $dependencyParts = @($dependencyLine.Split('|'))
    if ($dependencyParts.Count -ne 2) {
        throw "Easy compatibility dependency import probe returned an unexpected value: '$dependencyLine'."
    }
    if ($dependencyParts[0] -ne $easyCompatibilityPefileVersion) {
        throw "Easy compatibility offline gate requires pefile==$easyCompatibilityPefileVersion; found '$($dependencyParts[0])'."
    }
    if ($dependencyParts[1] -ne $easyCompatibilityCapstoneVersion) {
        throw "Easy compatibility offline gate requires capstone==$easyCompatibilityCapstoneVersion; found '$($dependencyParts[1])'."
    }

    return [PSCustomObject]@{
        Path = $pythonPath
        Version = $pythonVersion
        PefileVersion = $dependencyParts[0]
        CapstoneVersion = $dependencyParts[1]
    }
}

function Invoke-EasyCompatibilityOfflineGate {
    Assert-EasyCompatibilityFrozenFiles
    $python = Resolve-EasyCompatibilityPython
    $validatorPath = Join-Path $PSScriptRoot 'tools\re\san9_easy_manifest.py'
    $selfTestPath = Join-Path $PSScriptRoot 'tools\re\san9_easy_manifest_selftest.py'
    $manifestPath = Join-Path $PSScriptRoot 'docs\easy-compatibility-manifest.json'

    & $python.Path $validatorPath validate --manifest $manifestPath
    if ($LASTEXITCODE -ne 0) {
        throw "Easy compatibility manifest validation failed with exit code $LASTEXITCODE."
    }

    & $python.Path $selfTestPath
    if ($LASTEXITCODE -ne 0) {
        throw "Easy compatibility offline self-test failed with exit code $LASTEXITCODE."
    }

    Write-Host "Easy compatibility offline gate: PASS (Python $($python.Version), pefile==$($python.PefileVersion), capstone==$($python.CapstoneVersion); 4 frozen SHA-256 values verified)."
}

Invoke-EasyCompatibilityOfflineGate

try {
    if (-not $LocalRuntimeFallback) {
        $publishLock = Enter-PublishLock -Root $artifactsDirectory -BuildId $buildId
        Recover-PublishTransaction -Root $artifactsDirectory
    }

    [IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($intermediateDirectory) | Out-Null

    Assert-ProductionInputRetired `
        -ProductionRoots @(
            (Split-Path -Parent $uiProject),
            (Split-Path -Parent $adapterProject),
            (Split-Path -Parent $coreProject)) `
        -UiProjectPath $uiProject `
        -AllowedControllerLauncherPath $nativeControllerLauncherPath `
        -AllowedGameFocusHandoffPath $nativeGameFocusHandoffPath
    Assert-NativeControllerLauncherClosed -LauncherPath $nativeControllerLauncherPath
    Assert-NativeGameFocusHandoffClosed -HandoffPath $nativeGameFocusHandoffPath
    Invoke-NativeS8RuntimeBuild `
        -BuildScript $nativeM2bBuildScript `
        -ArtifactDirectory $nativeArtifactDirectory `
        -StagingDirectory $stagingDirectory `
        -ExpectedHashes $nativeRuntimeExpectedHashes

    Invoke-FrameworkBuild -ProjectPath $coreProject
    Invoke-FrameworkBuild -ProjectPath $adapterProject
    Invoke-FrameworkBuild -ProjectPath $bridgeProtocolProject
    Invoke-FrameworkBuild -ProjectPath $coreTestProject
    Invoke-FrameworkBuild -ProjectPath $diagnosticsProject
    Invoke-FrameworkBuild -ProjectPath $v1DiagnosticsProject
    Invoke-FrameworkBuild -ProjectPath $v2DiagnosticsProject
    Invoke-FrameworkBuild -ProjectPath $selfTestProject
    Invoke-FrameworkBuild -ProjectPath $v1SelfTestProject
    Invoke-FrameworkBuild -ProjectPath $v2SelfTestProject
    Invoke-FrameworkBuild -ProjectPath $v4RootTraceProject
    Invoke-FrameworkBuild -ProjectPath $bridgeSelfTestProject
    Invoke-FrameworkBuild -ProjectPath $uiProject
    Invoke-FrameworkBuild -ProjectPath $uiStateTestProject

    $diagnosticsOutput = Join-Path $stagingDirectory 'San9AutoDomestic.V0Diagnostics.exe'
    $v1DiagnosticsOutput = Join-Path $stagingDirectory 'San9AutoDomestic.V1ReadDiagnostics.exe'
    $v2DiagnosticsOutput = Join-Path $stagingDirectory 'San9AutoDomestic.V2AvailabilityDiagnostics.exe'
    $selfTestOutput = Join-Path $stagingDirectory 'San9AutoDomestic.V0SelfTest.exe'
    $v1SelfTestOutput = Join-Path $stagingDirectory 'San9AutoDomestic.V1ReadSelfTest.exe'
    $v2SelfTestOutput = Join-Path $stagingDirectory 'San9AutoDomestic.V2AvailabilitySelfTest.exe'
    $v4RootTraceOutput = Join-Path $stagingDirectory 'San9AutoDomestic.V4RootTrace.exe'
    $bridgeSelfTestOutput = Join-Path $stagingDirectory 'San9AutoDomestic.Bridge.SelfTest.exe'
    $coreTestOutput = Join-Path $stagingDirectory 'San9AutoDomestic.Core.Tests.exe'
    $uiStateTestOutput = Join-Path $stagingDirectory 'San9AutoDomestic.UI.StateTests.exe'
    $uiOutput = Join-Path $stagingDirectory 'San9AutoDomestic.exe'
    $coreOutput = Join-Path $stagingDirectory 'San9AutoDomestic.Core.dll'
    $adapterOutput = Join-Path $stagingDirectory 'San9AutoDomestic.Adapter.San9Pk101.dll'
    $bridgeProtocolOutput = Join-Path $stagingDirectory 'San9AutoDomestic.Bridge.Protocol.dll'
    $configurationOutput = Join-Path $stagingDirectory 'config\default.json'
    $nativeControllerOutput = Join-Path $stagingDirectory 'runtime\controller.exe'
    $nativeBasicBridgeOutput = Join-Path $stagingDirectory 'runtime\bridge_s8_basic_batch.dll'
    $nativeWealthyBridgeOutput = Join-Path $stagingDirectory 'runtime\bridge_s8_wealthy_batch.dll'
    $requiredOutputs = @(
        $coreOutput,
        $adapterOutput,
        $bridgeProtocolOutput,
        $configurationOutput,
        $uiOutput,
        $coreTestOutput,
        $uiStateTestOutput,
        $selfTestOutput,
        $v1SelfTestOutput,
        $v2SelfTestOutput,
        $v4RootTraceOutput,
        $bridgeSelfTestOutput,
        $diagnosticsOutput,
        $v1DiagnosticsOutput,
        $v2DiagnosticsOutput,
        $nativeControllerOutput,
        $nativeBasicBridgeOutput,
        $nativeWealthyBridgeOutput
    )
    foreach ($requiredOutput in $requiredOutputs) {
        if (-not [IO.File]::Exists($requiredOutput)) {
            throw "Expected staged output is missing: '$requiredOutput'."
        }
    }

    Assert-ProductionStagingAllowlist -StagingDirectory $stagingDirectory

    $retiredProductOutputs = @(
        Get-ChildItem -LiteralPath $stagingDirectory -File -Recurse |
            Where-Object {
                $_.Name -like 'San9AutoDomestic.Input.*' `
                    -or $_.Name -like 'San9AutoDomestic.SingleCommandRunner.*'
            })
    if ($retiredProductOutputs.Count -ne 0) {
        throw "Retired input executor artifacts entered the production staging directory: $($retiredProductOutputs.FullName -join ', ')."
    }

    if (-not $NoTests) {
        & $coreTestOutput
        if ($LASTEXITCODE -ne 0) {
            throw "Core test suite failed with exit code $LASTEXITCODE."
        }

        & $uiStateTestOutput
        if ($LASTEXITCODE -ne 0) {
            throw "Persistent UI state test suite failed with exit code $LASTEXITCODE."
        }

        & $selfTestOutput
        if ($LASTEXITCODE -ne 0) {
            throw "V0 metadata self-test failed with exit code $LASTEXITCODE."
        }

        & $v1SelfTestOutput
        if ($LASTEXITCODE -ne 0) {
            throw "V1 read self-test failed with exit code $LASTEXITCODE."
        }

        & $v2SelfTestOutput
        if ($LASTEXITCODE -ne 0) {
            throw "V2 availability self-test failed with exit code $LASTEXITCODE."
        }

        & $v4RootTraceOutput --self-test
        if ($LASTEXITCODE -ne 0) {
            throw "V4 root trace synthetic self-test failed with exit code $LASTEXITCODE."
        }

        & $bridgeSelfTestOutput --self-test
        if ($LASTEXITCODE -ne 0) {
            throw "Offline bridge protocol synthetic self-test failed with exit code $LASTEXITCODE."
        }
    }

    if ($RunDiagnostics) {
        & $selfTestOutput --live-only
        if ($LASTEXITCODE -ne 0) {
            throw "V0 live read-only self-test failed with exit code $LASTEXITCODE."
        }

        & $diagnosticsOutput
        if ($LASTEXITCODE -ne 0) {
            throw "V0 diagnostics failed with exit code $LASTEXITCODE."
        }

        & $v1DiagnosticsOutput
        if ($LASTEXITCODE -ne 0) {
            throw "V1 read diagnostics failed with exit code $LASTEXITCODE."
        }

        & $v2DiagnosticsOutput
        if ($LASTEXITCODE -ne 0) {
            throw "V2 availability diagnostics failed with exit code $LASTEXITCODE."
        }
    }

    Assert-ProductionStagingAllowlist -StagingDirectory $stagingDirectory

    if ($LocalRuntimeFallback) {
        Write-BuildMarker -Directory $stagingDirectory -BuildId $buildId -Status 'LocalRuntimeVerified'
        if ([IO.Directory]::Exists($localVerifiedDirectory)) {
            throw "Refusing to replace existing local verification output '$localVerifiedDirectory'."
        }

        [IO.Directory]::Move($stagingDirectory, $localVerifiedDirectory)
        $retainedLocalVerified = $true
        Write-Host "Local runtime fallback build '$buildId' passed every test and was retained at '$localVerifiedDirectory'. Published bin was not inspected or changed."
        return
    }

    if ($NoTests) {
        Write-BuildMarker -Directory $stagingDirectory -BuildId $buildId -Status 'Unverified'
        [IO.Directory]::Move($stagingDirectory, $unverifiedDirectory)
        $retainedUnverified = $true
        Write-Host "Built without tests. Unverified output was retained at '$unverifiedDirectory' and bin was not changed."
        return
    }

    Write-BuildMarker -Directory $stagingDirectory -BuildId $buildId -Status 'Verified'
    Publish-VerifiedStaging -Root $artifactsDirectory -StagingDirectory $stagingDirectory -BuildId $buildId
    $published = $true
    Write-Host "Built, tested, and transactionally published build '$buildId' to '$(Join-Path $artifactsDirectory $publishedDirectoryName)'."
}
finally {
    if (-not $published -and -not $retainedUnverified -and -not $retainedLocalVerified -and [IO.Directory]::Exists($stagingDirectory)) {
        $checkedStaging = Assert-ChildPath -Root $artifactsDirectory -Path $stagingDirectory
        [IO.Directory]::Delete($checkedStaging, $true)
    }

    if ([IO.Directory]::Exists($intermediateDirectory)) {
        $checkedIntermediate = Assert-ChildPath -Root $artifactsDirectory -Path $intermediateDirectory
        [IO.Directory]::Delete($checkedIntermediate, $true)
    }

    if ($null -ne $publishLock) {
        $publishLock.Dispose()
    }
}
