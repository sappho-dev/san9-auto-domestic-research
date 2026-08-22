using System;

namespace San9AutoDomestic.V8Transaction
{
    public static class V8SafetyBoundary
    {
        public const bool LiveBusinessAuthorizationCompiled = false;
        public const string Mode = "OFFLINE_SYNTHETIC_NON_AUTHORIZING";
    }

    public enum TransactionStage
    {
        Begin = 1,
        BindTargetCandidate = 2,
        OpenOuter = 3,
        OpenSelector = 4,
        Clear = 5,
        NativeFillMax = 6,
        VerifyExactlyExpected5 = 7,
        AcceptInner = 8,
        VerifyWorking = 9,
        AcceptOuter = 10,
        VerifyCommitted = 11
    }

    public enum TransactionState
    {
        AwaitBegin = 1,
        AwaitBindTargetCandidate = 2,
        AwaitOpenOuter = 3,
        AwaitOpenSelector = 4,
        AwaitClear = 5,
        AwaitNativeFillMax = 6,
        AwaitVerifyExactlyExpected5 = 7,
        AwaitAcceptInner = 8,
        AwaitVerifyWorking = 9,
        AwaitAcceptOuter = 10,
        AwaitVerifyCommitted = 11,
        SyntheticCommittedVerified = 12,
        SkipBeforeMutation = 13,
        AbortUncertain = 14
    }

    public enum TransactionOutcome
    {
        InProgress = 1,
        SyntheticCommittedVerified = 2,
        SkipBeforeMutation = 3,
        AbortUncertain = 4
    }

    public enum BusinessSkipReason
    {
        None = 0,
        DelegatedCity = 1,
        NativeGreyedOut = 2,
        InsufficientOfficers = 3,
        InsufficientFundsOrReserve = 4
    }

    public enum AbortUncertainReason
    {
        None = 0,
        NullOrMalformedEvidence = 1,
        RequestBindingMismatch = 2,
        UnexpectedStageOrReplay = 3,
        StageOrdinalMismatch = 4,
        ContextDigestMismatch = 7,
        GenerationDigestMismatch = 8,
        CityOrCorpsMismatch = 9,
        InvalidBusinessObservation = 10,
        TargetBindingMismatch = 11,
        ObjectMissingDestroyedOrReused = 12,
        CommandDescriptorMismatch = 13,
        NativeCanExecuteChanged = 14,
        SourceDigestMismatch = 15,
        CandidateListMismatch = 16,
        SelectionListMismatch = 17,
        NativeStageActionNotApplied = 18,
        SelectorLifecycleMismatch = 19,
        WorkingListMismatch = 20,
        MoneyChangedDuringTransaction = 21,
        PartialCommitObserved = 22,
        ResultGenerationNotFresh = 24,
        ExternalTimeout = 25,
        UntrustedObservation = 26,
        ActionIntentRequired = 27,
        ActionIntentReplay = 28,
        ActionReceiptBindingMismatch = 29,
        ActionReceiptReplay = 30,
        ActionCallbackFailed = 31,
        PostSnapshotUnauthenticated = 32,
        PostSnapshotUnstable = 33,
        PostSnapshotBindingMismatch = 34,
        SourceCountChanged = 35,
        TrustedClockFailure = 36,
        ActionExecutionClaimReplay = 37,
        PostReadTicketMismatch = 38,
        PostReadTicketReplay = 39,
        CleanupAuthenticationFailed = 40
    }

    internal enum ObjectKind
    {
        None = 0,
        RootController = 1,
        CityTarget = 2,
        OuterTask = 3,
        InnerSelector = 4
    }

    /// <summary>
    /// A synthetic identity plus generation.  Numeric identities model
    /// opaque native tokens; they are never interpreted as process pointers.
    /// </summary>
    internal sealed class ObjectGenerationToken : IEquatable<ObjectGenerationToken>
    {
        private static readonly ObjectGenerationToken NoneToken =
            new ObjectGenerationToken(ObjectKind.None, 0, 0, false, true);

        public ObjectGenerationToken(ObjectKind kind, ulong identity, ulong generation, bool isAlive)
            : this(kind, identity, generation, isAlive, false)
        {
        }

        private ObjectGenerationToken(
            ObjectKind kind,
            ulong identity,
            ulong generation,
            bool isAlive,
            bool creatingNone)
        {
            if (kind == ObjectKind.None)
            {
                if (!creatingNone || identity != 0 || generation != 0 || isAlive)
                {
                    throw new ArgumentException("Use ObjectGenerationToken.None for an absent object.", "kind");
                }
            }
            else if (!Enum.IsDefined(typeof(ObjectKind), kind)
                || identity == 0
                || generation == 0)
            {
                throw new ArgumentException("A present object requires a known kind and non-zero identity/generation.");
            }

            Kind = kind;
            Identity = identity;
            Generation = generation;
            IsAlive = isAlive;
        }

        public static ObjectGenerationToken None
        {
            get { return NoneToken; }
        }

        public ObjectKind Kind { get; private set; }

        public ulong Identity { get; private set; }

        public ulong Generation { get; private set; }

        public bool IsAlive { get; private set; }

        public bool IsAbsent
        {
            get { return Kind == ObjectKind.None; }
        }

        public bool Equals(ObjectGenerationToken other)
        {
            return !ReferenceEquals(other, null)
                && Kind == other.Kind
                && Identity == other.Identity
                && Generation == other.Generation
                && IsAlive == other.IsAlive;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ObjectGenerationToken);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ Identity.GetHashCode();
                hash = (hash * 397) ^ Generation.GetHashCode();
                hash = (hash * 397) ^ IsAlive.GetHashCode();
                return hash;
            }
        }
    }

    public sealed class TransactionTransition
    {
        internal TransactionTransition(
            bool accepted,
            TransactionState state,
            TransactionOutcome outcome,
            TransactionStage? nextExpectedStage,
            BusinessSkipReason skipReason,
            AbortUncertainReason abortReason,
            string detail,
            bool mutationStarted,
            int innerAcceptAttempts,
            int outerAcceptAttempts,
            AbortCleanupDirective cleanupDirective)
        {
            Accepted = accepted;
            State = state;
            Outcome = outcome;
            NextExpectedStage = nextExpectedStage;
            SkipReason = skipReason;
            AbortReason = abortReason;
            Detail = detail ?? string.Empty;
            MutationStarted = mutationStarted;
            InnerAcceptAttempts = innerAcceptAttempts;
            OuterAcceptAttempts = outerAcceptAttempts;
            CleanupDirective = cleanupDirective;
        }

        public bool Accepted { get; private set; }

        public TransactionState State { get; private set; }

        public TransactionOutcome Outcome { get; private set; }

        public TransactionStage? NextExpectedStage { get; private set; }

        public BusinessSkipReason SkipReason { get; private set; }

        public AbortUncertainReason AbortReason { get; private set; }

        public string Detail { get; private set; }

        public bool MutationStarted { get; private set; }

        public int InnerAcceptAttempts { get; private set; }

        public int OuterAcceptAttempts { get; private set; }

        public AbortCleanupDirective CleanupDirective { get; private set; }

        public bool IsTerminal
        {
            get { return Outcome != TransactionOutcome.InProgress; }
        }

        public bool RetryConfirmationAllowed
        {
            get { return false; }
        }

        public bool CommitAuthorized
        {
            get { return false; }
        }
    }
}
