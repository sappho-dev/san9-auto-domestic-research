# San9Bridge V5.1 native proof

This directory contains an **offline-only x86 ping-bootstrap proof**. It is not
a game injector, live bridge, domestic-command protocol, or deployable build.

Build and verify from the repository root:

```powershell
python -m pip install pefile==2024.8.26
tools\San9BridgeV51\build.ps1
```

The script accepts only Zig `0.16.0` with the pinned official `zig.exe` SHA-256,
requires Python `>=3.11.0` plus exactly `pefile==2024.8.26`, and runs synthetic
negative version-gate checks before compiling. It then uses
`cc -target x86-windows-gnu`, runs the x86 controller only with `--self-test`,
and performs an on-disk PE audit. It never opens or finds a game process and it
does not install a Windows Hook. Build outputs are written
under `tools/artifacts/San9BridgeV51/`, which is ignored by Git and kept outside
this source directory.

Safety properties intentionally frozen in this proof:

- `San9Bridge_Bootstrap` always returns `OFFLINE_ONLY`;
- the exported `San9Bridge_IdleBridge` is unarmed and never jumps to the fixed
  game idle address;
- offline idle behavior is tested only with an injected synthetic original
  thunk, a TLS reentry guard, and one authenticated ping at the outer depth;
- the DLL pins itself during its offline self-test and there is no hot-unload
  path;
- the fixed `256-byte` HMAC frame has no command/opcode/business payload;
- unauthenticated/bad frames enter an explicit ACK-reset terminal, so recovery
  is bounded without returning an unauthenticated response;
- a sampled clock decrease faults the session; recovery requires an atomic
  mailbox reset and a different session nonce. All timestamps must still come
  from one trusted monotonic boot-domain because sampled rollback detection is
  not a replacement for a monotonic clock;
- this wire format is explicitly incompatible with the C# `288-byte`
  `San9AutoDomestic.Bridge.Protocol` schema.

The project `DllMain` is deliberately minimal, but the Zig/MinGW PE entry still
runs CRT/TLS machinery and currently contains two CRT TLS callbacks. Therefore
this offline result does not prove loader-lock behavior for a future live Hook.

The source tree consists only of the `.def`, shared header, C sources, and this
README. Do not copy the generated DLL into the game directory and do not add it
to the repository root build.
