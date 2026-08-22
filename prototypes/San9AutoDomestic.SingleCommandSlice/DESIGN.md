# Offline shadow vertical-slice design

## Frozen input and ordering

`ExecutionPlanCompiler` validates and freezes the selected Core profile.  The
slice additionally requires every enabled task to be `NativeBest`, exact-count,
and `min=max=5`.  Fake city seeds are copied and sorted by game city ID; tasks
retain their enabled configuration order.  The absolute sequence is:

`cityOrdinal * taskCount + taskOrdinal + 1`

Skipped pairs retain their ordinal, so later sequences are never compressed or
renumbered.

## Cursor and ticket

Only the current pair can issue a `SingleCommandSliceTicket`.  Asking for any
later city/task ordinal halts the run, proving that a caller cannot jump a
higher-priority task.  One ticket may be evaluated once.  A duplicate,
cross-run, stale, or non-current ticket fail-closes the run and releases the
single-flight slot.  Ordinal mismatch is checked before the generic
outstanding-work result whenever an active cursor exists.

## Fresh structured validation

For a directly controlled, player-owned city, evaluation requests one fresh
synthetic observation and checks:

- exact generation digest;
- city, corps, and command identity;
- current player ownership and direct control;
- native availability / explicit block reason;
- every ranked ID belongs to the frozen city seed;
- exactly five highest-ranked, not-yet-consumed officers remain;
- the V8 descriptor's frozen cost and the task's reserve-money boundary.

The fake provider callback runs outside the run monitor.  Before calling it the
run installs an `Evaluating` operation containing a fresh nonce plus exact
owner, ticket, run, cursor, and generation bindings.  After return, every field
and the pending-operation reference are rechecked under the monitor.  A callback
that stops, disposes, recursively evaluates, attempts commit, or otherwise
invalidates the operation cannot resurrect the old run or disturb a new run
that acquired the released single-flight slot.

Candidate order is authoritative only within the synthetic observation: the
validator takes the first five currently unconsumed IDs in that order.  IDs
already consumed by an earlier shadow commit are filtered while the remaining
order is preserved.  Fewer than five is a structured skip; duplicate IDs are
rejected by the observation model.

Greyed out, delegated, foreign, under-five, underfunded, and reserve-protected
pairs produce structured skip results and advance one ordinal.  A skip never
changes either resource ledger.

## Commit and shared resources

A successful evaluation creates exactly one `ValidatedSingleCommand` and one V8
`SingleCommandRequest`.  Until `TryCommit` receives that exact object, no resource
changes.  Shadow commit atomically removes its five officers from that city's
ledger and subtracts the descriptor cost from the shared corps ledger.  Cities
in the same corps therefore observe sequentially depleted money.  Training is
the registry's zero-cost command and does not cross a reserve boundary.

## Stop and single-flight boundaries

The process/AppDomain has one static shadow-run slot.  A second sequential or
concurrent start loses while a run is active.  Complete, halt, explicit stop, or
dispose releases the slot exactly once.  Stopping with an outstanding ticket or
validated-but-uncommitted command discards it without resource consumption.

## Non-authority proof

The assembly contains no P/Invoke, process access, IPC, adapter/bridge reference,
or native callback.  Ticket and validated-command constructors are non-public.
The provider interface is explicitly synthetic and carries no Core trusted
capability.  All public state/request/result/receipt types have permanent shadow
and live-authorization flags; those flags are not constructor parameters or
mutable fields.
