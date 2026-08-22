# Offline safety contract

## Authority boundary

The assembly is not an execution authority. It cannot authorize or invoke live,
bridge, process, or native behavior. The IPC implementation is an in-memory test
transport only. No permit or callback boundary exists here.

## Single flight

For a process-session identity, a current-user-only named mutex is held by a
dedicated owner thread for the complete journaled operation. A second broker cannot
enter. Readiness and release waits are bounded. An abandoned mutex or timeout is
fail-closed as `RestartRequired`. A release timeout keeps ownership explicitly
unknown, keeps the operation attached to its host, and can only be cleared by an
explicit bounded release retry; it never masquerades as a successful `Dispose`.
`ReleaseMutex` failure is distinct from timeout and is also observable and
restart-latching.
If an explicit retry after a timeout completes as failed, the closed operation is
detached from the host while both restart latches remain permanent; it cannot leave
an unretryable active-operation tombstone behind.

## Controlled directory

The canonical ledger and journals may only live in an existing absolute directory
owned by the current Windows SID. Its DACL must be protected and contain explicit
rules for that SID only, including FullControl. Reparse points are rejected for the
directory and every ancestor, and session artifacts are rejected if they are
reparse points. The FullControl ACE must propagate to child files and directories
without propagation suppression. Every `TryStart` revalidates the directory, and
every created or scanned ledger/journal is revalidated for current-user ownership
and a current-SID-only ACL.

## Journal

Each process session has exactly one binding-derived canonical append-only ledger.
Every operation is registered there with its complete `ExecutionJournalBinding`
and canonical journal filename. Startup is ledger-driven: every registered journal
must exist at that exact path, bind to the exact entry, and be structurally valid.
Missing/corrupt ledgers, missing/corrupt/nonterminal journals, renamed journals,
and unregistered prefix artifacts (including `.bak`) fail closed. If the ledger is
missing while a session-prefix artifact exists, startup also fails closed.

Durable replay identity is `(process session, request fingerprint, stage ordinal)`.
`ConsentId` remains part of the persisted authorization binding and canonical path,
but changing only consent cannot replay an already terminal request. A genuinely
fresh upstream generation/sequence must therefore change the exact request
fingerprint.

The journal uses `FileShare.None`, `FileOptions.WriteThrough`, and `Flush(true)`.
Every fixed-size record carries the full process/request/stage/consent binding,
strict sequence, and SHA-256 checksum. The only normal path is:

`ConsentRecorded -> Prepared -> IntentIssued -> SideEffectEntered -> Receipted -> TerminalVerified`

`AbortUncertain` is a terminal fail-closed branch from a nonterminal state. Existing
terminal journals reject replay. Existing nonterminal, partial, corrupt, or binding-
mismatched journals require restart and are never resumed or overwritten.

Simultaneous deletion or rollback of both the canonical ledger and every journal
for the same SID/process-session is explicitly outside this offline prototype's
threat model. Closing that gap requires an external monotonic trust anchor; it is
not claimed here.

Any principal with rename or `DELETE_CHILD` rights on a writable ancestor can also
swap the directory between managed validation and a later file operation. Per-start
validation catches persistent ACL/reparse drift, but strong directory-object
identity binding would require a native handle/file-id boundary that this
no-P/Invoke prototype does not possess. That TOCTOU and writable-ancestor attack
remains explicitly out of scope.

## Permanent restart latch

`RestartRequired`, journal corruption/failure, operation journal-write failure,
`AbortUncertain`, stop-before-side-effect abort, nonterminal disposal, and mutex
release timeout/failure permanently latch the current host. Repairing or restoring
files cannot make that host execute again. Retryable `Busy`, `JournalInUse`, exact
replay rejection, and caller binding mismatch do not set this latch.

## Stop boundary

A stop request prevents a new operation. Before `SideEffectEntered`, it terminates
the current journal as `AbortUncertain` without entering the boundary. Once the
boundary has been journaled, stop is deferred: the caller must record a receipt and
terminal verification, or explicitly latch `AbortUncertain`. No force-cancel or
automatic retry exists.

## IPC

Frames are length-prefixed and bounded. The authenticated body binds fixed magic,
schema, strict sequence, message type, a 32-byte session nonce, and payload. HMAC-
SHA-256 is verified before semantic errors are exposed. A receiver advances its
sequence only after the complete frame has authenticated and validated.
