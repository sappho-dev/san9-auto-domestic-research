# San9BridgeV10 offline native adapter contract

This directory freezes the exact-version command and UI-stage mappings needed
by a future native adapter.  It is intentionally not an executor.

- exact target: San9PK 1.0.1.0, SHA-256
  `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`;
- five commands and exactly-five request validation mirror the audited V8
  transaction contract;
- request fingerprints are recomputed with V8's exact `BinaryWriter` field
  order, little-endian integers, 7-bit ASCII string lengths, and .NET
  `Guid.ToByteArray()` request-id byte order; the cross-language golden vector
  is `DFD94456128CAAB757A25389725184C5250B230A4BC98E82D7D784789093E0B7`;
- the x86 ABI is frozen at a 176-byte request (`sequence=24`,
  `officer_ids=48`, `context_digest=76`, `request_fingerprint=140`). This is
  not an IPC wire format and must never be copied raw across a trust boundary;
- seven mutating stages map to the V7/V9 static addresses and event IDs;
- target binding, nested-modal lifetime, and native cleanup remain explicitly
  unproven and unauthorized;
- this profile contains no process access, write, injection, hook, input, or
  callable game-function implementation.

Build and run the bounded offline tests with:

```powershell
.\tools\San9BridgeV10\build.ps1
```

The generated DLL and self-test executable are ignored development artifacts.
Do not copy the DLL into the game directory.  It is not connected to the root
build, the persistent UI, V5.2, or any running game.
