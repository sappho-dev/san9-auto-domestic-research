# San9Bridge V5.2 ping-only live-bootstrap prototype

This directory contains two separately compiled x86 profiles:

- the default profile (`SAN9_V52_LIVE_ENABLED=0`) is an offline-only synthetic
  harness and cannot install a hook or inspect a process;
- the explicit opt-in profile contains a compile-only `WH_GETMESSAGE`
  bootstrap prototype.  Its presence is not authorization to run it.

Default offline build and verification:

```powershell
tools\San9BridgeV52\build.ps1
```

Compile and statically audit the opt-in profile without running or loading it:

```powershell
tools\San9BridgeV52\build-live-optin.ps1 `
  -ConfirmCompileOnly I_ACCEPT_COMPILE_ONLY_DO_NOT_RUN_LIVE_ARTIFACT `
  -ConfirmDynamicNoGo I_ACCEPT_DYNAMIC_LIVE_REMAINS_NO_GO
```

Both scripts lock Zig to `0.16.0` and the expected `zig.exe` SHA-256.  The
audit requires Python `>=3.11`, exactly `pefile==2024.8.26`, and exactly
`capstone==5.0.7`.  Outputs and the generated per-build root key are written
only under ignored `tools/artifacts/San9BridgeV52/`; the native source tree
contains no generated binaries or key.

The live-optin prototype is ping-only.  It has no domestic-command opcode,
city/person fields, business function address, slot restore, or hot-unload
path.  The DLL is pinned until target process exit.  Its real idle wrapper
calls `0x00434100` exactly once on every normal entry before any post helper,
preserves the original return value and nonvolatile registers, and permits at
most one authenticated V5.1 ping at outer TLS depth one after every exact gate
passes.  The controller deletes the bootstrap atom and releases its own
mapping/view/hook resources, but it never calls `FreeLibrary` or restores the
game slot.

The controller/DLL use a pre-commit race boundary.  `SEALED`, `CLAIMED`, and
`INSTALLING` may be cancelled only by first winning a CAS to `STOPPED`; claim
is disabled afterwards.  The DLL performs every reversible preparation,
including a second full target probe, session creation, and final code/slot
anchors.  After those anchors it takes a fresh monotonic timestamp, fully
revalidates the authenticated envelope, and immediately competes for CAS
`INSTALLING -> COMMITTING`.  Only that COMMITTING owner may pin the DLL or CAS
the idle slot.  The controller's cancellation deadline is the authenticated
envelope expiry, not a new post-message timer.  A COMMITTING watchdog never reports
"cancelled": it reports `INDETERMINATE/RESTART_REQUIRED` without changing the
shared state or claim.  Rejection similarly acquires a `REJECTING` ownership
state before publishing its result, so it cannot overwrite `STOPPED` or
`READY`.

Lifecycle decisions are shared production code in `include/san9_v52_lifecycle.h`
and `src/lifecycle.c`; the offline suite calls those exact helpers.  A READY
ping response is consumed only after atomic observations also show request
COMPLETE and ping count exactly one.  Two consecutive unhook failures, a
post-pin slot failure, or any post-READY gate/ping failure require a target
restart.  Two consecutive atom-delete failures are reported separately as
`CLEANUP_INCOMPLETE`.

Random names, a registered message, an atom, and the embedded build key only
reduce accidental collision under a non-adversarial same-user model.  They do
not resist a malicious process running as the same user; the key can be
extracted from the artifacts.  `CreateFileMappingW` deliberately uses NULL
security attributes and therefore the token's default DACL; that default DACL
does not strengthen this same-user threat model.

The opt-in controller is a diagnostic console prototype, not the product's
main UI.  Do not double-click it.  Its exact CLI confirmations are only an
accidental-launch barrier and are not authorization to use it against a game.

Do not copy either DLL to the game directory.  This project does not modify
the EXE and does not use `version.dll` or any other proxy side-load.  Dynamic
live use and all business execution remain **NO-GO**.
