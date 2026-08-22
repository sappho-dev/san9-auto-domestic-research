using System;
using System.Collections.Generic;
using System.Threading;

namespace San9AutoDomestic.V8Transaction
{
    public sealed class ProcessGenerationKey
    {
        public ProcessGenerationKey(
            int processId,
            long processCreationUtcTicks,
            ulong generationNumber,
            FixedDigest generationDigest)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }

            if (processCreationUtcTicks <= 0 || processCreationUtcTicks > DateTime.MaxValue.Ticks)
            {
                throw new ArgumentOutOfRangeException("processCreationUtcTicks");
            }

            if (generationNumber == 0)
            {
                throw new ArgumentOutOfRangeException("generationNumber");
            }

            if (generationDigest == null)
            {
                throw new ArgumentNullException("generationDigest");
            }

            ProcessId = processId;
            ProcessCreationUtcTicks = processCreationUtcTicks;
            GenerationNumber = generationNumber;
            GenerationDigest = generationDigest;
        }

        public int ProcessId { get; private set; }
        public long ProcessCreationUtcTicks { get; private set; }
        public ulong GenerationNumber { get; private set; }
        public FixedDigest GenerationDigest { get; private set; }
    }

    internal enum CoordinatorStartRejection
    {
        None = 0,
        ActiveTransactionExists = 1,
        FaultLatched = 2,
        ReplayedRequest = 3,
        NonMonotonicSequence = 4,
        RequestExpired = 5,
        RequestNotYetValid = 6,
        GenerationBindingMismatch = 7,
        StaleCoordinator = 8,
        GenerationAdvanceRequired = 9,
        TrustedClockFailure = 10,
        SessionCapacityReached = 11
    }

    internal sealed class TransactionHandle
    {
        internal TransactionHandle(Guid coordinatorSessionId, SingleCommandRequest request)
        {
            CoordinatorSessionId = coordinatorSessionId;
            RequestId = request.RequestId;
            Sequence = request.Sequence;
            RequestFingerprint = request.RequestFingerprint;
        }

        internal Guid CoordinatorSessionId { get; private set; }
        internal Guid RequestId { get; private set; }
        internal ulong Sequence { get; private set; }
        internal FixedDigest RequestFingerprint { get; private set; }
    }

    internal sealed class CoordinatorStartResult
    {
        internal CoordinatorStartResult(
            bool accepted,
            CoordinatorStartRejection rejection,
            TransactionHandle handle,
            string detail)
        {
            Accepted = accepted;
            Rejection = rejection;
            Handle = handle;
            Detail = detail ?? string.Empty;
        }

        internal bool Accepted { get; private set; }
        internal CoordinatorStartRejection Rejection { get; private set; }
        internal TransactionHandle Handle { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class ActionIssueResult
    {
        internal ActionIssueResult(
            bool issued,
            OneShotActionIntent intent,
            TransactionTransition transition,
            string detail)
        {
            Issued = issued;
            Intent = intent;
            Transition = transition;
            Detail = detail ?? string.Empty;
        }

        internal bool Issued { get; private set; }
        internal OneShotActionIntent Intent { get; private set; }
        internal TransactionTransition Transition { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class FaultLatchSnapshot
    {
        internal FaultLatchSnapshot(SessionState state)
        {
            IsLatched = state.FaultLatched;
            FaultId = state.FaultId;
            IsAcknowledged = state.FaultAcknowledged;
            CleanupCompleted = state.CleanupCompleted;
            CleanupFailed = state.CleanupFailed;
            CleanupDirective = state.CleanupDirective;
        }

        internal bool IsLatched { get; private set; }
        internal Guid FaultId { get; private set; }
        internal bool IsAcknowledged { get; private set; }
        internal bool CleanupCompleted { get; private set; }
        internal bool CleanupFailed { get; private set; }
        internal AbortCleanupDirective CleanupDirective { get; private set; }
    }

    internal sealed class ProcessGenerationCoordinator
    {
        internal const string RequiredManualAcknowledgement = "ACKNOWLEDGE_ABORT_UNCERTAIN";
        internal const int CleanupTimeToLiveMilliseconds = 5000;
        internal const int MaximumTrackedProcesses = 256;

        private static readonly object GlobalOperationGate = new object();
        private static readonly object GlobalSync = new object();
        private static readonly Dictionary<int, SessionState> Sessions =
            new Dictionary<int, SessionState>();

        [ThreadStatic]
        private static bool _insideCoordinatorOperation;

        private readonly ProcessGenerationKey _key;
        private readonly SessionState _session;
        private readonly TrustedObservationFactory _observationFactory;

        private ProcessGenerationCoordinator(ProcessGenerationKey key, SessionState session)
        {
            _key = key;
            _session = session;
            _observationFactory = session.ObservationFactory;
        }

        internal ProcessGenerationKey Key
        {
            get { return _key; }
        }

        internal TrustedObservationFactory ObservationFactory
        {
            get
            {
                using (EnterOperation())
                {
                    lock (GlobalSync)
                    {
                        if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None
                            || !_observationFactory.IsActive)
                        {
                            throw new InvalidOperationException("This coordinator factory belongs to a retired session.");
                        }

                        return _observationFactory;
                    }
                }
            }
        }

        internal static ProcessGenerationCoordinator Attach(ProcessGenerationKey key)
        {
            return AttachCore(key, SystemTrustedMonotonicClock.Instance);
        }

#if V8_SELF_TEST
        internal static ProcessGenerationCoordinator AttachForTesting(
            ProcessGenerationKey key,
            ITrustedMonotonicClock clock)
        {
            return AttachCore(key, clock);
        }

        internal static int ActiveSessionCountForTesting
        {
            get
            {
                using (EnterOperation())
                {
                    lock (GlobalSync)
                    {
                        return Sessions.Count;
                    }
                }
            }
        }
#endif

        private static ProcessGenerationCoordinator AttachCore(
            ProcessGenerationKey key,
            ITrustedMonotonicClock clock)
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }

            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }

            using (EnterOperation())
            {
                lock (GlobalSync)
                {
                    SessionState state;
                    if (!Sessions.TryGetValue(key.ProcessId, out state))
                    {
                        if (Sessions.Count >= MaximumTrackedProcesses)
                        {
                            throw new InvalidOperationException(
                                "The bounded process-session table is full; retire an exited process first.");
                        }

                        state = new SessionState(key, clock);
                        Sessions.Add(key.ProcessId, state);
                    }
                    else if (key.ProcessCreationUtcTicks != state.Key.ProcessCreationUtcTicks)
                    {
                        if (key.ProcessCreationUtcTicks <= state.Key.ProcessCreationUtcTicks)
                        {
                            throw new InvalidOperationException("A stale process creation cannot replace the current PID owner.");
                        }

                        state.Retire();
                        state = new SessionState(key, clock);
                        Sessions[key.ProcessId] = state;
                    }
                    else if (key.GenerationNumber == state.Key.GenerationNumber)
                    {
                        if (!key.GenerationDigest.Equals(state.Key.GenerationDigest))
                        {
                            throw new InvalidOperationException("The same generation number has a different digest.");
                        }

                        if (!ReferenceEquals(clock, state.Clock))
                        {
                            throw new InvalidOperationException(
                                "All coordinators for one process generation must share one trusted clock.");
                        }
                    }
                    else if (key.GenerationNumber < state.Key.GenerationNumber)
                    {
                        throw new InvalidOperationException("A stale process generation cannot be attached.");
                    }
                    else
                    {
                        ValidateGenerationAdvanceLocked(key, state);
                        state.AdvanceGeneration(key);
                    }

                    return new ProcessGenerationCoordinator(key, state);
                }
            }
        }

        internal CoordinatorStartResult TryStart(SingleCommandRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            using (EnterOperation())
            {
                ClockSample sample;
                if (!TryReadClock(out sample))
                {
                    return RejectStart(
                        CoordinatorStartRejection.TrustedClockFailure,
                        "Trusted monotonic clock failed before the transaction slot changed.");
                }

                lock (GlobalSync)
                {
                    CoordinatorStartRejection stale = ValidateCoordinatorBindingLocked();
                    if (stale != CoordinatorStartRejection.None)
                    {
                        return RejectStart(stale, "This coordinator is not bound to the current process generation.");
                    }

                    if (_session.FaultLatched)
                    {
                        return RejectStart(CoordinatorStartRejection.FaultLatched, "The process/batch fault latch is set.");
                    }

                    if (_session.VerifiedNextGenerationDigest != null)
                    {
                        return RejectStart(
                            CoordinatorStartRejection.GenerationAdvanceRequired,
                            "A verified result requires the authenticated next generation before another start.");
                    }

                    if (_session.ActiveMachine != null && !_session.ActiveMachine.IsTerminal)
                    {
                        return RejectStart(
                            CoordinatorStartRejection.ActiveTransactionExists,
                            "Another coordinator already owns the process-generation transaction slot.");
                    }

                    if (_session.UsedRequestIds.Contains(request.RequestId))
                    {
                        return RejectStart(CoordinatorStartRejection.ReplayedRequest, "Request id was already started.");
                    }

                    if (request.Sequence <= _session.HighestSequence)
                    {
                        return RejectStart(CoordinatorStartRejection.NonMonotonicSequence, "Sequence is not strictly increasing.");
                    }

                    if (!request.GenerationDigest.Equals(_key.GenerationDigest))
                    {
                        return RejectStart(
                            CoordinatorStartRejection.GenerationBindingMismatch,
                            "Request generation digest does not match the coordinator key.");
                    }

                    long deadline;
                    if (!TryComputeDeadline(sample, request.TimeToLiveMilliseconds, out deadline))
                    {
                        return RejectStart(
                            CoordinatorStartRejection.TrustedClockFailure,
                            "Trusted clock frequency or TTL conversion is invalid.");
                    }

                    SyntheticSingleCommandStateMachine machine =
                        new SyntheticSingleCommandStateMachine(
                            request,
                            _key,
                            _session.ObservationCapability,
                            _session.ActionClaimAuthority,
                            _session.SessionId);
                    TransactionHandle handle = new TransactionHandle(_session.SessionId, request);
                    _session.ActiveMachine = machine;
                    _session.ActiveHandle = handle;
                    _session.ActiveDeadlineMonotonicTicks = deadline;
                    _session.UsedRequestIds.Add(request.RequestId);
                    _session.HighestSequence = request.Sequence;
                    return new CoordinatorStartResult(
                        true,
                        CoordinatorStartRejection.None,
                        handle,
                        "Started with a coordinator-stamped relative TTL under the global slot.");
                }
            }
        }

        internal TransactionTransition SubmitObservation(
            TransactionHandle handle,
            SyntheticStageEvidence evidence)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return null;
                    }

                    if (!clockOk)
                    {
                        return ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed while consuming an observation."));
                    }

                    TransactionTransition expired = AbortIfExpiredLocked(machine, sample.Timestamp);
                    if (expired != null)
                    {
                        return expired;
                    }

                    if (evidence == null
                        || !ReferenceEquals(evidence.ObservationCapability, _session.ObservationCapability))
                    {
                        return ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.UntrustedObservation,
                            "Observation is not sealed by this process-generation capability."));
                    }

                    if (ActionStages.IsAction(machine.ExpectedStage))
                    {
                        return ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.ActionIntentRequired,
                            "An action stage cannot advance without a claimed one-shot action receipt."));
                    }

                    return ProcessTransitionLocked(machine.AdvanceObservation(evidence));
                }
            }
        }

        internal ActionIssueResult IssueNextAction(TransactionHandle handle)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return new ActionIssueResult(false, null, null, "No matching active transaction.");
                    }

                    if (!clockOk)
                    {
                        TransactionTransition failure = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed before action intent issuance."));
                        return new ActionIssueResult(false, null, failure, "Clock failure.");
                    }

                    TransactionTransition expired = AbortIfExpiredLocked(machine, sample.Timestamp);
                    if (expired != null)
                    {
                        return new ActionIssueResult(false, null, expired, "Deadline elapsed before intent issuance.");
                    }

                    OneShotActionIntent intent;
                    TransactionTransition failureTransition;
                    if (!machine.TryIssueActionIntent(_session.SessionId, out intent, out failureTransition))
                    {
                        return new ActionIssueResult(
                            false,
                            null,
                            ProcessTransitionLocked(failureTransition),
                            "Action intent was not issued.");
                    }

                    return new ActionIssueResult(true, intent, machine.Snapshot(), "Attempt one was recorded before issuance.");
                }
            }
        }

        internal ActionClaimResult ClaimActionExecution(
            TransactionHandle handle,
            OneShotActionIntent intent)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return new ActionClaimResult(false, null, null, "No matching active transaction.");
                    }

                    if (!clockOk)
                    {
                        TransactionTransition failure = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed before callback execution claim."));
                        return new ActionClaimResult(false, null, failure, "Clock failure.");
                    }

                    TransactionTransition expired = AbortIfExpiredLocked(machine, sample.Timestamp);
                    if (expired != null)
                    {
                        return new ActionClaimResult(false, null, expired, "Deadline elapsed before callback claim.");
                    }

                    if (intent == null
                        || intent.CoordinatorSessionId != _session.SessionId
                        || !ReferenceEquals(intent.Capability, _session.ObservationCapability)
                        || !machine.MatchesPendingIntent(intent))
                    {
                        TransactionTransition mismatch = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.ActionReceiptBindingMismatch,
                            "Execution claim does not match the current pending action intent."));
                        return new ActionClaimResult(false, null, mismatch, "Intent binding mismatch.");
                    }

                    ActionExecutionClaim claim;
                    if (!intent.TryClaimExecution(
                        _session.ActionClaimAuthority,
                        _session.SideEffectAuthority,
                        out claim))
                    {
                        TransactionTransition replay = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.ActionExecutionClaimReplay,
                            "The action execution capability was already claimed or revoked."));
                        return new ActionClaimResult(false, null, replay, "Execution claim replay.");
                    }

                    return new ActionClaimResult(true, claim, machine.Snapshot(), "Execution was claimed once before side effects.");
                }
            }
        }

        internal ActionInvocationResult InvokeClaimedAction(
            TransactionHandle handle,
            ActionExecutionClaim claim,
            INativeSingleCommandCallbacks callbacks)
        {
            if (callbacks == null)
            {
                throw new ArgumentNullException("callbacks");
            }

            NativeActionMethod method = claim == null
                ? NativeActionMethod.BindTargetCandidate
                : ActionStages.GetRequiredMethod(claim.Intent.Stage);
            return InvokeClaimedActionCore(
                handle,
                claim,
                method,
                delegate(SingleCommandRequest request, ActionSideEffectPermit permit)
                {
                    switch (method)
                    {
                        case NativeActionMethod.BindTargetCandidate:
                            return callbacks.BindTargetCandidate(request, permit);
                        case NativeActionMethod.OpenOuter:
                            return callbacks.OpenOuter(request, permit);
                        case NativeActionMethod.OpenSelector:
                            return callbacks.OpenSelector(request, permit);
                        case NativeActionMethod.Clear:
                            return callbacks.Clear(request, permit);
                        case NativeActionMethod.NativeFillMax:
                            return callbacks.NativeFillMax(request, permit);
                        case NativeActionMethod.AcceptInner:
                            return callbacks.AcceptInner(request, permit);
                        case NativeActionMethod.AcceptOuter:
                            return callbacks.AcceptOuter(request, permit);
                        default:
                            throw new InvalidOperationException("Unknown native action method.");
                    }
                });
        }

#if V8_SELF_TEST
        internal ActionInvocationResult InvokeClaimedActionForTesting(
            TransactionHandle handle,
            ActionExecutionClaim claim,
            NativeActionMethod method,
            Func<ActionSideEffectPermit, SyntheticStageEvidence> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            return InvokeClaimedActionCore(
                handle,
                claim,
                method,
                delegate(SingleCommandRequest request, ActionSideEffectPermit permit)
                {
                    return callback(permit);
                });
        }
#endif

        private ActionInvocationResult InvokeClaimedActionCore(
            TransactionHandle handle,
            ActionExecutionClaim claim,
            NativeActionMethod method,
            Func<SingleCommandRequest, ActionSideEffectPermit, SyntheticStageEvidence> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            ActionSideEffectPermit permit;
            SingleCommandRequest callbackRequest;
            TransactionTransition admissionFailure;
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return new ActionInvocationResult(false, null, "No matching active transaction.");
                    }

                    if (!clockOk)
                    {
                        admissionFailure = ProcessTransitionLocked(machine.ForceHaltRestart(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed at the actual action callback boundary."));
                        return new ActionInvocationResult(false, admissionFailure, "Clock failure; callback not invoked.");
                    }

                    TransactionTransition expired = AbortIfExpiredLocked(machine, sample.Timestamp);
                    if (expired != null)
                    {
                        return new ActionInvocationResult(false, expired, "Claim expired before actual callback entry.");
                    }

                    if (claim == null
                        || claim.Intent == null
                        || !ReferenceEquals(claim.Intent, machine.PendingIntent)
                        || !claim.Intent.MatchesActiveClaim(claim)
                        || !machine.MatchesPendingIntent(claim.Intent)
                        || claim.Intent.State != OneShotExecutionState.Executing
                        || claim.Intent.CoordinatorSessionId != _session.SessionId
                        || claim.Intent.ProcessId != _key.ProcessId
                        || claim.Intent.ProcessCreationUtcTicks != _key.ProcessCreationUtcTicks
                        || claim.Intent.GenerationNumber != _key.GenerationNumber
                        || !claim.Intent.GenerationDigest.Equals(_key.GenerationDigest)
                        || !ReferenceEquals(claim.Intent.Capability, _session.ObservationCapability)
                        || ActionStages.GetRequiredMethod(claim.Intent.Stage) != method)
                    {
                        admissionFailure = ProcessTransitionLocked(machine.ForceHaltRestart(
                            AbortUncertainReason.ActionReceiptBindingMismatch,
                            "Action callback entry has wrong active claim, process generation, stage, or method."));
                        return new ActionInvocationResult(false, admissionFailure, "Entry binding mismatch; callback not invoked.");
                    }

                    if (!claim.TryEnterSideEffectOnce(_session.SideEffectAuthority, method))
                    {
                        admissionFailure = ProcessTransitionLocked(machine.ForceHaltRestart(
                            AbortUncertainReason.ActionExecutionClaimReplay,
                            "The same action claim was dispatched to a side-effect callback more than once."));
                        return new ActionInvocationResult(false, admissionFailure, "Side-effect entry replay; callback not invoked.");
                    }

                    permit = new ActionSideEffectPermit(_session.SideEffectAuthority, claim, method);
                    callbackRequest = machine.Request;
                }
            }

            SyntheticStageEvidence evidence = null;
            bool callbackCompleted = false;
            string callbackDiagnostic = string.Empty;
            try
            {
                evidence = callback(callbackRequest, permit);
                callbackCompleted = evidence != null;
                if (!callbackCompleted)
                {
                    callbackDiagnostic = "The native callback returned no stage evidence.";
                }
            }
            catch (Exception exception)
            {
                callbackDiagnostic = exception.GetType().Name + ": " + exception.Message;
            }

            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return new ActionInvocationResult(
                            true, null, "The process generation retired while the callback was running.");
                    }

                    if (machine.IsTerminal || _session.FaultLatched)
                    {
                        return new ActionInvocationResult(
                            true,
                            machine.Snapshot(),
                            "A concurrent fault won while the callback was running; restart remains required.");
                    }

                    if (!clockOk)
                    {
                        TransactionTransition failure = ProcessTransitionLocked(machine.ForceHaltRestart(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed while settling an entered action callback."));
                        return new ActionInvocationResult(true, failure, "Clock failure after callback; restart required.");
                    }

                    TransactionTransition expired = AbortIfExpiredLocked(machine, sample.Timestamp);
                    if (expired != null)
                    {
                        return new ActionInvocationResult(
                            true, expired, "The callback returned after the trusted deadline; restart required.");
                    }

                    if (!ReferenceEquals(claim.Intent, machine.PendingIntent)
                        || !claim.Intent.MatchesActiveClaim(claim)
                        || !machine.MatchesPendingIntent(claim.Intent)
                        || claim.Intent.State != OneShotExecutionState.Executing
                        || !permit.IsAuthorized(_session.SideEffectAuthority, claim, method))
                    {
                        TransactionTransition failure = ProcessTransitionLocked(machine.ForceHaltRestart(
                            AbortUncertainReason.ActionReceiptBindingMismatch,
                            "The entered callback no longer matches the exact active permit at settlement."));
                        return new ActionInvocationResult(true, failure, "Callback settlement binding changed; restart required.");
                    }

                    ActionReceipt receipt;
                    try
                    {
                        receipt = callbackCompleted
                            ? _observationFactory.Complete(
                                permit, evidence, "coordinator-owned callback completed")
                            : _observationFactory.Fail(permit, callbackDiagnostic);
                    }
                    catch (Exception receiptException)
                    {
                        TransactionTransition failure = ProcessTransitionLocked(machine.ForceHaltRestart(
                            AbortUncertainReason.ActionCallbackFailed,
                            "Callback receipt creation failed: " + receiptException.Message));
                        return new ActionInvocationResult(true, failure, "Callback could not be receipted; restart required.");
                    }

                    return new ActionInvocationResult(
                        true,
                        ConsumeActionReceiptLocked(machine, receipt),
                        callbackCompleted
                            ? "Coordinator-owned callback was invoked and receipted exactly once."
                            : "Coordinator-owned callback failure was receipted exactly once.");
                }
            }
        }

#if V8_SELF_TEST
        internal TransactionTransition SubmitActionReceipt(
            TransactionHandle handle,
            ActionReceipt receipt)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return null;
                    }

                    if (!clockOk)
                    {
                        return ProcessTransitionLocked(machine.ForceHaltRestart(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed after callback execution; outcome is uncertain."));
                    }

                    TransactionTransition expired = AbortIfExpiredLocked(machine, sample.Timestamp);
                    if (expired != null)
                    {
                        // Abort revokes only machine.PendingIntent.  A foreign
                        // receipt is never touched in this branch.
                        return expired;
                    }

                    return ConsumeActionReceiptLocked(machine, receipt);
                }
            }
        }
#endif

        private TransactionTransition ConsumeActionReceiptLocked(
            SyntheticSingleCommandStateMachine machine,
            ActionReceipt receipt)
        {
            NativeActionMethod method = receipt == null || receipt.Intent == null
                ? NativeActionMethod.BindTargetCandidate
                : ActionStages.GetRequiredMethod(receipt.Intent.Stage);
            if (receipt == null
                || receipt.Permit == null
                || receipt.PermitId != receipt.Permit.PermitId
                || receipt.Intent == null
                || receipt.Claim == null
                || !receipt.Intent.MatchesActiveClaim(receipt.Claim)
                || receipt.IntentNonce != receipt.Intent.Nonce
                || receipt.ClaimNonce != receipt.Claim.ClaimNonce
                || receipt.Intent.CoordinatorSessionId != _session.SessionId
                || !ReferenceEquals(receipt.Capability, _session.ObservationCapability)
                || !ReferenceEquals(receipt.Intent.Capability, _session.ObservationCapability)
                || !receipt.Permit.IsAuthorized(_session.SideEffectAuthority, receipt.Claim, method)
                || !machine.MatchesPendingIntent(receipt.Intent))
            {
                return ProcessTransitionLocked(machine.ForceHaltRestart(
                    AbortUncertainReason.ActionReceiptBindingMismatch,
                    "Action receipt is absent, unauthenticated, or not bound to the exact entered permit."));
            }

            if (!receipt.Intent.TryConsumeReceiptOnce())
            {
                return ProcessTransitionLocked(machine.ForceHaltRestart(
                    AbortUncertainReason.ActionReceiptReplay,
                    "The action execution receipt was replayed or not produced by the entered callback."));
            }

            if (!receipt.CallbackCompleted)
            {
                return ProcessTransitionLocked(machine.ForceHaltRestart(
                    AbortUncertainReason.ActionCallbackFailed,
                    "The entered callback failed before authenticated stage evidence was available: "
                        + receipt.Diagnostic));
            }

            if (receipt.Evidence == null
                || !ReferenceEquals(receipt.Evidence.ObservationCapability, _session.ObservationCapability))
            {
                return ProcessTransitionLocked(machine.ForceHaltRestart(
                    AbortUncertainReason.ActionReceiptBindingMismatch,
                    "Successful callback receipt lacks current-session evidence."));
            }

            return ProcessTransitionLocked(machine.ConsumeActionReceipt(receipt.Intent, receipt.Evidence));
        }

        internal PostReadPairIssueResult IssuePostReadPair(TransactionHandle handle)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return new PostReadPairIssueResult(false, null, null, null, "No matching active transaction.");
                    }

                    if (!clockOk)
                    {
                        TransactionTransition failure = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed before post-read ticket issuance."));
                        return new PostReadPairIssueResult(false, null, null, failure, "Clock failure.");
                    }

                    TransactionTransition expired = AbortIfExpiredLocked(machine, sample.Timestamp);
                    if (expired != null)
                    {
                        return new PostReadPairIssueResult(false, null, null, expired, "Deadline elapsed.");
                    }

                    if (machine.ExpectedStage != TransactionStage.VerifyCommitted)
                    {
                        TransactionTransition wrongStage = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.UnexpectedStageOrReplay,
                            "Post read tickets can be issued only for VerifyCommitted."));
                        return new PostReadPairIssueResult(false, null, null, wrongStage, "Wrong stage.");
                    }

                    if (_session.FirstPostReadTicket != null || _session.SecondPostReadTicket != null)
                    {
                        TransactionTransition replay = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.PostReadTicketReplay,
                            "Post read A/B tickets were already issued and are never reissued."));
                        return new PostReadPairIssueResult(false, null, null, replay, "Ticket replay.");
                    }

                    _session.FirstPostReadTicket = new PostReadTicket(
                        _session.ObservationCapability,
                        _session.PostReadClaimAuthority,
                        _key,
                        machine.Request,
                        1);
                    _session.SecondPostReadTicket = new PostReadTicket(
                        _session.ObservationCapability,
                        _session.PostReadClaimAuthority,
                        _key,
                        machine.Request,
                        2);
                    return new PostReadPairIssueResult(
                        true,
                        _session.FirstPostReadTicket,
                        _session.SecondPostReadTicket,
                        machine.Snapshot(),
                        "Independent ordered A/B read tickets issued once.");
                }
            }
        }

        internal PostReadClaimResult ClaimPostRead(
            TransactionHandle handle,
            PostReadTicket ticket)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return new PostReadClaimResult(false, null, null, "No matching active transaction.");
                    }

                    if (!clockOk)
                    {
                        TransactionTransition failure = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed before post read claim."));
                        return new PostReadClaimResult(false, null, failure, "Clock failure.");
                    }

                    TransactionTransition expired = AbortIfExpiredLocked(machine, sample.Timestamp);
                    if (expired != null)
                    {
                        return new PostReadClaimResult(false, null, expired, "Deadline elapsed.");
                    }

                    if (ticket == null
                        || (!ReferenceEquals(ticket, _session.FirstPostReadTicket)
                            && !ReferenceEquals(ticket, _session.SecondPostReadTicket))
                        || !ReferenceEquals(ticket.Capability, _session.ObservationCapability)
                        || ticket.CoordinatorSessionId != _session.SessionId)
                    {
                        TransactionTransition mismatch = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.PostReadTicketMismatch,
                            "Post read ticket is foreign or stale."));
                        return new PostReadClaimResult(false, null, mismatch, "Ticket mismatch.");
                    }

                    if (ReferenceEquals(ticket, _session.SecondPostReadTicket)
                        && (_session.FirstPostReadTicket == null
                            || _session.FirstPostReadTicket.State != OneShotExecutionState.Receipted))
                    {
                        TransactionTransition outOfOrder = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.PostReadTicketMismatch,
                            "Post read B cannot be claimed until independent read A has produced its receipt."));
                        return new PostReadClaimResult(false, null, outOfOrder, "A/B read order mismatch.");
                    }

                    PostReadClaim claim;
                    if (!ticket.TryClaim(_session.PostReadClaimAuthority, out claim))
                    {
                        TransactionTransition replay = ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.PostReadTicketReplay,
                            "Post read ticket was already claimed, consumed, or revoked."));
                        return new PostReadClaimResult(false, null, replay, "Ticket replay.");
                    }

                    return new PostReadClaimResult(true, claim, machine.Snapshot(), "Read ticket claimed once.");
                }
            }
        }

        internal TransactionTransition PollExpiry(TransactionHandle handle)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    SyntheticSingleCommandStateMachine machine = ResolveActiveLocked(handle);
                    if (machine == null)
                    {
                        return null;
                    }

                    if (!clockOk)
                    {
                        return ProcessTransitionLocked(machine.ForceAbort(
                            AbortUncertainReason.TrustedClockFailure,
                            "Trusted clock failed during explicit expiry poll."));
                    }

                    return AbortIfExpiredLocked(machine, sample.Timestamp);
                }
            }
        }

        internal FaultLatchSnapshot GetFaultLatch()
        {
            using (EnterOperation())
            {
                lock (GlobalSync)
                {
                    if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None)
                    {
                        throw new InvalidOperationException("This coordinator belongs to a retired process generation.");
                    }

                    return new FaultLatchSnapshot(_session);
                }
            }
        }

        internal CleanupIssueResult IssueCleanup(Guid faultId, Guid directiveId)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None
                        || !_session.FaultLatched
                        || _session.FaultId != faultId
                        || _session.CleanupDirective == null
                        || _session.CleanupDirective.DirectiveId != directiveId)
                    {
                        return new CleanupIssueResult(false, null, "Fault or directive binding is stale.");
                    }

                    if (!clockOk)
                    {
                        _session.EscalateCleanupFailure("Trusted clock failed before cleanup intent issuance.");
                        return new CleanupIssueResult(false, null, "Clock failure; restart required.");
                    }

                    if (_session.CleanupDirective.RequiresProcessRestart
                        || !_session.CleanupDirective.RequiresExternalCleanup
                        || _session.CleanupCompleted
                        || _session.CleanupFailed)
                    {
                        return new CleanupIssueResult(false, null, "Directive does not permit an external cleanup action.");
                    }

                    if (_session.CleanupIntent != null)
                    {
                        _session.EscalateCleanupFailure("Cleanup intent issuance was replayed.");
                        return new CleanupIssueResult(false, null, "Cleanup intent replay; restart required.");
                    }

                    long deadline;
                    if (!TryComputeDeadline(sample, CleanupTimeToLiveMilliseconds, out deadline))
                    {
                        _session.EscalateCleanupFailure("Cleanup TTL could not be stamped.");
                        return new CleanupIssueResult(false, null, "Invalid clock conversion; restart required.");
                    }

                    _session.CleanupIntent = new OneShotCleanupIntent(
                        _session.ObservationCapability,
                        _session.CleanupClaimAuthority,
                        faultId,
                        _session.CleanupDirective,
                        deadline);
                    return new CleanupIssueResult(true, _session.CleanupIntent, "One cleanup attempt was issued.");
                }
            }
        }

        internal CleanupClaimResult ClaimCleanupExecution(OneShotCleanupIntent intent)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None
                        || !_session.FaultLatched
                        || intent == null
                        || !ReferenceEquals(intent, _session.CleanupIntent)
                        || !ReferenceEquals(intent.Capability, _session.ObservationCapability))
                    {
                        if (_session.FaultLatched)
                        {
                            _session.EscalateCleanupFailure("Unknown cleanup execution capability was presented.");
                        }

                        return new CleanupClaimResult(false, null, "Cleanup intent is foreign or stale.");
                    }

                    if (!clockOk || sample.Timestamp >= intent.DeadlineMonotonicTicks)
                    {
                        intent.Revoke();
                        _session.EscalateCleanupFailure("Cleanup claim expired or trusted clock failed.");
                        return new CleanupClaimResult(false, null, "Cleanup claim failed closed; restart required.");
                    }

                    CleanupExecutionClaim claim;
                    if (!intent.TryClaim(
                        _session.CleanupClaimAuthority,
                        _session.SideEffectAuthority,
                        out claim))
                    {
                        _session.EscalateCleanupFailure("Cleanup execution claim was replayed.");
                        return new CleanupClaimResult(false, null, "Cleanup execution replay; restart required.");
                    }

                    return new CleanupClaimResult(true, claim, "Cleanup execution claimed once before any side effect.");
                }
            }
        }

        internal CleanupInvocationResult InvokeClaimedCleanup(
            CleanupExecutionClaim claim,
            INativeAbortCleanupCallback callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            NativeCleanupMethod method = claim == null
                ? NativeCleanupMethod.ConditionalRestoreTarget
                : CleanupMethods.GetRequiredMethod(claim.Intent.Directive.Kind);
            return InvokeClaimedCleanupCore(
                claim,
                method,
                delegate(CleanupSideEffectPermit permit)
                {
                    switch (method)
                    {
                        case NativeCleanupMethod.ConditionalRestoreTarget:
                            callback.ConditionalRestoreTarget(permit);
                            break;
                        case NativeCleanupMethod.CancelSelector:
                            callback.CancelSelector(permit);
                            break;
                        case NativeCleanupMethod.CancelOuter:
                            callback.CancelOuter(permit);
                            break;
                        case NativeCleanupMethod.ReviewAfterAccept:
                            callback.ReviewAfterAccept(permit);
                            break;
                        default:
                            throw new InvalidOperationException("Unknown native cleanup method.");
                    }
                },
                delegate(CleanupPostReadPermit readPermit)
                {
                    return callback.ReadPostState(readPermit);
                });
        }

#if V8_SELF_TEST
        internal CleanupInvocationResult InvokeClaimedCleanupForTesting(
            CleanupExecutionClaim claim,
            NativeCleanupMethod method,
            Action<CleanupSideEffectPermit> callback,
            Func<CleanupPostReadPermit, CleanupPostStateRead> readCallback)
        {
            return InvokeClaimedCleanupCore(claim, method, callback, readCallback);
        }

        internal CleanupSideEffectPermit AuthorizeCleanupSideEffectForReceiptTesting(
            CleanupExecutionClaim claim)
        {
            if (claim == null || claim.Intent == null || claim.Intent.Directive == null)
            {
                throw new ArgumentNullException("claim");
            }

            CleanupSideEffectPermit permit;
            string detail;
            if (!TryAdmitCleanupSideEffect(
                claim,
                CleanupMethods.GetRequiredMethod(claim.Intent.Directive.Kind),
                out permit,
                out detail))
            {
                throw new InvalidOperationException(detail);
            }

            return permit;
        }

        internal CleanupPostReadClaim ClaimCleanupPostReadForTesting(
            CleanupSideEffectPermit permit,
            int ordinal)
        {
            using (EnterOperation())
            {
                lock (GlobalSync)
                {
                    return _observationFactory.ClaimCleanupPostRead(
                        _session.SideEffectAuthority, permit, ordinal);
                }
            }
        }

        internal AuthenticatedCleanupPostReadReceipt CompleteCleanupPostReadForTesting(
            CleanupPostReadClaim claim,
            CleanupPostStateRead read)
        {
            using (EnterOperation())
            {
                lock (GlobalSync)
                {
                    return _observationFactory.CompleteCleanupPostRead(
                        _session.SideEffectAuthority, claim, read);
                }
            }
        }

        internal AuthenticatedCleanupReceipt CompleteCleanupForTesting(
            CleanupSideEffectPermit permit,
            AuthenticatedCleanupPostReadReceipt first,
            AuthenticatedCleanupPostReadReceipt second,
            string diagnostic)
        {
            using (EnterOperation())
            {
                lock (GlobalSync)
                {
                    return _observationFactory.CompleteCleanup(
                        _session.SideEffectAuthority, permit, first, second, diagnostic);
                }
            }
        }
#endif

        private CleanupInvocationResult InvokeClaimedCleanupCore(
            CleanupExecutionClaim claim,
            NativeCleanupMethod method,
            Action<CleanupSideEffectPermit> callback,
            Func<CleanupPostReadPermit, CleanupPostStateRead> readCallback)
        {
            if (callback == null || readCallback == null)
            {
                throw new ArgumentNullException(callback == null ? "callback" : "readCallback");
            }

            CleanupSideEffectPermit permit;
            string admissionDetail;
            if (!TryAdmitCleanupSideEffect(claim, method, out permit, out admissionDetail))
            {
                return new CleanupInvocationResult(false, false, admissionDetail);
            }

            try
            {
                callback(permit);
            }
            catch (Exception exception)
            {
                return SettleCleanupFailure(
                    permit,
                    exception.GetType().Name + ": " + exception.Message);
            }

            AuthenticatedCleanupPostReadReceipt first;
            if (!TryCollectCleanupPostRead(permit, 1, readCallback, out first))
            {
                return new CleanupInvocationResult(
                    true, false, "Cleanup post-read A failed closed; restart required.");
            }

            AuthenticatedCleanupPostReadReceipt second;
            if (!TryCollectCleanupPostRead(permit, 2, readCallback, out second))
            {
                return new CleanupInvocationResult(
                    true, false, "Cleanup post-read B failed closed; restart required.");
            }

            return SettleCleanupSuccess(permit, first, second);
        }

        private CleanupInvocationResult SettleCleanupFailure(
            CleanupSideEffectPermit permit,
            string diagnostic)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None)
                    {
                        return new CleanupInvocationResult(
                            true, false, "The process generation retired while cleanup was running.");
                    }

                    if (!ValidateCleanupPermitForSettlementLocked(permit))
                    {
                        if (_session.FaultLatched && !_session.CleanupFailed)
                        {
                            _session.EscalateCleanupFailure(
                                "Cleanup settlement lost its exact entered permit binding.");
                        }

                        return new CleanupInvocationResult(
                            true, false, "A concurrent cleanup fault won; restart remains required.");
                    }

                    if (!clockOk
                        || sample.Timestamp >= permit.Claim.Intent.DeadlineMonotonicTicks)
                    {
                        permit.Claim.Intent.Revoke();
                        _session.EscalateCleanupFailure(
                            "Cleanup callback returned after its trusted deadline or clock failure.");
                        return new CleanupInvocationResult(
                            true, false, "Cleanup settlement failed closed; restart required.");
                    }

                    AuthenticatedCleanupReceipt receipt;
                    try
                    {
                        receipt = _observationFactory.FailCleanup(
                            _session.SideEffectAuthority, permit, diagnostic);
                    }
                    catch (Exception exception)
                    {
                        _session.EscalateCleanupFailure(
                            "Cleanup failure receipt could not be created: " + exception.Message);
                        return new CleanupInvocationResult(
                            true, false, "Cleanup callback failure could not be receipted.");
                    }

                    ConsumeCleanupReceiptLocked(receipt);
                    return new CleanupInvocationResult(
                        true, false, "Cleanup callback failure was receipted and requires restart.");
                }
            }
        }

        private bool TryCollectCleanupPostRead(
            CleanupSideEffectPermit permit,
            int ordinal,
            Func<CleanupPostReadPermit, CleanupPostStateRead> readCallback,
            out AuthenticatedCleanupPostReadReceipt receipt)
        {
            receipt = null;
            CleanupPostReadClaim claim;
            CleanupPostReadPermit readPermit;
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    if (!ValidateCleanupPermitForSettlementLocked(permit)
                        || !clockOk
                        || sample.Timestamp >= permit.Claim.Intent.DeadlineMonotonicTicks)
                    {
                        if (_session.FaultLatched && !_session.CleanupFailed)
                        {
                            _session.EscalateCleanupFailure(
                                "Cleanup post-read admission lost binding or exceeded its deadline.");
                        }

                        return false;
                    }

                    try
                    {
                        claim = _observationFactory.ClaimCleanupPostRead(
                            _session.SideEffectAuthority, permit, ordinal);
                        readPermit = new CleanupPostReadPermit(permit, claim);
                    }
                    catch (Exception exception)
                    {
                        _session.EscalateCleanupFailure(
                            "Cleanup post-read ticket claim failed: " + exception.Message);
                        return false;
                    }
                }
            }

            CleanupPostStateRead read;
            try
            {
                read = readCallback(readPermit);
            }
            catch (Exception exception)
            {
                using (EnterOperation())
                {
                    lock (GlobalSync)
                    {
                        if (_session.FaultLatched)
                        {
                            _session.EscalateCleanupFailure(
                                "Cleanup post-read callback failed: " + exception.Message);
                        }
                    }
                }

                return false;
            }

            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    if (!ValidateCleanupPermitForSettlementLocked(permit)
                        || !clockOk
                        || sample.Timestamp >= permit.Claim.Intent.DeadlineMonotonicTicks
                        || read == null
                        || !ReferenceEquals(readPermit.CleanupPermit, permit)
                        || !ReferenceEquals(readPermit.Claim, claim)
                        || readPermit.PermitId != permit.PermitId
                        || readPermit.TicketId != claim.Ticket.TicketId
                        || readPermit.Ordinal != ordinal)
                    {
                        if (_session.FaultLatched && !_session.CleanupFailed)
                        {
                            _session.EscalateCleanupFailure(
                                "Cleanup post-read result lost its exact returned-callback permit binding.");
                        }

                        return false;
                    }

                    try
                    {
                        receipt = _observationFactory.CompleteCleanupPostRead(
                            _session.SideEffectAuthority, claim, read);
                        return true;
                    }
                    catch (Exception exception)
                    {
                        _session.EscalateCleanupFailure(
                            "Cleanup post-read receipt creation failed: " + exception.Message);
                        return false;
                    }
                }
            }
        }

        private CleanupInvocationResult SettleCleanupSuccess(
            CleanupSideEffectPermit permit,
            AuthenticatedCleanupPostReadReceipt first,
            AuthenticatedCleanupPostReadReceipt second)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    if (!ValidateCleanupPermitForSettlementLocked(permit)
                        || !clockOk
                        || sample.Timestamp >= permit.Claim.Intent.DeadlineMonotonicTicks)
                    {
                        if (_session.FaultLatched && !_session.CleanupFailed)
                        {
                            _session.EscalateCleanupFailure(
                                "Cleanup final settlement lost binding or exceeded its deadline.");
                        }

                        return new CleanupInvocationResult(
                            true, false, "Cleanup final settlement failed closed; restart required.");
                    }

                    AuthenticatedCleanupReceipt receipt;
                    try
                    {
                        receipt = _observationFactory.CompleteCleanup(
                            _session.SideEffectAuthority,
                            permit,
                            first,
                            second,
                            "coordinator-owned cleanup and post-action A/B reads completed");
                    }
                    catch (Exception exception)
                    {
                        _session.EscalateCleanupFailure(
                            "Cleanup final receipt creation failed: " + exception.Message);
                        return new CleanupInvocationResult(
                            true, false, "Cleanup receipt could not be authenticated.");
                    }

                    bool accepted = ConsumeCleanupReceiptLocked(receipt);
                    return new CleanupInvocationResult(
                        true,
                        accepted,
                        accepted
                            ? "Coordinator-owned cleanup and post-action A/B reads completed exactly once."
                            : "Cleanup receipt failed authentication; restart required.");
                }
            }
        }

        private bool ValidateCleanupPermitForSettlementLocked(CleanupSideEffectPermit permit)
        {
            OneShotCleanupIntent expected = _session.CleanupIntent;
            CleanupExecutionClaim claim = permit == null ? null : permit.Claim;
            return ValidateCoordinatorBindingLocked() == CoordinatorStartRejection.None
                && _session.FaultLatched
                && !_session.CleanupFailed
                && !_session.CleanupCompleted
                && expected != null
                && claim != null
                && ReferenceEquals(expected, claim.Intent)
                && expected.MatchesActiveClaim(claim)
                && claim.MatchesActivePermit(permit)
                && ReferenceEquals(expected.Directive, _session.CleanupDirective)
                && expected.State == OneShotExecutionState.Executing
                && permit.IsAuthorized(
                    _session.SideEffectAuthority,
                    claim,
                    CleanupMethods.GetRequiredMethod(expected.Directive.Kind));
        }

        private bool TryAdmitCleanupSideEffect(
            CleanupExecutionClaim claim,
            NativeCleanupMethod method,
            out CleanupSideEffectPermit permit,
            out string detail)
        {
            permit = null;
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    OneShotCleanupIntent expected = _session.CleanupIntent;
                    AbortCleanupDirective directive = expected == null ? null : expected.Directive;
                    if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None
                        || !_session.FaultLatched
                        || _session.CleanupFailed
                        || _session.CleanupCompleted
                        || expected == null
                        || claim == null
                        || claim.Intent == null
                        || !ReferenceEquals(claim.Intent, expected)
                        || !expected.MatchesActiveClaim(claim)
                        || !ReferenceEquals(expected.Capability, _session.ObservationCapability)
                        || !ReferenceEquals(directive, _session.CleanupDirective)
                        || expected.CoordinatorSessionId != _session.SessionId
                        || expected.FaultId != _session.FaultId
                        || directive == null
                        || directive.ProcessId != _key.ProcessId
                        || directive.ProcessCreationUtcTicks != _key.ProcessCreationUtcTicks
                        || directive.GenerationNumber != _key.GenerationNumber
                        || !directive.GenerationDigest.Equals(_key.GenerationDigest)
                        || expected.State != OneShotExecutionState.Executing
                        || CleanupMethods.GetRequiredMethod(directive.Kind) != method)
                    {
                        if (_session.FaultLatched)
                        {
                            _session.EscalateCleanupFailure(
                                "Cleanup callback entry has wrong active claim, process generation, directive, or method.");
                        }

                        detail = "Cleanup side-effect entry binding mismatch; callback not invoked.";
                        return false;
                    }

                    if (!clockOk || sample.Timestamp >= expected.DeadlineMonotonicTicks)
                    {
                        expected.Revoke();
                        _session.EscalateCleanupFailure(
                            "Cleanup side-effect entry expired or trusted clock failed.");
                        detail = "Cleanup side-effect entry failed closed; callback not invoked.";
                        return false;
                    }

                    if (!claim.TryEnterSideEffectOnce(
                        _session.SideEffectAuthority,
                        method,
                        out permit))
                    {
                        _session.EscalateCleanupFailure(
                            "The same cleanup claim was dispatched to a side-effect callback more than once.");
                        detail = "Cleanup side-effect entry replay; callback not invoked.";
                        return false;
                    }

                    detail = "Cleanup side-effect entry admitted exactly once.";
                    return true;
                }
            }
        }

#if V8_SELF_TEST
        internal bool SubmitCleanupReceipt(AuthenticatedCleanupReceipt receipt)
        {
            using (EnterOperation())
            {
                ClockSample sample;
                bool clockOk = TryReadClock(out sample);
                lock (GlobalSync)
                {
                    if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None
                        || !_session.FaultLatched)
                    {
                        return false;
                    }

                    OneShotCleanupIntent expected = _session.CleanupIntent;
                    if (!clockOk
                        || expected == null
                        || sample.Timestamp >= expected.DeadlineMonotonicTicks
                        || receipt == null
                        || !ReferenceEquals(receipt.Intent, expected)
                        || !ReferenceEquals(receipt.Capability, _session.ObservationCapability)
                        || receipt.Intent.FaultId != _session.FaultId
                        || !ReferenceEquals(receipt.Intent.Directive, _session.CleanupDirective))
                    {
                        if (expected != null)
                        {
                            expected.Revoke();
                        }

                        _session.EscalateCleanupFailure("Cleanup receipt was expired, unknown, or unauthenticated.");
                        return false;
                    }

                    return ConsumeCleanupReceiptLocked(receipt);
                }
            }
        }
#endif

        private bool ConsumeCleanupReceiptLocked(AuthenticatedCleanupReceipt receipt)
        {
            OneShotCleanupIntent expected = _session.CleanupIntent;
            if (expected == null
                || receipt == null
                || receipt.Permit == null
                || receipt.PermitId != receipt.Permit.PermitId
                || receipt.Claim == null
                || !receipt.Claim.SideEffectEntered
                || !ReferenceEquals(receipt.Intent, expected)
                || !expected.MatchesActiveClaim(receipt.Claim)
                || !ReferenceEquals(receipt.Capability, _session.ObservationCapability)
                || receipt.Intent.FaultId != _session.FaultId
                || !ReferenceEquals(receipt.Intent.Directive, _session.CleanupDirective)
                || !receipt.Permit.IsAuthorized(
                    _session.SideEffectAuthority,
                    receipt.Claim,
                    CleanupMethods.GetRequiredMethod(expected.Directive.Kind)))
            {
                if (expected != null)
                {
                    expected.Revoke();
                }

                _session.EscalateCleanupFailure(
                    "Cleanup receipt was not bound to the exact entered side-effect claim.");
                return false;
            }

            if (!expected.TryConsumeReceipt())
            {
                _session.EscalateCleanupFailure("Cleanup receipt was replayed or concurrently consumed.");
                return false;
            }

            if (!receipt.CallbackCompleted
                || !receipt.IsStable
                || !ValidateCleanupPostStateLocked(receipt.First)
                || !ValidateCleanupPostStateLocked(receipt.Second))
            {
                _session.EscalateCleanupFailure(
                    "Cleanup callback failed or authenticated A/B object postconditions did not hold.");
                return false;
            }

            _session.CleanupCompleted = true;
            if (_session.CleanupDirective.Kind == AbortCleanupKind.DoNotRollbackAfterAccept)
            {
                _session.VerifiedNextGenerationNumber = receipt.Second.GenerationNumber;
                _session.VerifiedNextGenerationDigest = receipt.Second.GenerationDigest;
            }

            return true;
        }

        internal bool AcknowledgeFault(Guid faultId, string exactAcknowledgement)
        {
            using (EnterOperation())
            {
                lock (GlobalSync)
                {
                    if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None
                        || !_session.FaultLatched
                        || _session.FaultId != faultId
                        || !string.Equals(exactAcknowledgement, RequiredManualAcknowledgement, StringComparison.Ordinal)
                        || _session.CleanupFailed
                        || (_session.CleanupDirective != null
                            && (_session.CleanupDirective.RequiresProcessRestart
                                || (_session.CleanupDirective.RequiresExternalCleanup
                                    && !_session.CleanupCompleted))))
                    {
                        return false;
                    }

                    _session.FaultAcknowledged = true;
                    return true;
                }
            }
        }

        internal bool RetireProcess()
        {
            using (EnterOperation())
            {
                lock (GlobalSync)
                {
                    SessionState current;
                    if (!Sessions.TryGetValue(_key.ProcessId, out current)
                        || !ReferenceEquals(current, _session)
                        || current.Key.ProcessCreationUtcTicks != _key.ProcessCreationUtcTicks
                        || current.ActiveMachine != null
                        || current.FaultLatched
                        || current.VerifiedNextGenerationDigest != null
                        || current.HighestSequence != 0)
                    {
                        return false;
                    }

                    current.Retire();
                    return Sessions.Remove(_key.ProcessId);
                }
            }
        }

        private static void ValidateGenerationAdvanceLocked(ProcessGenerationKey key, SessionState state)
        {
            if (key.GenerationDigest.Equals(state.Key.GenerationDigest))
            {
                throw new InvalidOperationException(
                    "A strictly newer generation number must also carry a new generation digest.");
            }

            if (state.ActiveMachine != null && !state.ActiveMachine.IsTerminal)
            {
                throw new InvalidOperationException("A new generation cannot replace an active transaction.");
            }

            if (state.VerifiedNextGenerationDigest != null
                && (key.GenerationNumber != state.VerifiedNextGenerationNumber
                    || !key.GenerationDigest.Equals(state.VerifiedNextGenerationDigest)))
            {
                throw new InvalidOperationException(
                    "The next generation must equal the authenticated post-state generation.");
            }

            if (state.FaultLatched
                && (!state.FaultAcknowledged
                    || !state.CleanupCompleted
                    || state.CleanupFailed
                    || (state.CleanupDirective != null && state.CleanupDirective.RequiresProcessRestart)))
            {
                throw new InvalidOperationException(
                    "AbortUncertain requires authenticated cleanup, explicit acknowledgement, and a recoverable state.");
            }
        }

        private CoordinatorStartRejection ValidateCoordinatorBindingLocked()
        {
            SessionState current;
            return _session.Retired
                || !Sessions.TryGetValue(_key.ProcessId, out current)
                || !ReferenceEquals(current, _session)
                || _session.SessionId == Guid.Empty
                || _key.ProcessCreationUtcTicks != _session.Key.ProcessCreationUtcTicks
                || _key.GenerationNumber != _session.Key.GenerationNumber
                || !_key.GenerationDigest.Equals(_session.Key.GenerationDigest)
                ? CoordinatorStartRejection.StaleCoordinator
                : CoordinatorStartRejection.None;
        }

        private SyntheticSingleCommandStateMachine ResolveActiveLocked(TransactionHandle handle)
        {
            if (ValidateCoordinatorBindingLocked() != CoordinatorStartRejection.None
                || handle == null
                || _session.ActiveMachine == null
                || _session.ActiveHandle == null
                || handle.CoordinatorSessionId != _session.SessionId
                || handle.RequestId != _session.ActiveHandle.RequestId
                || handle.Sequence != _session.ActiveHandle.Sequence
                || !handle.RequestFingerprint.Equals(_session.ActiveHandle.RequestFingerprint))
            {
                return null;
            }

            return _session.ActiveMachine;
        }

        private TransactionTransition AbortIfExpiredLocked(
            SyntheticSingleCommandStateMachine machine,
            long now)
        {
            if (now < _session.ActiveDeadlineMonotonicTicks)
            {
                return null;
            }

            return ProcessTransitionLocked(machine.ForceAbort(
                AbortUncertainReason.ExternalTimeout,
                "Coordinator-stamped trusted monotonic deadline elapsed; no capability is reissued."));
        }

        private TransactionTransition ProcessTransitionLocked(TransactionTransition transition)
        {
            if (transition == null)
            {
                return null;
            }

            if (transition.Outcome == TransactionOutcome.AbortUncertain)
            {
                _session.RevokePostReadTickets();
                _session.LatchFault(transition.CleanupDirective);
            }
            else if (transition.IsTerminal)
            {
                if (transition.Outcome == TransactionOutcome.SyntheticCommittedVerified
                    && _session.ActiveMachine != null)
                {
                    _session.VerifiedNextGenerationNumber =
                        _session.ActiveMachine.VerifiedNextGenerationNumber;
                    _session.VerifiedNextGenerationDigest =
                        _session.ActiveMachine.VerifiedNextGenerationDigest;
                }

                _session.ActiveMachine = null;
                _session.ActiveHandle = null;
                _session.ActiveDeadlineMonotonicTicks = 0;
            }

            return transition;
        }

        private bool ValidateCleanupPostStateLocked(CleanupPostStateRead read)
        {
            AbortCleanupDirective directive = _session.CleanupDirective;
            if (read == null
                || directive == null
                || read.ProcessId != directive.ProcessId
                || read.ProcessCreationUtcTicks != directive.ProcessCreationUtcTicks
                || !read.ContextDigest.Equals(directive.ContextDigest)
                || read.FaultId != _session.FaultId
                || read.DirectiveId != directive.DirectiveId
                || read.RequestId != directive.RequestId
                || !read.RequestFingerprint.Equals(directive.RequestFingerprint)
                || !read.RootBefore.Equals(directive.RootAtAbort)
                || !read.TargetBefore.Equals(directive.TargetAtAbort)
                || !read.OuterBefore.Equals(directive.OuterAtAbort)
                || !read.SelectorBefore.Equals(directive.SelectorAtAbort)
                || !read.NoUnexpectedMutation)
            {
                return false;
            }

            if (directive.Kind == AbortCleanupKind.DoNotRollbackAfterAccept)
            {
                return read.GenerationNumber > directive.GenerationNumber
                    && !read.GenerationDigest.Equals(directive.GenerationDigest)
                    && read.RootAfter.IsAbsent
                    && read.TargetAfter.IsAbsent
                    && read.OuterAfter.IsAbsent
                    && read.SelectorAfter.IsAbsent
                    && read.ManualReviewCompleted;
            }

            return read.GenerationNumber == directive.GenerationNumber
                && read.GenerationDigest.Equals(directive.GenerationDigest)
                && read.RootAfter.Equals(directive.RootAtAbort)
                && read.RootAfter.Kind == ObjectKind.RootController
                && read.RootAfter.IsAlive
                && read.TargetAfter.IsAbsent
                && read.OuterAfter.IsAbsent
                && read.SelectorAfter.IsAbsent
                && !read.ManualReviewCompleted;
        }

        private bool TryReadClock(out ClockSample sample)
        {
            try
            {
                long frequency = _session.Clock.Frequency;
                long timestamp = _session.Clock.GetTimestamp();
                if (frequency <= 0 || timestamp < 0)
                {
                    sample = default(ClockSample);
                    return false;
                }

                sample = new ClockSample(timestamp, frequency);
                return true;
            }
            catch (Exception)
            {
                sample = default(ClockSample);
                return false;
            }
        }

        private static bool TryComputeDeadline(
            ClockSample sample,
            int timeToLiveMilliseconds,
            out long deadline)
        {
            try
            {
                long ttlTicks = checked(
                    (checked((long)timeToLiveMilliseconds * sample.Frequency) + 999L) / 1000L);
                if (ttlTicks <= 0)
                {
                    deadline = 0;
                    return false;
                }

                deadline = checked(sample.Timestamp + ttlTicks);
                return true;
            }
            catch (OverflowException)
            {
                deadline = 0;
                return false;
            }
        }

        private static CoordinatorStartResult RejectStart(
            CoordinatorStartRejection rejection,
            string detail)
        {
            return new CoordinatorStartResult(false, rejection, null, detail);
        }

        private static CoordinatorOperationScope EnterOperation()
        {
            if (_insideCoordinatorOperation)
            {
                throw new InvalidOperationException(
                    "Coordinator re-entry is forbidden; trusted callbacks cannot mutate generation state.");
            }

            Monitor.Enter(GlobalOperationGate);
            _insideCoordinatorOperation = true;
            return new CoordinatorOperationScope();
        }

        private struct ClockSample
        {
            internal ClockSample(long timestamp, long frequency)
            {
                Timestamp = timestamp;
                Frequency = frequency;
            }

            internal long Timestamp;
            internal long Frequency;
        }

        private sealed class CoordinatorOperationScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _insideCoordinatorOperation = false;
                Monitor.Exit(GlobalOperationGate);
                _disposed = true;
            }
        }
    }

    internal sealed class SessionState
    {
        internal SessionState(ProcessGenerationKey key, ITrustedMonotonicClock clock)
        {
            Clock = clock;
            Key = key;
            ResetGenerationState();
        }

        internal ProcessGenerationKey Key;
        internal ITrustedMonotonicClock Clock;
        internal bool Retired;
        internal Guid SessionId;
        internal SessionObservationCapability ObservationCapability;
        internal CoordinatorActionClaimAuthority ActionClaimAuthority;
        internal CoordinatorSideEffectAuthority SideEffectAuthority;
        internal CoordinatorPostReadClaimAuthority PostReadClaimAuthority;
        internal CoordinatorCleanupClaimAuthority CleanupClaimAuthority;
        internal TrustedObservationFactory ObservationFactory;
        internal SyntheticSingleCommandStateMachine ActiveMachine;
        internal TransactionHandle ActiveHandle;
        internal long ActiveDeadlineMonotonicTicks;
        internal readonly HashSet<Guid> UsedRequestIds = new HashSet<Guid>();
        internal ulong HighestSequence;
        internal bool FaultLatched;
        internal Guid FaultId;
        internal bool FaultAcknowledged;
        internal bool CleanupCompleted;
        internal bool CleanupFailed;
        internal AbortCleanupDirective CleanupDirective;
        internal OneShotCleanupIntent CleanupIntent;
        internal PostReadTicket FirstPostReadTicket;
        internal PostReadTicket SecondPostReadTicket;
        internal ulong VerifiedNextGenerationNumber;
        internal FixedDigest VerifiedNextGenerationDigest;

        internal void AdvanceGeneration(ProcessGenerationKey key)
        {
            RevokeGenerationCapabilities();
            Key = key;
            ResetGenerationState();
        }

        internal void Retire()
        {
            RevokeGenerationCapabilities();
            Retired = true;
            SessionId = Guid.Empty;
            ActiveMachine = null;
            ActiveHandle = null;
        }

        internal void LatchFault(AbortCleanupDirective directive)
        {
            if (FaultLatched)
            {
                return;
            }

            FaultLatched = true;
            FaultId = Guid.NewGuid();
            FaultAcknowledged = false;
            CleanupFailed = directive == null || directive.RequiresProcessRestart;
            CleanupDirective = directive;
            CleanupCompleted = directive != null && !directive.RequiresExternalCleanup && !directive.RequiresProcessRestart;
        }

        internal void EscalateCleanupFailure(string reason)
        {
            CleanupFailed = true;
            CleanupCompleted = false;
            if (CleanupIntent != null)
            {
                CleanupIntent.Revoke();
            }

            if (CleanupDirective != null && !CleanupDirective.RequiresProcessRestart)
            {
                CleanupDirective = CleanupDirective.EscalateToHaltRestart(reason);
            }
        }

        internal void RevokePostReadTickets()
        {
            if (FirstPostReadTicket != null)
            {
                FirstPostReadTicket.Revoke();
            }

            if (SecondPostReadTicket != null)
            {
                SecondPostReadTicket.Revoke();
            }
        }

        private void RevokeGenerationCapabilities()
        {
            if (ActiveMachine != null && ActiveMachine.PendingIntent != null)
            {
                ActiveMachine.PendingIntent.RevokeForAbort();
            }

            RevokePostReadTickets();
            if (CleanupIntent != null)
            {
                CleanupIntent.Revoke();
            }

            if (ObservationCapability != null)
            {
                ObservationCapability.Revoke();
            }
        }

        private void ResetGenerationState()
        {
            Retired = false;
            SessionId = Guid.NewGuid();
            ObservationCapability = new SessionObservationCapability(SessionId);
            ActionClaimAuthority = new CoordinatorActionClaimAuthority();
            SideEffectAuthority = new CoordinatorSideEffectAuthority();
            PostReadClaimAuthority = new CoordinatorPostReadClaimAuthority();
            CleanupClaimAuthority = new CoordinatorCleanupClaimAuthority();
            ObservationFactory = new TrustedObservationFactory(ObservationCapability);
            ActiveMachine = null;
            ActiveHandle = null;
            ActiveDeadlineMonotonicTicks = 0;
            UsedRequestIds.Clear();
            HighestSequence = 0;
            FaultLatched = false;
            FaultId = Guid.Empty;
            FaultAcknowledged = false;
            CleanupCompleted = false;
            CleanupFailed = false;
            CleanupDirective = null;
            CleanupIntent = null;
            FirstPostReadTicket = null;
            SecondPostReadTicket = null;
            VerifiedNextGenerationNumber = 0;
            VerifiedNextGenerationDigest = null;
        }
    }
}
