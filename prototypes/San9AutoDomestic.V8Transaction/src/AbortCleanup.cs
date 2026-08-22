using System;

namespace San9AutoDomestic.V8Transaction
{
    public enum AbortCleanupKind
    {
        NoMutation = 1,
        ConditionalRestoreTarget = 2,
        CancelSelector = 3,
        CancelOuter = 4,
        DoNotRollbackAfterAccept = 5,
        HaltRestart = 6
    }

    public sealed class AbortCleanupDirective
    {
        internal AbortCleanupDirective(
            AbortCleanupKind kind,
            Guid coordinatorSessionId,
            ProcessGenerationKey processKey,
            SingleCommandRequest request,
            TransactionStage stageAtAbort,
            ObjectGenerationToken rootAtAbort,
            ObjectGenerationToken targetAtAbort,
            ObjectGenerationToken outerAtAbort,
            ObjectGenerationToken selectorAtAbort,
            string reason)
        {
            if (!Enum.IsDefined(typeof(AbortCleanupKind), kind)
                || coordinatorSessionId == Guid.Empty
                || processKey == null
                || request == null
                || !Enum.IsDefined(typeof(TransactionStage), stageAtAbort)
                || rootAtAbort == null
                || targetAtAbort == null
                || outerAtAbort == null
                || selectorAtAbort == null)
            {
                throw new ArgumentException("Cleanup directive bindings are invalid.");
            }

            DirectiveId = Guid.NewGuid();
            Kind = kind;
            CoordinatorSessionId = coordinatorSessionId;
            ProcessId = processKey.ProcessId;
            ProcessCreationUtcTicks = processKey.ProcessCreationUtcTicks;
            GenerationNumber = processKey.GenerationNumber;
            GenerationDigest = processKey.GenerationDigest;
            RequestId = request.RequestId;
            RequestFingerprint = request.RequestFingerprint;
            ContextDigest = request.ContextDigest;
            StageAtAbort = stageAtAbort;
            RootAtAbort = rootAtAbort;
            TargetAtAbort = targetAtAbort;
            OuterAtAbort = outerAtAbort;
            SelectorAtAbort = selectorAtAbort;
            Reason = reason ?? string.Empty;
        }

        public Guid DirectiveId { get; private set; }
        public AbortCleanupKind Kind { get; private set; }
        public Guid RequestId { get; private set; }
        public TransactionStage StageAtAbort { get; private set; }
        public string Reason { get; private set; }

        internal Guid CoordinatorSessionId { get; private set; }
        internal int ProcessId { get; private set; }
        internal long ProcessCreationUtcTicks { get; private set; }
        internal ulong GenerationNumber { get; private set; }
        internal FixedDigest GenerationDigest { get; private set; }
        internal FixedDigest RequestFingerprint { get; private set; }
        internal FixedDigest ContextDigest { get; private set; }
        internal ObjectGenerationToken RootAtAbort { get; private set; }
        internal ObjectGenerationToken TargetAtAbort { get; private set; }
        internal ObjectGenerationToken OuterAtAbort { get; private set; }
        internal ObjectGenerationToken SelectorAtAbort { get; private set; }

        public bool RequiresExternalCleanup
        {
            get
            {
                return Kind == AbortCleanupKind.ConditionalRestoreTarget
                    || Kind == AbortCleanupKind.CancelSelector
                    || Kind == AbortCleanupKind.CancelOuter
                    || Kind == AbortCleanupKind.DoNotRollbackAfterAccept;
            }
        }

        public bool RequiresProcessRestart
        {
            get { return Kind == AbortCleanupKind.HaltRestart; }
        }

        internal AbortCleanupDirective EscalateToHaltRestart(string reason)
        {
            return new AbortCleanupDirective(this, reason);
        }

        private AbortCleanupDirective(AbortCleanupDirective source, string reason)
        {
            DirectiveId = Guid.NewGuid();
            Kind = AbortCleanupKind.HaltRestart;
            CoordinatorSessionId = source.CoordinatorSessionId;
            ProcessId = source.ProcessId;
            ProcessCreationUtcTicks = source.ProcessCreationUtcTicks;
            GenerationNumber = source.GenerationNumber;
            GenerationDigest = source.GenerationDigest;
            RequestId = source.RequestId;
            RequestFingerprint = source.RequestFingerprint;
            ContextDigest = source.ContextDigest;
            StageAtAbort = source.StageAtAbort;
            RootAtAbort = source.RootAtAbort;
            TargetAtAbort = source.TargetAtAbort;
            OuterAtAbort = source.OuterAtAbort;
            SelectorAtAbort = source.SelectorAtAbort;
            Reason = reason ?? string.Empty;
        }
    }
}
