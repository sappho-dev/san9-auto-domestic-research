using System;
using System.Threading;

namespace San9AutoDomestic.V8Transaction
{
    internal sealed class CoordinatorActionClaimAuthority
    {
    }

    internal sealed class CoordinatorSideEffectAuthority
    {
    }

    internal enum NativeActionMethod
    {
        BindTargetCandidate = 1,
        OpenOuter = 2,
        OpenSelector = 3,
        Clear = 4,
        NativeFillMax = 5,
        AcceptInner = 6,
        AcceptOuter = 7
    }

    internal enum OneShotExecutionState
    {
        Issued = 0,
        Executing = 1,
        Receipted = 2,
        Consumed = 3,
        Revoked = 4
    }

    internal enum IntentAbortDisposition
    {
        None = 0,
        RevokedBeforeExecution = 1,
        ExecutionInFlight = 2,
        SettledReceiptRevoked = 3,
        AlreadyTerminal = 4
    }

    /// <summary>
    /// One capability for one native-side action attempt.  Claiming changes
    /// Issued to Executing, but only the coordinator-owned invoker may admit
    /// the exact claim/method at the actual callback boundary and issue a
    /// side-effect permit.  Receipt production and consumption are two more
    /// irreversible state changes.
    /// </summary>
    internal sealed class OneShotActionIntent
    {
        private int _state;
        private readonly CoordinatorActionClaimAuthority _claimAuthority;
        private ActionExecutionClaim _activeClaim;

        internal OneShotActionIntent(
            SessionObservationCapability capability,
            CoordinatorActionClaimAuthority claimAuthority,
            Guid coordinatorSessionId,
            ProcessGenerationKey processKey,
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            TransactionStage stage,
            int stageOrdinal,
            int attemptNumber)
        {
            if (capability == null
                || claimAuthority == null
                || coordinatorSessionId == Guid.Empty
                || processKey == null
                || requestId == Guid.Empty
                || requestFingerprint == null
                || sequence == 0
                || stageOrdinal <= 0
                || attemptNumber != 1
                || !ActionStages.IsAction(stage))
            {
                throw new ArgumentException("A one-shot action intent requires exact non-zero bindings.");
            }

            Capability = capability;
            _claimAuthority = claimAuthority;
            CoordinatorSessionId = coordinatorSessionId;
            ProcessId = processKey.ProcessId;
            ProcessCreationUtcTicks = processKey.ProcessCreationUtcTicks;
            GenerationNumber = processKey.GenerationNumber;
            GenerationDigest = processKey.GenerationDigest;
            RequestId = requestId;
            RequestFingerprint = requestFingerprint;
            Sequence = sequence;
            Stage = stage;
            StageOrdinal = stageOrdinal;
            AttemptNumber = attemptNumber;
            Nonce = Guid.NewGuid();
            _state = (int)OneShotExecutionState.Issued;
        }

        internal SessionObservationCapability Capability { get; private set; }

        internal Guid CoordinatorSessionId { get; private set; }

        internal int ProcessId { get; private set; }

        internal long ProcessCreationUtcTicks { get; private set; }

        internal ulong GenerationNumber { get; private set; }

        internal FixedDigest GenerationDigest { get; private set; }

        internal Guid RequestId { get; private set; }

        internal FixedDigest RequestFingerprint { get; private set; }

        internal ulong Sequence { get; private set; }

        internal TransactionStage Stage { get; private set; }

        internal int StageOrdinal { get; private set; }

        internal int AttemptNumber { get; private set; }

        internal Guid Nonce { get; private set; }

        internal OneShotExecutionState State
        {
            get { return (OneShotExecutionState)Volatile.Read(ref _state); }
        }

        internal bool IsConsumed
        {
            get
            {
                OneShotExecutionState state = State;
                return state == OneShotExecutionState.Consumed
                    || state == OneShotExecutionState.Revoked;
            }
        }

        internal bool TryClaimExecution(
            CoordinatorActionClaimAuthority claimAuthority,
            CoordinatorSideEffectAuthority sideEffectAuthority,
            out ActionExecutionClaim claim)
        {
            if (ReferenceEquals(claimAuthority, _claimAuthority)
                && sideEffectAuthority != null
                && Interlocked.CompareExchange(
                ref _state,
                (int)OneShotExecutionState.Executing,
                (int)OneShotExecutionState.Issued) == (int)OneShotExecutionState.Issued)
            {
                claim = new ActionExecutionClaim(this, sideEffectAuthority);
                Interlocked.CompareExchange(ref _activeClaim, claim, null);
                return true;
            }

            claim = null;
            return false;
        }

        internal bool TryProduceReceipt(ActionExecutionClaim claim)
        {
            return claim != null
                && ReferenceEquals(claim.Intent, this)
                && ReferenceEquals(Volatile.Read(ref _activeClaim), claim)
                && claim.ClaimNonce != Guid.Empty
                && Interlocked.CompareExchange(
                    ref _state,
                    (int)OneShotExecutionState.Receipted,
                    (int)OneShotExecutionState.Executing) == (int)OneShotExecutionState.Executing;
        }

        internal bool MatchesActiveClaim(ActionExecutionClaim claim)
        {
            return claim != null
                && ReferenceEquals(claim.Intent, this)
                && ReferenceEquals(Volatile.Read(ref _activeClaim), claim)
                && claim.ClaimNonce != Guid.Empty;
        }

        internal bool TryConsumeReceiptOnce()
        {
            return Interlocked.CompareExchange(
                ref _state,
                (int)OneShotExecutionState.Consumed,
                (int)OneShotExecutionState.Receipted) == (int)OneShotExecutionState.Receipted;
        }

        internal IntentAbortDisposition RevokeForAbort()
        {
            while (true)
            {
                OneShotExecutionState state = State;
                switch (state)
                {
                    case OneShotExecutionState.Issued:
                        if (Interlocked.CompareExchange(
                            ref _state,
                            (int)OneShotExecutionState.Revoked,
                            (int)OneShotExecutionState.Issued) == (int)OneShotExecutionState.Issued)
                        {
                            return IntentAbortDisposition.RevokedBeforeExecution;
                        }

                        break;
                    case OneShotExecutionState.Executing:
                        return IntentAbortDisposition.ExecutionInFlight;
                    case OneShotExecutionState.Receipted:
                        if (Interlocked.CompareExchange(
                            ref _state,
                            (int)OneShotExecutionState.Revoked,
                            (int)OneShotExecutionState.Receipted) == (int)OneShotExecutionState.Receipted)
                        {
                            return IntentAbortDisposition.SettledReceiptRevoked;
                        }

                        break;
                    case OneShotExecutionState.Consumed:
                    case OneShotExecutionState.Revoked:
                        return IntentAbortDisposition.AlreadyTerminal;
                    default:
                        throw new InvalidOperationException("Unknown action intent state.");
                }
            }
        }
    }

    internal sealed class ActionExecutionClaim
    {
        private int _sideEffectEntered;
        private readonly CoordinatorSideEffectAuthority _sideEffectAuthority;

        internal ActionExecutionClaim(
            OneShotActionIntent intent,
            CoordinatorSideEffectAuthority sideEffectAuthority)
        {
            if (intent == null || sideEffectAuthority == null)
            {
                throw new ArgumentNullException(intent == null ? "intent" : "sideEffectAuthority");
            }

            Intent = intent;
            _sideEffectAuthority = sideEffectAuthority;
            ClaimNonce = Guid.NewGuid();
        }

        internal OneShotActionIntent Intent { get; private set; }

        internal Guid ClaimNonce { get; private set; }

        internal bool TryEnterSideEffectOnce(
            CoordinatorSideEffectAuthority authority,
            NativeActionMethod method)
        {
            return authority != null
                && ReferenceEquals(authority, _sideEffectAuthority)
                && ActionStages.GetRequiredMethod(Intent.Stage) == method
                && Interlocked.CompareExchange(ref _sideEffectEntered, 1, 0) == 0;
        }

        internal bool SideEffectEntered
        {
            get { return Volatile.Read(ref _sideEffectEntered) != 0; }
        }
    }

    internal sealed class ActionSideEffectPermit
    {
        private readonly CoordinatorSideEffectAuthority _authority;

        internal ActionSideEffectPermit(
            CoordinatorSideEffectAuthority authority,
            ActionExecutionClaim claim,
            NativeActionMethod method)
        {
            if (authority == null
                || claim == null
                || claim.Intent == null
                || !claim.SideEffectEntered
                || ActionStages.GetRequiredMethod(claim.Intent.Stage) != method)
            {
                throw new ArgumentException("Action side-effect permit binding is invalid.");
            }

            _authority = authority;
            Claim = claim;
            Method = method;
            PermitId = Guid.NewGuid();
            CoordinatorSessionId = claim.Intent.CoordinatorSessionId;
            ProcessId = claim.Intent.ProcessId;
            ProcessCreationUtcTicks = claim.Intent.ProcessCreationUtcTicks;
            GenerationNumber = claim.Intent.GenerationNumber;
            GenerationDigest = claim.Intent.GenerationDigest;
            RequestId = claim.Intent.RequestId;
            RequestFingerprint = claim.Intent.RequestFingerprint;
            Sequence = claim.Intent.Sequence;
            Stage = claim.Intent.Stage;
            StageOrdinal = claim.Intent.StageOrdinal;
            ClaimNonce = claim.ClaimNonce;
        }

        internal ActionExecutionClaim Claim { get; private set; }
        internal NativeActionMethod Method { get; private set; }
        internal Guid PermitId { get; private set; }
        internal Guid CoordinatorSessionId { get; private set; }
        internal int ProcessId { get; private set; }
        internal long ProcessCreationUtcTicks { get; private set; }
        internal ulong GenerationNumber { get; private set; }
        internal FixedDigest GenerationDigest { get; private set; }
        internal Guid RequestId { get; private set; }
        internal FixedDigest RequestFingerprint { get; private set; }
        internal ulong Sequence { get; private set; }
        internal TransactionStage Stage { get; private set; }
        internal int StageOrdinal { get; private set; }
        internal Guid ClaimNonce { get; private set; }

        internal bool IsAuthorized(
            CoordinatorSideEffectAuthority authority,
            ActionExecutionClaim claim,
            NativeActionMethod method)
        {
            OneShotActionIntent intent = claim == null ? null : claim.Intent;
            return authority != null
                && ReferenceEquals(authority, _authority)
                && claim != null
                && intent != null
                && ReferenceEquals(Claim, claim)
                && Method == method
                && PermitId != Guid.Empty
                && claim.SideEffectEntered
                && CoordinatorSessionId == intent.CoordinatorSessionId
                && ProcessId == intent.ProcessId
                && ProcessCreationUtcTicks == intent.ProcessCreationUtcTicks
                && GenerationNumber == intent.GenerationNumber
                && GenerationDigest.Equals(intent.GenerationDigest)
                && RequestId == intent.RequestId
                && RequestFingerprint.Equals(intent.RequestFingerprint)
                && Sequence == intent.Sequence
                && Stage == intent.Stage
                && StageOrdinal == intent.StageOrdinal
                && ClaimNonce == claim.ClaimNonce;
        }
    }

    internal sealed class ActionInvocationResult
    {
        internal ActionInvocationResult(
            bool callbackInvoked,
            TransactionTransition transition,
            string detail)
        {
            CallbackInvoked = callbackInvoked;
            Transition = transition;
            Detail = detail ?? string.Empty;
        }

        internal bool CallbackInvoked { get; private set; }
        internal TransactionTransition Transition { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class ActionClaimResult
    {
        internal ActionClaimResult(
            bool claimed,
            ActionExecutionClaim claim,
            TransactionTransition transition,
            string detail)
        {
            Claimed = claimed;
            Claim = claim;
            Transition = transition;
            Detail = detail ?? string.Empty;
        }

        internal bool Claimed { get; private set; }

        internal ActionExecutionClaim Claim { get; private set; }

        internal TransactionTransition Transition { get; private set; }

        internal string Detail { get; private set; }
    }

    internal sealed class ActionReceipt
    {
        internal ActionReceipt(
            SessionObservationCapability capability,
            ActionSideEffectPermit permit,
            SyntheticStageEvidence evidence,
            bool callbackCompleted,
            string diagnostic)
        {
            if (capability == null || permit == null || permit.Claim == null)
            {
                throw new ArgumentNullException(capability == null ? "capability" : "permit");
            }

            if (callbackCompleted && evidence == null)
            {
                throw new ArgumentNullException("evidence");
            }

            Capability = capability;
            Permit = permit;
            PermitId = permit.PermitId;
            Claim = permit.Claim;
            Intent = permit.Claim.Intent;
            IntentNonce = permit.Claim.Intent.Nonce;
            ClaimNonce = permit.Claim.ClaimNonce;
            Evidence = evidence;
            CallbackCompleted = callbackCompleted;
            Diagnostic = diagnostic ?? string.Empty;
        }

        internal SessionObservationCapability Capability { get; private set; }

        internal ActionSideEffectPermit Permit { get; private set; }

        internal Guid PermitId { get; private set; }

        internal ActionExecutionClaim Claim { get; private set; }

        internal OneShotActionIntent Intent { get; private set; }

        internal Guid IntentNonce { get; private set; }

        internal Guid ClaimNonce { get; private set; }

        internal SyntheticStageEvidence Evidence { get; private set; }

        internal bool CallbackCompleted { get; private set; }

        internal string Diagnostic { get; private set; }
    }

    internal static class ActionStages
    {
        internal static bool IsAction(TransactionStage stage)
        {
            switch (stage)
            {
                case TransactionStage.BindTargetCandidate:
                case TransactionStage.OpenOuter:
                case TransactionStage.OpenSelector:
                case TransactionStage.Clear:
                case TransactionStage.NativeFillMax:
                case TransactionStage.AcceptInner:
                case TransactionStage.AcceptOuter:
                    return true;
                default:
                    return false;
            }
        }

        internal static NativeActionMethod GetRequiredMethod(TransactionStage stage)
        {
            switch (stage)
            {
                case TransactionStage.BindTargetCandidate:
                    return NativeActionMethod.BindTargetCandidate;
                case TransactionStage.OpenOuter:
                    return NativeActionMethod.OpenOuter;
                case TransactionStage.OpenSelector:
                    return NativeActionMethod.OpenSelector;
                case TransactionStage.Clear:
                    return NativeActionMethod.Clear;
                case TransactionStage.NativeFillMax:
                    return NativeActionMethod.NativeFillMax;
                case TransactionStage.AcceptInner:
                    return NativeActionMethod.AcceptInner;
                case TransactionStage.AcceptOuter:
                    return NativeActionMethod.AcceptOuter;
                default:
                    throw new ArgumentOutOfRangeException("stage");
            }
        }
    }
}
