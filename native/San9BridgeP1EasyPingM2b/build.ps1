[CmdletBinding()]
param([Parameter(Mandatory=$false)][string]$ArtifactRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repo 'tools\artifacts\San9BridgeP1EasyPingM2b\final'
}
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$rootB = Join-Path $ArtifactRoot 'determinism-second-root'
$wire = Join-Path $repo 'native\San9BridgeP1Wire'
$easy = Join-Path $repo 'native\San9BridgeP1EasyPing'
$m2 = Join-Path $repo 'native\San9BridgeP1EasyPingM2'
$manifest = Join-Path $repo 'docs\easy-compatibility-manifest.json'
$generator = Join-Path $repo 'tools\re\generate_p1_native_easy_manifest.py'
$audit = Join-Path $PSScriptRoot 'audit_pe.py'

$expected = @{
    Offline = '173A031B72D65325F9D1EC5DE6B7AC28920B43EA944F1BE9DCD0ED8988B201A9'
    Controller = '4B9F79D4F0B258D4C4196EC4BDDBC99B6769EF26B2D943ED32C2B18FC4506764'
    Dll = '6DA9FA89AC4D7E9C794773EA0EF2D6F9DB76D08A52B8BE5F7C29C88662C2B09E'
    ApplyDll = '3DA898AA3B5A26D976B73608701546635B3BBEB72AA50CEBB735F42D5E8911E3'
    CultivateDll = '49C4D25DED19E3D1D8905ACCA01362CF3D990CAD1C05137797F2A2C5FFF44717'
    PatrolDll = 'B2DC98F04CCC2091CF57106C991422697D16E28738BE8C33FC9D1CBC2769C0A9'
    TrainDll = '82539E6745F5341377FC5278A8A297C328F46F6885F1BE42DD7970695B37A1EA'
    RepairDll = '91D74FFFC27FFFE8111A1039E76BEE3A40A2377F64645BA6EF8A3AC16015B4D2'
    S8BasicDll = '0BB305BC6E121ABDDD268E8213F093F20BD547CDB7A184EC8180806C34852A65'
    S8WealthyDll = 'FC24FFB5472E955A8EF795DAD42DC447A26323002B533709E2057D4F1930420C'
    Header = 'CCA306579C23581A660CD6972CC37104DBE5339094DDD841FED8C91A40E98F7A'
}
$pins = @(
    @((Join-Path $m2 'include\san9_p1_m2.h'),'524AD0EB497630D6EAE1052EA47899D005A1D2A829C5976F104BF7CA28E967BD'),
    @((Join-Path $m2 'src\m2.c'),'7FD8C4E1C6CAA33D5BDB43453C63E0F8C8B9B15D43467C78EADC1E87276B3F33'),
    @((Join-Path $wire 'include\san9_p1_wire.h'),'FA109C829A09EB5D47034D1CD2329357C27C2A3969621E3AB04168AC5E4550B5'),
    @((Join-Path $wire 'src\p1_wire.c'),'AE891EC63E3200430CF0759C36B44137065153831299FFE2A813802208A432B6'),
    @((Join-Path $wire 'src\sha256.h'),'07434B03911299F4A977C95BF7816D0FD82D7A48D2894D008FBD888E7849CEF1'),
    @((Join-Path $wire 'src\sha256.c'),'C10F015DD3C60AB82AD1440752E69F977D11CB26F94DC1FAD883D020E77CF97D'),
    @((Join-Path $easy 'include\san9_p1_easy_gate.h'),'AC74F16B23659162BA30229C4B3469FEBA603CB9E54CC986C75EC80531527B5C'),
    @((Join-Path $easy 'src\easy_gate.c'),'E29BE243DF07FBF0E2878CD06C7553D2D1DF2A251FB10758734186A4A4C12744'),
    @($generator,'8006B8C556623C01FE505E7E6A1EAB23BAD655B17C3FA9DCF4C0C0DCEA43F4AA'),
    @($manifest,'72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE'),
    @($audit,'6A54E57F82AF3E6D7205479BFD8E58B7855A7CBB76A08146C2514B1C054F186C'),
    @((Join-Path $PSScriptRoot 'include\san9_p1_m2b.h'),'15047CA72FDA40D49F76ABD92C7EDAD6DE426B9CEA00DA303694F86A08979ECE'),
    @((Join-Path $PSScriptRoot 'include\s5_commerce.h'),'5CE2C08F3EEFD56EC7F07B6B8525636A479A431ECFBA45D18F38E82C47DE6B05'),
    @((Join-Path $PSScriptRoot 'include\s5_current_context.h'),'82F0CA8D12B7480EFADA7C483F65C9724953BEF222C1C3B72EAD05BAC0BCF4D8'),
    @((Join-Path $PSScriptRoot 'include\s3_readonly.h'),'ACB3872BB28E93FB274984BDC5D24F781CAF53BC86AC726D77E82AA59ABAB5FA'),
    @((Join-Path $PSScriptRoot 'include\easy_page_normalization.h'),'C9A70B167F1184D677F00F77A13FC4FA0BEB5CB8457A8B2BD648EA096D0A314C'),
    @((Join-Path $PSScriptRoot 'include\discover.h'),'9453BDAA01618DE55A5FC3980AFF1C2EA04626CCCD9E23644DA2CCABCCC202B7'),
    @((Join-Path $PSScriptRoot 'src\s3_readonly.c'),'1186803BC3FA47A949061CB964CB2760548B8F46CFABE6FDAB37AAC0CB51443E'),
    @((Join-Path $PSScriptRoot 'src\s5_commerce.c'),'845C03275FB3CA51EC9128AD86BEFC0D158800FF92C65145D1523B2F27AA42F6'),
    @((Join-Path $PSScriptRoot 'src\s5_current_context.c'),'0BACEDF3301C8EC81090985E247CC66913673E3ED6E81FE0382FD49B930D13B7'),
    @((Join-Path $PSScriptRoot 'src\s5_evidence.c'),'20515D4715A23168A5BE163C46606821C643938D80C47C79C656132732DDF09B'),
    @((Join-Path $PSScriptRoot 'src\controller.c'),'44360304B3CE9F395CB809FD49C0213736AF75D39CB8C8B8BF3DB1DD71BFC92C'),
    @((Join-Path $PSScriptRoot 'src\discover.c'),'B7687C7823E86C689B5B94FE345149786683D881A06E400400089DE7FBA7D5B2'),
    @((Join-Path $PSScriptRoot 'src\bridge_dll.c'),'6D1BDEDA7A902166F953E389C9E72758ED6859A4FBEDF921B0D9979937AA04FE'),
    @((Join-Path $PSScriptRoot 'src\idle_bridge.S'),'CD5F6A8E8BD487E7B3B55814D1C3AEEA20B6DAF32BA4D0CAEA3DD8805ADC90F2'),
    @((Join-Path $PSScriptRoot 'src\s5_modal_tick.S'),'AA5D97F0F8B7312C6AB25F41E710EA6BAED228FA0113B56313602D3D4D48C758'),
    @((Join-Path $PSScriptRoot 'src\s5_v8_menu_tick.S'),'6360C4C424C337200CEA940FC2B90D2DB5D92A0B067AF776E9CFA49D4DF4FF89'),
    @((Join-Path $PSScriptRoot 'src\s5_thunks.S'),'2FE1E80370F89CA2F845423AFFDD9D6D90B5EC4140F6BF235907F4D9D4B3CD18'),
    @((Join-Path $PSScriptRoot 'src\s5_apply_once_thunks.S'),'3487A74D6D6EBE9C45F71F417E33296B5EF3C45EDDFCC881621C4A07C735BA9D'),
    @((Join-Path $PSScriptRoot 'src\offline_selftest.c'),'E579E507607394F3AE737EA2F9D4266A67625147FF4A5FFBC8A7FDF1492072D0'),
    @((Join-Path $PSScriptRoot 'San9BridgeP1M2b.def'),'E3F5E4D2FB2FBC308BA7B5E1720B83BA773A12219C29A212FAD210ECE8C840E5')
)
foreach ($pin in $pins) {
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $pin[0]).Hash
    if (-not [string]::Equals($actual,$pin[1],[StringComparison]::OrdinalIgnoreCase)) {
        throw "source identity rejected: path=$($pin[0]) actual=$actual expected=$($pin[1])"
    }
}

$discoverSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\discover.c') -Raw
$discoverHeader = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'include\discover.h') -Raw
if ($discoverSource -match 'san9_p1_m2b\.h' -or
    $discoverHeader -match 'san9_p1_m2b\.h') {
    throw 'inspect discovery boundary must not include the install contract'
}
foreach ($token in @('WriteProcessMemory','VirtualProtectEx','VirtualAllocEx',
        'CreateRemoteThread','SetWindowsHookEx','PostThreadMessage','CreateFileMapping',
        'OpenFileMapping','MapViewOfFile','LoadLibrary','FreeLibrary','SendInput')) {
    if ($discoverSource.Contains($token) -or $discoverHeader.Contains($token)) {
        throw "inspect discovery write/install capability rejected: $token"
    }
}

$zig = (Get-Command zig.exe -ErrorAction Stop).Source
$python = (Get-Command python.exe -ErrorAction Stop).Source
if (((& $zig version) | Out-String).Trim() -ne '0.16.0' -or
    (Get-FileHash -Algorithm SHA256 -LiteralPath $zig).Hash -ne
        '086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864') {
    throw 'pinned Zig identity rejected'
}
if (((& $python --version 2>&1) | Out-String).Trim() -ne 'Python 3.13.0' -or
    (Get-FileHash -Algorithm SHA256 -LiteralPath $python).Hash -ne
        '62EBC90A2884BB63A0CD67E789CAFDD51E771EEE043587E2354327B4CCC9BB05') {
    throw 'pinned Python identity rejected'
}

function Build-M2b([string]$root) {
    $gen = Join-Path $root 'staging\include'
    New-Item -ItemType Directory -Force -Path $gen | Out-Null
    & $python $generator --manifest $manifest --output (Join-Path $gen 'san9_p1_easy_manifest.gen.h')
    if ($LASTEXITCODE -ne 0) { throw 'manifest generation failed' }
    $generatedHeader = Join-Path $gen 'san9_p1_easy_manifest.gen.h'
    $headerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedHeader).Hash
    if ($headerHash -ne $expected.Header) { throw "generated header rejected: $headerHash" }
    $includes = @('-I',(Join-Path $PSScriptRoot 'include'),'-I',(Join-Path $m2 'include'),
        '-I',(Join-Path $wire 'include'),'-I',(Join-Path $wire 'src'),
        '-I',(Join-Path $easy 'include'),'-I',$gen)
    $common = @('cc','-target','x86-windows-gnu','-std=c11','-O2','-Wall','-Wextra',
        '-Wpedantic','-Werror','-DS3_READONLY_BUILD=1','-DS5_NO_APPLY_BUILD=1','-s') + $includes + @(
        (Join-Path $PSScriptRoot 'src\s3_readonly.c'),
        (Join-Path $PSScriptRoot 'src\s5_commerce.c'),
        (Join-Path $PSScriptRoot 'src\s5_evidence.c'),
        (Join-Path $m2 'src\m2.c'),
        (Join-Path $wire 'src\p1_wire.c'),(Join-Path $wire 'src\sha256.c'),
        (Join-Path $easy 'src\easy_gate.c'))
    & $zig @common '-DSAN9_S5_CONTEXT_NO_HANDLE=1' `
        '-DSAN9_S5_MODAL_OFFLINE_FAKE=1' `
        '-DSAN9_S5_V8_OFFLINE_FAKE=1' `
        (Join-Path $PSScriptRoot 'src\s5_current_context.c') `
        (Join-Path $PSScriptRoot 'src\s5_modal_tick.S') `
        (Join-Path $PSScriptRoot 'src\s5_v8_menu_tick.S') `
        (Join-Path $PSScriptRoot 'src\offline_selftest.c') '-o' (Join-Path $root 'offline.exe')
    if ($LASTEXITCODE -ne 0) { throw 'offline compile failed' }
    & $zig @common (Join-Path $PSScriptRoot 'src\s5_current_context.c') `
        (Join-Path $PSScriptRoot 'src\controller.c') (Join-Path $PSScriptRoot 'src\discover.c') `
        '-o' (Join-Path $root 'controller.exe') '-luser32' '-ladvapi32' '-lbcrypt'
    if ($LASTEXITCODE -ne 0) { throw 'controller compile failed' }
    $dllArguments = $common + @('-DSAN9_P1_M2B_EXPORTS_VIA_DEF','-shared',
        '-DSAN9_S5_CONTEXT_NO_HANDLE=1',
        (Join-Path $PSScriptRoot 'src\s5_current_context.c'),
        (Join-Path $PSScriptRoot 'src\bridge_dll.c'),
        (Join-Path $PSScriptRoot 'src\s5_modal_tick.S'),
        (Join-Path $PSScriptRoot 'src\s5_v8_menu_tick.S'),
        (Join-Path $PSScriptRoot 'src\s5_thunks.S'),
        (Join-Path $PSScriptRoot 'src\idle_bridge.S'),
        (Join-Path $PSScriptRoot 'San9BridgeP1M2b.def'),'-o',
        (Join-Path $root 'bridge.dll'),'-luser32')
    & $zig @dllArguments
    if ($LASTEXITCODE -ne 0) { throw 'DLL compile failed' }
    $applyDllArguments = $dllArguments + @(
        '-DS5_APPLY_ONCE_BUILD=1',
        (Join-Path $PSScriptRoot 'src\s5_apply_once_thunks.S'))
    $outputIndex = [Array]::IndexOf($applyDllArguments, (Join-Path $root 'bridge.dll'))
    if ($outputIndex -lt 0) { throw 'apply DLL output argument missing' }
    $applyDllArguments[$outputIndex] = Join-Path $root 'bridge_apply_once.dll'
    & $zig @applyDllArguments
    if ($LASTEXITCODE -ne 0) { throw 'APPLY_ONCE DLL compile failed' }
    $cultivateDllArguments = $dllArguments + @(
        '-DS6_CULTIVATE_APPLY_ONCE_BUILD=1',
        (Join-Path $PSScriptRoot 'src\s5_apply_once_thunks.S'))
    $cultivateOutputIndex = [Array]::IndexOf(
        $cultivateDllArguments, (Join-Path $root 'bridge.dll'))
    if ($cultivateOutputIndex -lt 0) {
        throw 'Cultivate APPLY_ONCE DLL output argument missing'
    }
    $cultivateDllArguments[$cultivateOutputIndex] =
        Join-Path $root 'bridge_s6_cultivate_apply_once.dll'
    & $zig @cultivateDllArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Cultivate APPLY_ONCE DLL compile failed'
    }
    $patrolDllArguments = $dllArguments + @(
        '-DS6_PATROL_APPLY_ONCE_BUILD=1',
        (Join-Path $PSScriptRoot 'src\s5_apply_once_thunks.S'))
    $patrolOutputIndex = [Array]::IndexOf(
        $patrolDllArguments, (Join-Path $root 'bridge.dll'))
    if ($patrolOutputIndex -lt 0) {
        throw 'Patrol APPLY_ONCE DLL output argument missing'
    }
    $patrolDllArguments[$patrolOutputIndex] =
        Join-Path $root 'bridge_s6_patrol_apply_once.dll'
    & $zig @patrolDllArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Patrol APPLY_ONCE DLL compile failed'
    }
    $trainDllArguments = $dllArguments + @(
        '-DS6_TRAIN_APPLY_ONCE_BUILD=1',
        (Join-Path $PSScriptRoot 'src\s5_apply_once_thunks.S'))
    $trainOutputIndex = [Array]::IndexOf(
        $trainDllArguments, (Join-Path $root 'bridge.dll'))
    if ($trainOutputIndex -lt 0) {
        throw 'Train APPLY_ONCE DLL output argument missing'
    }
    $trainDllArguments[$trainOutputIndex] =
        Join-Path $root 'bridge_s6_train_apply_once.dll'
    & $zig @trainDllArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Train APPLY_ONCE DLL compile failed'
    }
    $repairDllArguments = $dllArguments + @(
        '-DS6_REPAIR_APPLY_ONCE_BUILD=1',
        (Join-Path $PSScriptRoot 'src\s5_apply_once_thunks.S'))
    $repairOutputIndex = [Array]::IndexOf(
        $repairDllArguments, (Join-Path $root 'bridge.dll'))
    if ($repairOutputIndex -lt 0) {
        throw 'Repair APPLY_ONCE DLL output argument missing'
    }
    $repairDllArguments[$repairOutputIndex] =
        Join-Path $root 'bridge_s6_repair_apply_once.dll'
    & $zig @repairDllArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Repair APPLY_ONCE DLL compile failed'
    }
    $s8BasicDllArguments = $dllArguments + @(
        '-DS8_BASIC_BATCH_BUILD=1',
        (Join-Path $PSScriptRoot 'src\s5_apply_once_thunks.S'))
    $s8BasicOutputIndex = [Array]::IndexOf(
        $s8BasicDllArguments, (Join-Path $root 'bridge.dll'))
    if ($s8BasicOutputIndex -lt 0) {
        throw 'S8 Basic batch DLL output argument missing'
    }
    $s8BasicDllArguments[$s8BasicOutputIndex] =
        Join-Path $root 'bridge_s8_basic_batch.dll'
    & $zig @s8BasicDllArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'S8 Basic batch DLL compile failed'
    }
    $s8WealthyDllArguments = $dllArguments + @(
        '-DS8_WEALTHY_BATCH_BUILD=1',
        (Join-Path $PSScriptRoot 'src\s5_apply_once_thunks.S'))
    $s8WealthyOutputIndex = [Array]::IndexOf(
        $s8WealthyDllArguments, (Join-Path $root 'bridge.dll'))
    if ($s8WealthyOutputIndex -lt 0) {
        throw 'S8 Wealthy batch DLL output argument missing'
    }
    $s8WealthyDllArguments[$s8WealthyOutputIndex] =
        Join-Path $root 'bridge_s8_wealthy_batch.dll'
    & $zig @s8WealthyDllArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'S8 Wealthy batch DLL compile failed'
    }
}

Build-M2b $ArtifactRoot
Build-M2b $rootB
$files = @{
    Offline = 'offline.exe'; Controller = 'controller.exe'; Dll = 'bridge.dll';
    ApplyDll = 'bridge_apply_once.dll';
    CultivateDll = 'bridge_s6_cultivate_apply_once.dll'
    PatrolDll = 'bridge_s6_patrol_apply_once.dll'
    TrainDll = 'bridge_s6_train_apply_once.dll'
    RepairDll = 'bridge_s6_repair_apply_once.dll'
    S8BasicDll = 'bridge_s8_basic_batch.dll'
    S8WealthyDll = 'bridge_s8_wealthy_batch.dll'
}
foreach ($role in $files.Keys) {
    $a = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $ArtifactRoot $files[$role])).Hash
    $b = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $rootB $files[$role])).Hash
    if ($a -ne $b -or $a -ne $expected[$role]) {
        throw "deterministic whole image rejected: role=$role A=$a B=$b expected=$($expected[$role])"
    }
}
$auditArgs = @('--offline',(Join-Path $ArtifactRoot 'offline.exe'),
    '--controller',(Join-Path $ArtifactRoot 'controller.exe'),'--dll',(Join-Path $ArtifactRoot 'bridge.dll'),
    '--apply-dll',(Join-Path $ArtifactRoot 'bridge_apply_once.dll'),
    '--cultivate-dll',(Join-Path $ArtifactRoot 'bridge_s6_cultivate_apply_once.dll'),
    '--patrol-dll',(Join-Path $ArtifactRoot 'bridge_s6_patrol_apply_once.dll'),
    '--train-dll',(Join-Path $ArtifactRoot 'bridge_s6_train_apply_once.dll'),
    '--repair-dll',(Join-Path $ArtifactRoot 'bridge_s6_repair_apply_once.dll'),
    '--offline-sha',$expected.Offline,'--controller-sha',$expected.Controller,
    '--dll-sha',$expected.Dll,'--apply-dll-sha',$expected.ApplyDll,
    '--cultivate-dll-sha',$expected.CultivateDll,
    '--patrol-dll-sha',$expected.PatrolDll,
    '--train-dll-sha',$expected.TrainDll,
    '--repair-dll-sha',$expected.RepairDll)
Write-Host 'P1_M2B_STAGE COMPILED_LIVE_NOT_LOADED'
& $python $audit @auditArgs
if ($LASTEXITCODE -ne 0) { throw 'pre-execution PE audit failed' }
Write-Host 'P1_M2B_STAGE PREEXEC_STATIC_AUDIT_PASS_LIVE_NOT_LOADED'

# This is the only generated artifact the build executes.
& (Join-Path $ArtifactRoot 'offline.exe')
if ($LASTEXITCODE -ne 0) { throw 'offline selftest failed' }
foreach ($role in $files.Keys) {
    $after = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $ArtifactRoot $files[$role])).Hash
    if ($after -ne $expected[$role]) { throw "post-selftest hash changed: role=$role" }
}
& $python $audit @auditArgs
if ($LASTEXITCODE -ne 0) { throw 'post-execution PE audit failed' }
Write-Host "P1_M2B_BUILD PASS offline=$($expected.Offline) controller=$($expected.Controller) dll=$($expected.Dll) apply_dll=$($expected.ApplyDll) cultivate_dll=$($expected.CultivateDll) patrol_dll=$($expected.PatrolDll) train_dll=$($expected.TrainDll) repair_dll=$($expected.RepairDll) s8_basic_dll=$($expected.S8BasicDll) s8_wealthy_dll=$($expected.S8WealthyDll) live_loaded=0"
