# San9BridgeP1Wire

Pure C11, offline-only implementation of the canonical 512-byte P1 ping wire. This is
an executable contract harness, not a DLL or loader. It contains no Windows headers,
process access, P/Invoke, IPC, shared-memory, input, injection, or game interaction.
`SAN9_P1_LIVE_AUTHORIZATION` is fixed to `0`.

`build.ps1` pins Zig 0.16.0 and Python 3.13.0 by version/runtime hashes, compiles
PE32/x86 with `-Wall -Wextra -Werror`, runs 1,700+ offline contract/mutation/replay
checks, and applies an exact PE import allowlist. The `pefile` source and the audit
script are hash-frozen.

The executable has no exports, delay imports, or RWX sections. Zig/MinGW's CRT TLS
scaffold is explicitly bounded: exactly one four-byte TLS cell and exactly two callbacks
at frozen RVAs `0x7590` and `0x7610`; their table must be read-only, their code executable
and non-writable, and every pointer/range must remain inside the image. Missing, extra,
drifted, unterminated, writable, or malformed callbacks fail the audit. User-defined C
TLS keywords are rejected at the source gate.

The combined C#/C/independent-Python request-and-response proof is driven by
`prototypes/San9AutoDomestic.P1Wire/build-and-test.ps1`.
