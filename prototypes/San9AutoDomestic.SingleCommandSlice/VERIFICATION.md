# Verification report — offline shadow single-command slice

Date: 2026-08-07
Scope: `prototypes/San9AutoDomestic.SingleCommandSlice` only
Verdict: **OFFLINE SHADOW-ONLY GO; LIVE/PROCESS/IPC/NATIVE CALLBACK NO-GO**

## Authoritative command

```powershell
cd 'C:\codex files\San9AutoDomestic\prototypes\San9AutoDomestic.SingleCommandSlice'
.\build-and-test.ps1
```

The command completed with exit code `0`: authoritative V8 `55/55`, pinned
Core/V8 hashes, warnings-as-errors Release builds, source forbidden-pattern
audit, production-assembly reflection audit, and slice `46/46`.

## Coverage

The 46 slice tests prove:

- permanent `ShadowOnly=true` and `LiveAuthorized=false` flags;
- Basic order (`Commerce`, `Cultivate`) and Wealthy order (`Patrol`,
  `Commerce`, `Cultivate`, `Train`, `Repair`);
- all five explicit Core-to-V8 descriptor mappings, native command IDs, costs,
  exact-five officer binding, request identity, digests, and stable sequences;
- ascending city IDs, configuration task order, and disabled-task removal;
- structured grey, delegated, foreign, under-five, insufficient-funds,
  reserve-protected, and other-native-block skips;
- skip continuation without officer/money consumption;
- commit as the sole consumption boundary and shared corps funds across cities;
- a prior commit exhausting five officers makes the later task skip without a
  second charge;
- an officer cannot be seeded as available in two different cities;
- one-use tickets, cross-run rejection, out-of-order fail-close, generation and
  observation mismatch fail-close, and provider-exception fail-close;
- ordinal mismatch fail-closes even while work is outstanding, while the same
  ordinal returns the narrower outstanding-work result;
- the fake provider executes outside the run monitor and Stop, Dispose,
  recursive Evaluate, and Commit re-entry cannot resurrect an invalidated run;
- a second run may acquire the released slot during the callback without the
  returning first run corrupting it;
- nontrivial candidate ordering chooses the exact first five, previously
  consumed IDs are filtered while fresh order is retained, incomplete lists
  skip, and duplicate IDs are rejected;
- stop before issue, with an outstanding ticket, and after validation;
- sequential and 16-way concurrent process/AppDomain single-flight;
- no public ticket/validated/receipt constructors, no P/Invoke, and an exact
  assembly-reference allowlist containing only Core, V8, `mscorlib`, and
  `System.Core`.

## Release artifact hashes

| Artifact | SHA-256 |
|---|---|
| `San9AutoDomestic.SingleCommandSlice.dll` | `F6253F961F4A4F5D5B20298987DBE511FDCB469827AC09C713DC7D24E320EB38` |
| `San9AutoDomestic.SingleCommandSlice.SelfTest.exe` | `5F1D370D779C67248F9D97FB32D5E40358374FC4A5C9E3EEA13E9C27B01525C9` |
| referenced `San9AutoDomestic.Core.dll` | `64BFFA87A094834ED4440822B16AB6AE3401C66965374E44FD1BD5635C8FA6CF` |
| referenced `San9AutoDomestic.V8Transaction.dll` | `06690240E44448748F3B2A1A7CAA2F9073253B0A32C3AE328DC1DE8B5B47DEEF` |

These hashes identify this offline verification build only.  They confer no
authority to load code into, communicate with, or mutate San9PK.

## Explicit exclusions

No game was launched, discovered, opened, read, hooked, injected, or written.
No IPC endpoint or native callback exists.  No Core trusted capability was
forged via reflection or `InternalsVisibleTo`.  The root build, Core, UI,
adapter, V5.2, and root documentation were not modified by this prototype.
