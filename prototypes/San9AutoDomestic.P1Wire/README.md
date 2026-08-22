# P1Wire offline contract prototype

This directory is an isolated, non-product prototype for the one allowed P1 message: a
512-byte ping request/response. It targets .NET Framework 4.8, x86, and C# 5. It has no
P/Invoke, IPC, process access, UI reference, Adapter reference, or root-build connection.
`P1WireContract.LiveAuthorization` is permanently `false` in this prototype.

The independent C implementation and PE audit live in
`native/San9BridgeP1Wire`. Run the complete offline proof with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\prototypes\San9AutoDomestic.P1Wire\build-and-test.ps1
```

## Canonical little-endian layout

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | magic `S9P1` (`0x31503953`) |
| 4 | 2 | schema major (`1`) |
| 6 | 2 | schema minor (`0`) |
| 8 | 4 | declared size (`512`) |
| 12 | 2 | kind: request `1`, response `2` |
| 14 | 2 | state: pending `1`, completed `2`, rejected `3` |
| 16 | 4 | flags, fixed zero |
| 20 | 4 | CRC-32/IEEE |
| 24 | 4 | ping result code |
| 28 | 4 | reserved, fixed zero |
| 32 | 8 | sequence |
| 40 | 8 | issued-at milliseconds |
| 48 | 8 | expires-at milliseconds |
| 56 | 4 | game PID |
| 60 | 4 | game main-thread TID |
| 64 | 4 | x86 game HWND value |
| 68 | 4 | helper PID |
| 72 | 4 | Easy loader PID |
| 76 | 4 | reserved, fixed zero |
| 80 | 8 | game creation-time generation |
| 88 | 8 | helper creation-time generation |
| 96 | 8 | Easy loader creation-time generation |
| 104 | 16 | session nonce |
| 120 | 16 | request ID / one-shot nonce |
| 136 | 16 | Easy epoch nonce |
| 152 | 32 | build digest |
| 184 | 32 | profile digest |
| 216 | 32 | manifest digest |
| 248 | 32 | Easy epoch digest |
| 280 | 32 | Easy ticket digest |
| 312 | 32 | context digest |
| 344 | 32 | bridge digest |
| 376 | 32 | mapping digest |
| 408 | 32 | challenge digest |
| 440 | 32 | result digest (all zero only in requests) |
| 472 | 32 | HMAC-SHA-256 |
| 504 | 8 | reserved, fixed zero |

For HMAC, both the CRC field and HMAC field are zeroed and all 512 bytes are covered.
After the HMAC is written, CRC is computed across all 512 bytes with only its own four
bytes zeroed. Decoders require exactly 512 bytes, verify HMAC in constant time, reject
all nonzero reserved bytes, and validate every ping/replay/binding field. Each session
gate remembers the last trusted `now` value; a rollback permanently faults that gate,
and only constructing a new session gate can clear the fault.

The frozen deterministic vectors are:

- request SHA-256: `acadf1f1c5cf70672484632e32865871058e3193b84ffc5c233990985fbe80d5`;
- completed-response SHA-256: `2dcef30e7c5b5219f255d97c7a260019748551d2279c868e62539ce204a0e87e`.

The pipeline makes C#, C, and a third implementation in
`oracle/p1_wire_oracle.py` construct both vectors independently. The Python oracle is
stdlib-only and does not import, parse, invoke, or generate from either implementation.
All three encoders must be byte-identical and all three authenticated decoders must
accept the other implementations' request and response. Mutation, truncation, overlong,
CRC-repaired, replay, and binding tests remain fail-closed.

The build gate pins the exact Python executable/runtime and oracle SHA-256, the exact
.NET Framework MSBuild/C# compiler/targets, and the five net48 reference-pack inputs
used by these projects. `FrameworkPathOverride` is forced to that authenticated v4.8
reference pack so a different installed framework cannot be selected implicitly.
