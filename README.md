# San9AutoDomestic

> 这是供外部深度分析使用的公开源码快照。仓库不包含游戏本体、Easy 二进制、存档、构建产物或未脱敏的本机日志。建议按 [OPEN-ISSUES.md](OPEN-ISSUES.md) → [STATE.md](STATE.md) → [docs/PRD.md](docs/PRD.md) → `diagnostics/` 的顺序阅读。

`San9AutoDomestic` is an external helper for the exact 32-bit San9 PK 1.01 build described in [docs/PRD.md](docs/PRD.md). The resident UI currently provides exact-target diagnostics and config-driven **read-only** previews. Its `基础内政` and `有钱内政` profile buttons are permanently disabled and labelled `无感执行开发中·未开放`; they cannot authorize or reach mouse, keyboard, memory-write, injection, or game-command submission code.

The former visible-row Win32 input prototype has been retired from the product route. Its source may remain as historical/offline research, but the production UI has no project or assembly reference to it, the root build does not compile or test it, and the strict staging allowlist rejects its DLL, runner, or any unexpected artifact. The target execution architecture is an Easy-compatible, in-process native main-thread bridge with no foreground input; that capability is not yet open.

## Start the persistent helper window

After a verified build, start the resident UI by double-clicking `tools\artifacts\bin\San9AutoDomestic.exe`. This is the only persistent helper window. Files whose names contain `V0`, `V1`, `V2`, `V4`, `Diagnostics`, `Probe`, or `SelfTest` are bounded engineering tools; they print or verify their result and then exit by design.

The UI stays open when the game is absent or a read-only check fails. Its 2.5-second presence check reads only the target window, PID, exact image path, and process-creation generation with `QUERY_LIMITED_INFORMATION`; it does not retain a game handle or run the full V2 scan on every tick. A read-ready state enables only `预览全部方案（只读）`; both profile buttons and the stop button remain disabled. A second launch signals the existing window to restore instead of starting another copy.

Transient presence/read-worker/render faults use at most four automatic retries at 2.5, 5, 10, and 20 seconds; one retry grant can be consumed only once. A structured blocking result is stable and is rechecked only after an explicit refresh or a PID/process-creation generation change. The diagnostic log retains only the newest 200,000 characters and 2,000 lines, and every profile reload removes ToolTip registrations and disposes the previous button generation.

## V0 safety boundary

V0 can:

- validate the exact executable path, file version, byte size, SHA-256, and x86 PE machine;
- find a unique game process only when `KOEI_SAN9WINDOW` and the exact process image path agree;
- open that PID with only `PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`;
- enumerate known conflicting modifier processes and loaded modules;
- return structured diagnostic DTOs.

V0 cannot write, allocate, protect, inject, create a remote thread, submit a game command, or change a save. The adapter does not import `WriteProcessMemory`, `VirtualAllocEx`, `VirtualProtectEx`, or `CreateRemoteThread`.

The `ExecutionAllowed` report field is a compatibility gate, not a submission capability by itself. The user's exact daily `San9PKEasy.exe` and loaded `Easy.dll` are accepted only after an A/B-stable read-only check of their exact identity, all 32 Easy code redirections, all 6 auxiliary ownership points, page/HWND state, and the original idle anchors. A different Easy build, missing or duplicate Easy, Hard, SanIX, an unknown proxy DLL, a partial hook epoch, or any identity drift remains blocking. The resulting ticket is explicitly non-authorizing and cannot be used as a bridge security capability.

## V1 read-only snapshot

V1 reads the fixed FORCE, CITY, and PERSON tables twice and accepts a snapshot only when every field used by the parser is stable. It currently identifies the player main force, all player-controlled corps, directly controlled cities, resident officers, and their effective leadership/might/intelligence/politics values. The read connection is released before the report reaches the UI.

V1 intentionally exports a `StructureOnly` Core snapshot. Command capability is added only by the separate V2 observer; V1 cannot be promoted to a planning-ready snapshot by a public caller.

Detailed evidence and value domains are recorded in [docs/reverse-engineering-v1-read.md](docs/reverse-engineering-v1-read.md).

## V2 read-only availability and preview

V2 performs a V1-before/V1-after revalidation, raw A/B/A observation, process-generation check, and 51 disk/live code-anchor checks. It reads the five domestic command gates, the `person+0xE8 bit12` ready state, and the original `city+0xDC` officer order. The unverified ranking is:

- patrol: intelligence;
- commerce/cultivate: politics;
- train: might;
- repair: leadership;
- ties preserve the original city resident-list order.

The UI can now display all config-generated profiles through `预览全部方案（只读）`. Every click first performs a fresh V2 read; the preview shows its completion time and context-token digest, rejects snapshots older than 30 seconds, and labels every selected row `静态拟选·未验证`. It consumes officers in task order, shares corps money across cities, skips gray/understaffed/underfunded tasks without consuming resources, and handles training as zero-cost on this exact target. It uses dedicated DTOs whose `IsActionable` and `CommitAuthorized` properties are hard-coded `false`; any report, condition, evidence, candidate, ranking, or money contradiction discards the whole profile projection.

`PlanningReady` and `VerifiedNativeCapability` remain `false`. No production executor is wired around those gates, and the preview DTOs remain permanently non-authorizing.

## Retired all-direct-cities input prototype

`src/San9AutoDomestic.Input.Win32` and its offline tests record the former foreground-input experiment. They are not a production fallback, are not referenced by `San9AutoDomestic.UI`, and are excluded from the root build and publish manifest. Historical navigation and screenshot evidence remains useful only for understanding game states; it does not authorize execution.

The current verified publication is BuildId `7638b922c17344a187d9ff724eb0471c`; `San9AutoDomestic.exe` SHA-256 is `B17CA67E79F065557F35CBEB3D07D08EEEC74AE1409D4CC0804E531EB53ABD7C`. The UI state suite passes `22/22`, including a production dependency-closure audit that permits only the enumerated read-only native entry points. The published `bin` contains no `San9AutoDomestic.Input.*` or `San9AutoDomestic.SingleCommandRunner.*` artifact.

Detailed evidence is in [V2 availability](docs/reverse-engineering-v2-availability.md), [V3 command lifecycle](docs/reverse-engineering-v3-execute.md), [V4 dispatcher audit](docs/reverse-engineering-v4-dispatcher.md), and the [verification ledger](docs/VERIFICATION.md).

## V4 read-only root lifecycle sampler

`San9AutoDomestic.V4RootTrace.exe` is a bounded console-only observer. Every sample that could be printed is enclosed by two fresh `Diagnose` passes: the pre-sample and post-sample environments must independently pass exact-file hashing, unique-process discovery, read-only connection, aggregate blocking-issue rejection, and complete conflict-process/module inspection. Both temporary connections are disposed, and both environments must match the session's original PID, process-creation generation, and exact main-module identity.

Inside that clean-environment sandwich, the sampler follows the version-locked `app -> window -> owner -> scene -> schedulerRoot -> child` chain twice. The two complete structures must match across window/owner/scene/root and every task node's address/vptr/tick/child/pending fields. A mismatch is retried at most three times and then stops with `ROOT_TRACE_CHANGED_DURING_SAMPLE`. It also rejects failed reads, bounds child depth, rejects cycles, and validates task vtables and their `+0x0C` function pointers against the exact main-module range. The scheduler root is the `0x607560` task-tree owner created by `0x47EB10`; `+0x30/+0x34/+0x38` are read only from an optional, unique `0x610BC8` domestic-controller descendant. Ordinary descendants can never expose those controller fields.

The default duration is 10 seconds at a 100 ms interval. Both are deliberately bounded, Ctrl+C stops early, and the tool writes samples only to stdout—there is no log-file option:

```powershell
San9AutoDomestic.V4RootTrace.exe --duration-seconds 10 --interval-ms 100
San9AutoDomestic.V4RootTrace.exe --self-test
```

This sampler is non-actionable and does not authorize a game call, command, input, write, injection, debugger attachment, or hardware breakpoint. Its synthetic suite currently passes `14/14`, including between-pass mutation, persistent instability, aggregate blocking-issue, session-generation, non-controller descendant, and duplicate-controller cases. A clean-process baseline is recorded in the verification ledger; it is lifecycle evidence only and does not authorize execution.

## Manual command lifecycle and V6 probe

On a dedicated copied save, one user-operated native `江陵 -> 商业` command has completed the full `select -> submit -> save -> reload -> next turn` validation. Read-only observation confirmed the native top five IDs `253,514,507,467,701`, commerce `230 -> 286`, money `15273 -> 15023`, all five busy bits, the commerce order bit, and ready candidates `43 -> 38`. Reload preserved every result. Advancing one turn cleared the order/busy bits and made commerce available again; commerce stayed `286`, while normal turn settlement changed money independently to `14934`. This proves the observed native lifecycle, not an automatic submission path.

`tools\re\san9_v6_selection_probe.py` is a separate stdout-only selection observer. With no arguments it validates only the exact disk image; `--self-test` is pure in-memory and currently passes `18/18`. Live reading requires an explicit `--pid`, opens only `QUERY | VM_READ`, applies the exact process/module/proxy conflict gate before and after each pass, locates a unique supported UI-task vptr, and accepts the three selection lists only after complete A/B agreement. It permanently reports `execution_authorized=false` and contains no write, injection, Hook, debugger, or input API.

```powershell
py -3 tools\re\san9_v6_selection_probe.py
py -3 tools\re\san9_v6_selection_probe.py --self-test
```

## Offline bridge protocol skeleton

`San9AutoDomestic.Bridge.Protocol.dll` is a process-independent protocol/state-machine foundation, not a game bridge. Its schema-1.0 frame is exactly 288 bytes with explicit little-endian offsets, declared size, CRC32, zero-reserved regions, a 16-byte session nonce, a strict contiguous `UInt64` sequence, request ID, exact-target/context SHA-256 summaries, bounded timestamps, and request/result summaries. Wire-visible state follows only `Pending -> Claimed -> Completed | Rejected`; Claim and Result frames carry the SHA-256 binding of the canonical Pending request.

The state machine permits one unresolved request and rejects duplicates, replays, sequence gaps, expired/future requests, clock regression, cross-session claims/aborts, target or context mismatch, invalid terminal digests, repeated transitions, and post-abort work. Aborting or expiring a Pending request internally passes through Claimed before producing a bound Rejected result. Every protocol/claim/frame authorization property is hard-coded `false`.

`San9AutoDomestic.Bridge.SelfTest.exe --self-test` uses only a bounded, cloning, in-memory duplex transport and a synthetic host. It imports no process, shared-memory, named-pipe, input, injection, or game-write API, and currently passes `25/25`. The default build always runs it in the offline test block; `-RunDiagnostics` does not change its arguments or connect it to the live-diagnostics branch.

CRC32 and unkeyed SHA-256 do not authenticate a hostile peer, the session request-ID history is not yet production-bounded, and the synthetic transport has no durable terminal acknowledgement/retry. The skeleton must not be promoted into an execution bridge without closing those boundaries and completing the separate native lifecycle validation.

## V5.1 native ping-only offline proof

`native\San9BridgeV51` is a separate x86 native proof for a future authenticated main-thread heartbeat. It is not wired into the C# protocol above and deliberately has no business opcode or city/officer payload. The 256-byte HMAC-SHA256 ping frame binds session nonce, strict sequence, request ID, target/context digests, challenge, and a five-second lifetime. Its 576-byte single-slot mailbox supports authenticated terminal responses, explicit recovery from unauthenticated garbage, and a persistent clock-rollback fault that can be cleared only by constructing a new session with a new nonce.

The current DLL cannot be used live: `San9Bridge_Bootstrap` always returns `OFFLINE_ONLY`; the uninitialized `San9Bridge_IdleBridge` returns safely without calling a game function; the controller accepts only `--self-test`; Hook exports can only chain to `CallNextHookEx`. There is no live PID/path argument, Hook installer, process handle, remote-memory API, business call, or hot unload. The PE audit also records two CRT TLS callbacks and `live_loader_lifecycle_proven=false`, so copying or injecting this DLL into the game is explicitly forbidden.

The isolated build locks Zig to version `0.16.0` and the exact compiler SHA-256, requires Python `>=3.11.0` and exactly `pefile==2024.8.26`, then runs protocol `102/102`, DLL `115/115`, and PE audits `12/12 + 11/11`. It is intentionally separate from the root build:

```powershell
.\tools\San9BridgeV51\build.ps1
```

Matching offline artifacts authorize neither live bootstrap nor domestic execution.

## V5.2 ping-only live-bootstrap candidate

`native\San9BridgeV52` is the separately gated successor to V5.1. Its live-opt-in profile can only install an authenticated, one-ping idle wrapper: the wrapper calls the original game idle function exactly once, processes at most one ping with no business opcode or city/officer payload, and then remains pinned until the game process exits. It does not submit a domestic command, write a city/officer/save field, restore the slot, or support hot unload.

Three independent review passes found no remaining medium/high defect in that narrow ping path. The frozen default offline build passes controller `97/97`, DLL `98/98`, and PE audits `18/18 + 14/14`; the live-opt-in artifacts have only been compiled and inspected on disk (`27/27 + 19/19`) and have never been loaded or run. A live ping remains conditional on fresh exact-target/conflict validation, a saved game, and separate explicit user consent to both Hook injection and a persistent-until-restart slot. Regardless of success, failure, timeout, or cleanup result, the game must be restarted before any later test. This conditional ping approval does not authorize automatic domestic execution.

## V9 city-binding boundary

The exact executable exposes no verified `cityId/CCityData* -> domestic controller target` setter. All reviewed `0x7D1/0x7D3` routes carry packed map coordinates and reach `root+0x38` only after native hit testing; the domestic-root city-list route and the other three `CCityListDlg` callers do not write that field. `0x01232474` is a read-only current-operation `CChiikiBuildingData` observation, not an authoritative current-city setter. The offline V9 audit passes `40/40` and has no live mode.

A main-thread compare-exchange of transient `root+0x38` is documented only as a future dynamic-evidence candidate. It remains unauthorized until exact root/scene generation, V2 context, conditional restore, factory snapshot timing, one-shot transaction, and restart-on-uncertainty behavior are independently proven. No live CAS or business dispatch has been implemented.

## V8 offline single-command transaction contract

`prototypes\San9AutoDomestic.V8Transaction` is the authoritative offline contract for one exact native command attempt; it is not referenced by Core, UI, V5.2, or the root publish build. It binds one of the five exact native descriptors, one city/corps, an ordered exact-five officer set, sequence/deadline, process generation, context digest, and request fingerprint. Business conditions known before mutation produce `SkipBeforeMutation`; every uncertainty after mutation produces a fault latch, never an automatic retry.

The coordinator-owned action and cleanup invokers perform a fresh trusted-clock/generation/stage/method check at the actual callback boundary, atomically admit one side effect, invoke outside the global lock exactly once, and settle only after the callback returns. Entered callbacks without authenticated success evidence force `HaltRestart`. Cleanup A/B post reads are ordered, one-shot, bound to the exact side-effect permit, and cannot be issued before cleanup returns. The production DLL exposes no friend assembly, testing/raw-submit seam, callback implementation, or P/Invoke.

The frozen Release/warnings-as-errors suite passes `55/55`, with an additional `20/20` repeated concurrency run. SHA-256: DLL `06690240E44448748F3B2A1A7CAA2F9073253B0A32C3AE328DC1DE8B5B47DEEF`; SelfTest `09B6E6918D44FF9E86AEFE9453BB6E4BCBF3A68CEC8BEFBF29BF186C4CEC1E06`. The added recorder test drives all seven action and four cleanup routes through the production callback switch and compares them with an independent oracle. Independent review approves the offline authority contract only. Live integration still requires OS-authenticated process creation/handle identity, cross-helper single-flight and persistent fault journal, a separately reviewed native adapter, real non-cacheable A/B reads, and a hung-callback watchdog.

## V10 offline native-adapter contract

`native\San9BridgeV10` freezes the exact x86 data contract between a future broker/native adapter and V8. It records all five command descriptors, seven mutating UI routes, V8 cleanup values `1..6`, the 176-byte x86 request ABI, and an independently reproduced V8 request fingerprint. It is deliberately not an executor: all dynamic-lifecycle and live-authorization fields remain false, and the DLL has no process, game-call, write, injection, hook, window-message, or input capability.

The frozen build passes `45/45` internal tests, `202/202` independent-oracle checks, and `27/27` PE checks. SHA-256: DLL `8AECD7C80C6BE0E531F5216DC1883E5B181F2F35AC3F24A10E8562C2D8B9C3EC`; SelfTest `81BD8CC9A1D24AC4ED6CEFD59FBC5C614E6099CCC44FB2878A57469B2D112078`. Independent review found no remaining P1/P2 defect in this offline non-authorizing scope. Passing V10 does not authorize loading it into the game or calling any registered address.

## P0 offline single-command shadow slice

`prototypes\San9AutoDomestic.SingleCommandSlice` connects a frozen configuration profile to a stable city/task cursor and produces at most one structurally validated V8 request at a time. It proves the intended product ordering without connecting the product: direct cities are visited by ascending game ID, tasks stay in configured order, each step re-reads a synthetic authoritative ranking and takes exactly the first five still available officers, and only an explicit shadow commit consumes its in-memory officer/money ledger. Gray, delegated, under-five, underfunded, and reserve-money cases skip without consumption; corps money is shared across cities and training remains zero-cost.

The provider callback runs outside the run lock. A one-shot evaluation operation binds nonce, owner, run, ticket, cursor, and generation; any Stop, Dispose, recursive Evaluate/Commit, late return, or second-run race invalidates the old operation and cannot revive it. The build first runs the authoritative V8 `55/55`, pins the Core and V8 production hashes, then passes slice `46/46` plus an independent `20/20` repeat. SHA-256: library `F6253F961F4A4F5D5B20298987DBE511FDCB469827AC09C713DC7D24E320EB38`; SelfTest `5F1D370D779C67248F9D97FB32D5E40358374FC4A5C9E3EEA13E9C27B01525C9`. Independent review approves only this offline shadow scope; all public artifacts remain `ShadowOnly=true`, `LiveAuthorized=false`, with no IPC, process access, adapter, bridge, or native callback.

## P1 offline command broker

`prototypes\San9AutoDomestic.CommandBroker` freezes the fail-closed shell around one future command attempt. A current-user-only named mutex provides process-generation single flight; a protected directory holds a checksummed append-only session ledger and exclusive write-through journals. Durable replay identity is `(process session, request fingerprint, stage ordinal)`, so changing only `ConsentId` cannot replay a completed request. Journal corruption, nonterminal recovery, `AbortUncertain`, durable-write ambiguity, mutex release timeout/failure, or a required-restart scan permanently latches the host; busy/in-use and exact replay remain retryable non-execution results.

The frozen Release/x86 suite passes `57/57`; independent review repeated it five times (`285` executions) and found P1/P2 = 0. SHA-256: library `0C70EDFE0CE3417734DE684172D051A2A0EB639EE469A729C15130E216FC5419`; SelfTest `B69ABF9422AF769A41A245F462FBD829025D44F84074632A6CD3B344B7181926`. This is still an offline, in-memory-transport-only contract with no P/Invoke, process access, Bridge, native callback, or game execution. Live use additionally requires a stable native directory handle/file identity and an external monotonic trust anchor; managed path checks cannot defeat directory replacement or coordinated deletion/rollback by a principal that can rename the directory ancestry.

## Configuration boundary

`config\default.json` owns profile composition. Within the five supported commands, adding/removing/reordering profiles or tasks, enabling/disabling a task, changing the supported `1..5` officer range, exact-count behavior, and reserve money does not require a planner or UI code change. The UI generates every profile button from the frozen catalog and the projector iterates `plan.Tasks` in configuration order.

The exact-build adapter deliberately owns native facts: command IDs, stat mapping, fee rules, money-evidence semantics, order bits, and value gates. Adding a sixth native command such as recruitment is therefore an adapter/reverse-engineering change, not a safe JSON-only value change. The target-locked `San9Pk101CommandDescriptor` and every remaining command switch fail closed, so a forgotten mapping cannot silently inherit another command's ability, fee, money gate, or order bit.

Until a native ranking result is dynamically verified, a configured `native_best` request is shown as `requested=NativeBest, applied=StaticStatProxy`. `verified_stat_fallback` uses a separately resolved stable current-stat ranking. Both remain read-only and non-authorizing.

## Exact supported target

- Path: `D:\三国志9\10101749\San9PK.exe`
- Version: `1.0.1.0`
- Size: `2,636,800` bytes
- SHA-256: `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`
- PE machine: `0x014C` (`IMAGE_FILE_MACHINE_I386`)
- Window class: `KOEI_SAN9WINDOW`

Any mismatch is blocking. An identical file copied to a different path is also rejected.

## Build and test

Every C# project in the repository targets C# 5 / .NET Framework 4.8. `build.ps1` uses the system Framework MSBuild at `C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe`. A normal build requires the real .NET Framework 4.8 targeting pack under `Program Files (x86)\Reference Assemblies`; missing reference assemblies fail closed before any publish transaction begins. No NuGet package or `dotnet` SDK build is used.

The official .NET Framework 4.8 Developer Pack is installed on the current development machine. The formal transactional build passed on 2026-08-07 and published verified BuildId `45b855203b6e4af1897ec4317612812b`; the resident UI SHA-256 is `5CDFB6ED28656D4974C0E47F484418739C30BEE0909BF881BC8CE58F81245449`. For non-publishing local verification only, the explicit `-LocalRuntimeFallback` switch compiles against the CLR `v4.0.30319` runtime/GAC surface, runs every test, and optionally runs diagnostics:

```powershell
.\build.ps1 -LocalRuntimeFallback
.\build.ps1 -LocalRuntimeFallback -RunDiagnostics
```

A successful fallback is marked `LocalRuntimeVerified` and retained under `tools\artifacts\.local-verified-<buildId>`. It never acquires the publish lock, performs publish recovery, or replaces `tools\artifacts\bin`; its intermediate directory is always removed. `-LocalRuntimeFallback` cannot be combined with `-NoTests`, and fallback output is never release/publish evidence.

```powershell
Set-Location 'C:\codex files\San9AutoDomestic'
.\build.ps1
```

Run the live diagnostic report as part of the build:

```powershell
.\build.ps1 -RunDiagnostics
```

Normal builds hold a process-wide `FileShare.None` publish lock, use unique per-project intermediate directories, and run Core, persistent-UI state (`19/19`), V0 metadata, V1 read, V2 availability, V4 root-trace, and offline bridge-protocol synthetic self-tests before publication. The UI state-test EXE, Bridge protocol DLL, and Bridge self-test EXE are required staged outputs, so a green build cannot silently omit them. Publication uses a build-ID marker, a persistent journal, and `bin.previous`; startup recovers an interrupted rename before any new publish build begins. `-NoTests` retains output under `.unverified-*` and never changes `tools\artifacts\bin`.

Run the isolated publish-transaction fault simulations (they use and delete a unique test directory and do not touch the current `bin`):

```powershell
.\build.ps1 -TransactionSelfTest
```

The default executable test set verifies Core planning invariants, the persistent UI's waiting/fault/blocking/retry/log/control-disposal/single-instance/ThreadException states, target identity on disk, x86 execution, .NET Framework 4.8 metadata, rejection of a wrong path, the read-only public/native surface, synthetic V1 snapshot invariants, synthetic V2 availability, synthetic V4 root-lifecycle invariants, and the offline bridge protocol/state machine. It does not open the game process. Live conflict and process-read checks run only when `-RunDiagnostics` (or the V0 self-test's explicit `--live` / `--live-only`) is requested; the UI and Bridge self-tests have no live mode.

Run the live V0, V1, and V2 diagnostics together:

```powershell
.\build.ps1 -RunDiagnostics
```

## Read-only APIs

```csharp
var report = new San9Pk101Adapter().ReadSnapshot();
// report.Snapshot is present only after target validation, a unique process
// match, a successful read-only connection, stable double reads, and all V1
// structural invariants. The connection has already been released here.

var availability = new San9Pk101Adapter().ReadAvailability();
// availability.PlanningReady and availability.VerifiedNativeCapability are
// deliberately always false. Use San9Pk101UnverifiedPreviewProjector only for
// a non-authorizing display projection.
```

Version-specific code is isolated under `src\San9AutoDomestic.Adapter.San9Pk101`. Core and UI must not depend on fixed game paths or addresses.
