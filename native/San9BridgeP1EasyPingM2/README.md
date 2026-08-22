# San9BridgeP1EasyPingM2 — M2a offline model only

This sibling is the compile-and-test-only M2a model. It contains no Windows
headers, process access, hook, mapping, ACL, IPC, target write, helper, DLL, or
live authorization path. All modeled system actions are fake callbacks.

The fixed 4096-byte mailbox keeps the canonical frozen wire frames at offsets
512 and 1024. Reserved ranges remain zero. Request acceptance only calls the
frozen `san9_p1_decode_and_accept`; response consumption only calls the frozen
`san9_p1_decode_and_verify_response`. There is no public decoded-frame accept
entry point and no copied wire codec.

Lifecycle state is advanced only by C11 atomic expected-to-desired CAS. A
precommit cancellation may win through `VALIDATING`; after `COMMITTING`, only
the owner can finish and every failure becomes monotonic `POISONED_RESTART`.
The owner token is stored only as a domain-separated digest. A separate
lock-free processing-owner CAS serializes publish, target processing,
controller completion/discard, and postcommit poison so the private pending
request and expected result digest have one owner at a time.
The single request/response slot permits one outstanding ping. Sequence,
request-ID replay, lifetime, clock rollback, and the 100-ping ceiling are
enforced by the frozen wire gate.

The controller alone advances `controller_ack`, exactly one step when a round
is consumed or discard-acknowledged. Slot reuse is published only after that
ack and complete clearing. Stale completion, discard, or round tokens cannot
modify the next round.

The result digest contract is:

```text
SHA256(
  ASCII("SAN9-P1-RESULT-v1") ||
  request_id[16] || sequenceLE64 || challenge_digest[32] ||
  main_tidLE32 || callerLE32 || easy_snapshot_digest[32] || ordinalLE32
)
```

M2a links the frozen P1Wire sources and frozen M1 Easy verifier source. Its
build regenerates only M1's ignored private manifest header from authenticated
evidence. Completion of M2a does not authorize ACL, helper, injection, DLL,
hook, mailbox sharing, or a live ping.

Run the fail-closed pipeline with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\San9BridgeP1EasyPingM2\build.ps1
```

The pipeline authenticates the linked frozen sources and tools, builds two
byte-identical stripped x86 executables, enforces the frozen whole-image hash,
audits the PE before execution (including five malicious fixtures), runs the
offline state-machine matrix, checks that the image hash did not change, and
repeats the PE audit.
