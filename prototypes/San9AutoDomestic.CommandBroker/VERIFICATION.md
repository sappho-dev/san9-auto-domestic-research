# Offline verification

Run from this directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-and-test.ps1
```

Frozen result (2026-08-07):

- configuration: Release, x86, .NET Framework 4.8, warnings as errors;
- self-tests: `57/57`;
- library SHA-256: `0C70EDFE0CE3417734DE684172D051A2A0EB639EE469A729C15130E216FC5419`;
- self-test SHA-256: `B69ABF9422AF769A41A245F462FBD829025D44F84074632A6CD3B344B7181926`;
- `live_authorized=false`;
- no process access, P/Invoke, native callback, socket, named pipe, or game access;
- transport remains bounded and in-memory only.

The negative suite covers protected inheritable current-SID-only ACL enforcement,
per-start directory revalidation, created-file owner/ACL checks, mutex ready/release
timeouts and release failure, retained unknown ownership, ledger capacity,
corruption/deletion, registered journal deletion, journal rename outside the
historical glob, unregistered `.bak` artifacts, nonterminal/partial/corrupt
journals, permanent same-host restart latching, non-latching retryable outcomes,
forced durable-write failure, replay, single-flight, stop boundaries, and
authenticated IPC framing.
The release matrix also covers timeout followed by a failed bounded retry: the
operation detaches, ownership is no longer reported unknown, and restart remains
latched.
Replay coverage includes changing only `ConsentId` while preserving request
fingerprint and stage ordinal; this remains rejected without latching restart.

This is an offline safety contract, not execution authority. Simultaneous rollback
or deletion of both ledger and all same-session journals is out of scope.
Directory-object swap TOCTOU by any principal with ancestor rename or `DELETE_CHILD`
rights is also out of scope in this no-P/Invoke prototype.
