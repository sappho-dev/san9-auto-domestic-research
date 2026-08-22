# Offline bridge protocol skeleton

This project is a reusable, completely offline protocol model. It does not discover,
open, read, write, inject into, or send input to any process. It does not authorize an
operation. The only bundled transport is a bounded in-memory fake that clones frames.

## Fixed wire frame (schema 1.0)

Every frame is exactly 288 bytes and uses explicit little-endian offsets. There are no
native pointers, variable-length fields, CLR layout dependencies, or opaque executable
command bytes.

| Offset | Width | Field |
|---:|---:|---|
| 0 | 4 | magic `S9BP` |
| 4 | 2 + 2 | schema major/minor |
| 8 | 4 | declared frame size |
| 12 | 2 + 2 | frame kind (`Request`, `Claim`, `Result`) and request state |
| 16 | 4 | CRC32 over all 288 bytes with this field zeroed |
| 20 | 4 + 8 | zero flags and reserved header |
| 32 | 16 | session nonce |
| 48 | 8 | unsigned sequence |
| 56 | 16 | request identifier |
| 72 | 32 | exact-target identity SHA-256 summary |
| 104 | 32 | context-token SHA-256 summary |
| 136 | 8 + 8 | creation and expiry UTC ticks |
| 152 | 4 + 4 | opaque nonzero operation code and result code |
| 160 | 32 | request-payload SHA-256 summary |
| 192 | 32 | result-payload SHA-256 summary, zero on requests |
| 224 | 32 | SHA-256 binding of the canonical request frame, zero on requests |
| 256 | 32 | zero reserved tail |

Decoding rejects a wrong magic, schema, size, checksum, reserved byte, enum, zero
binding, or invalid request/result field combination. CRC32 catches accidental frame
damage; SHA-256 summaries provide identity and request/result binding.

## State machine

The sole accepted transition path is:

`Pending -> Claimed -> Completed | Rejected`

All three stages have fixed wire representations. A Claim frame contains the SHA-256
binding of the canonical Pending request, and a Result contains that same binding plus
its own result-payload summary.

`ExactTargetIdentity` builds its digest from a canonical executable path, executable
SHA-256, file size, PE image base/size, and four-part file version. The dynamic context
token remains a separate caller-supplied canonical SHA-256 summary, so a new process or
snapshot context can invalidate an otherwise identical static target.

The state machine accepts exactly the next sequence number, remembers every accepted
request identifier for the session, allows one unresolved request, bounds lifetime to
one minute, and rechecks expiry before claim and completion. Cross-session, wrong-target,
wrong-context, duplicate, replayed, skipped, future, and expired requests are rejected.
The clock is observed monotonically within a session; a backwards clock step closes the
current operation path until time catches up instead of extending a request lifetime.

Aborting a session internally claims any pending request and emits a request-bound
Rejected result before closing the session. A pending timeout can be closed the same way
with `TryRejectExpired`. A completion that races expiry returns `false` and returns a
bound Rejected frame, never a Completed frame.

This is a protocol/state-machine foundation only. `IsExecutionAuthorized` is hardcoded
to `false` throughout, and no game-side bridge or execution adapter is supplied.

CRC32 and unkeyed SHA-256 do not authenticate a hostile peer. A future production
bridge would still need an authenticated channel, a bounded session/request budget,
and durable terminal acknowledgement/retry semantics before any execution capability
could be considered. The in-memory synthetic host intentionally provides none of those
and must not be promoted into an execution bridge.
