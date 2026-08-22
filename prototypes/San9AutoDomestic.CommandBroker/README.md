# San9AutoDomestic.CommandBroker

This is a deliberately isolated, offline-only P1 safety-shell prototype. It targets
.NET Framework 4.8, C# 5, and x86 without NuGet packages.

It contains:

- a bounded current-user-SID-only named-mutex lease whose release timeout remains observable and retriable;
- a protected, current-user-owned, reparse-free journal directory contract;
- a canonical append-only session ledger plus write-through, exclusive, checksummed execution journals;
- a bounded authenticated in-memory IPC codec;
- a process-session identity DTO that never opens a process; and
- a single-flight host with a cooperative stop boundary.

It does **not** reference the product UI, Core, V5.2, V8, V9, a game process, a
bridge, native callbacks, sockets, or named pipes. `BrokerSafetyPolicy.LiveAuthorized`
is permanently `false`.

Build and run the offline console self-test:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-and-test.ps1
```

The test executable uses random local mutex names and temporary directories whose
ACL is explicitly protected for the current SID. It tests ledger deletion,
corruption, journal deletion/rename/`.bak` escape attempts, ACL rejection, bounded
mutex timeouts, and strict journal transitions. It does not inspect or open any
external process.

Current verified result: `57/57` Release/x86 tests. See `VERIFICATION.md` for the
frozen hashes and commands.
