using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace San9AutoDomestic.V8Transaction
{
    /// <summary>
    /// Base for offline evidence only.  A future native bridge must not cast
    /// live observations into this type as a shortcut to authorization.
    /// </summary>
    internal abstract class SyntheticStageEvidence
    {
        private SessionObservationCapability _observationCapability;

        protected SyntheticStageEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            TransactionStage stage,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            ObjectGenerationToken selector)
        {
            if (requestId == Guid.Empty)
            {
                throw new ArgumentException("Evidence requires a request id.", "requestId");
            }

            if (requestFingerprint == null)
            {
                throw new ArgumentNullException("requestFingerprint");
            }

            if (sequence == 0)
            {
                throw new ArgumentOutOfRangeException("sequence");
            }

            if (!Enum.IsDefined(typeof(TransactionStage), stage))
            {
                throw new ArgumentOutOfRangeException("stage");
            }

            if (stageOrdinal <= 0)
            {
                throw new ArgumentOutOfRangeException("stageOrdinal");
            }

            if (observedAtUtcTicks <= 0 || observedAtUtcTicks > DateTime.MaxValue.Ticks)
            {
                throw new ArgumentOutOfRangeException("observedAtUtcTicks");
            }

            if (contextDigest == null)
            {
                throw new ArgumentNullException("contextDigest");
            }

            if (generationDigest == null)
            {
                throw new ArgumentNullException("generationDigest");
            }

            RequestId = requestId;
            RequestFingerprint = requestFingerprint;
            Sequence = sequence;
            Stage = stage;
            StageOrdinal = stageOrdinal;
            ObservedAtUtcTicks = observedAtUtcTicks;
            ContextDigest = contextDigest;
            GenerationDigest = generationDigest;
            CityId = cityId;
            CorpsId = corpsId;
            Root = RequireToken(root, "root");
            Target = RequireToken(target, "target");
            Outer = RequireToken(outer, "outer");
            Selector = RequireToken(selector, "selector");
        }

        public Guid RequestId { get; private set; }

        public FixedDigest RequestFingerprint { get; private set; }

        public ulong Sequence { get; private set; }

        public TransactionStage Stage { get; private set; }

        public int StageOrdinal { get; private set; }

        public long ObservedAtUtcTicks { get; private set; }

        public FixedDigest ContextDigest { get; private set; }

        public FixedDigest GenerationDigest { get; private set; }

        public int CityId { get; private set; }

        public int CorpsId { get; private set; }

        public ObjectGenerationToken Root { get; private set; }

        public ObjectGenerationToken Target { get; private set; }

        public ObjectGenerationToken Outer { get; private set; }

        public ObjectGenerationToken Selector { get; private set; }

        public bool IsLiveEvidence
        {
            get { return false; }
        }

        internal SessionObservationCapability ObservationCapability
        {
            get { return _observationCapability; }
        }

        internal void Seal(SessionObservationCapability capability)
        {
            if (capability == null)
            {
                throw new ArgumentNullException("capability");
            }

            if (_observationCapability != null)
            {
                throw new InvalidOperationException("Evidence is already sealed to a coordinator session.");
            }

            _observationCapability = capability;
        }

        private static ObjectGenerationToken RequireToken(ObjectGenerationToken token, string parameterName)
        {
            if (token == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return token;
        }
    }

    internal sealed class BeginEvidence : SyntheticStageEvidence
    {
        public BeginEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            bool isDirectlyControlled,
            bool nativeCanExecute,
            int availableOfficerCount,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.Begin,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                ObjectGenerationToken.None,
                ObjectGenerationToken.None,
                ObjectGenerationToken.None,
                ObjectGenerationToken.None)
        {
            IsDirectlyControlled = isDirectlyControlled;
            NativeCanExecute = nativeCanExecute;
            AvailableOfficerCount = availableOfficerCount;
            ObservedMoney = observedMoney;
        }

        public bool IsDirectlyControlled { get; private set; }

        public bool NativeCanExecute { get; private set; }

        public int AvailableOfficerCount { get; private set; }

        public int ObservedMoney { get; private set; }
    }

    internal sealed class BindTargetCandidateEvidence : SyntheticStageEvidence
    {
        public BindTargetCandidateEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            bool priorTargetWasEmpty,
            bool exactTargetBound,
            bool nativeCanExecuteRevalidated,
            bool actionApplied,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.BindTargetCandidate,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                ObjectGenerationToken.None,
                ObjectGenerationToken.None)
        {
            PriorTargetWasEmpty = priorTargetWasEmpty;
            ExactTargetBound = exactTargetBound;
            NativeCanExecuteRevalidated = nativeCanExecuteRevalidated;
            ActionApplied = actionApplied;
            ObservedMoney = observedMoney;
        }

        public bool PriorTargetWasEmpty { get; private set; }

        public bool ExactTargetBound { get; private set; }

        public bool NativeCanExecuteRevalidated { get; private set; }

        public bool ActionApplied { get; private set; }

        public int ObservedMoney { get; private set; }
    }

    internal sealed class OpenOuterEvidence : SyntheticStageEvidence
    {
        public OpenOuterEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            int observedNativeCommandId,
            NativeOuterTaskType observedOuterTaskType,
            uint observedOuterTaskVptr,
            bool actionApplied,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.OpenOuter,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                ObjectGenerationToken.None)
        {
            if (!Enum.IsDefined(typeof(NativeOuterTaskType), observedOuterTaskType)
                || observedOuterTaskType == NativeOuterTaskType.Unknown)
            {
                throw new ArgumentOutOfRangeException("observedOuterTaskType");
            }

            ObservedNativeCommandId = observedNativeCommandId;
            ObservedOuterTaskType = observedOuterTaskType;
            ObservedOuterTaskVptr = observedOuterTaskVptr;
            ActionApplied = actionApplied;
            ObservedMoney = observedMoney;
        }

        public int ObservedNativeCommandId { get; private set; }

        public NativeOuterTaskType ObservedOuterTaskType { get; private set; }

        public uint ObservedOuterTaskVptr { get; private set; }

        public bool ActionApplied { get; private set; }

        public int ObservedMoney { get; private set; }
    }

    internal sealed class OpenSelectorEvidence : SyntheticStageEvidence
    {
        private readonly ReadOnlyCollection<int> _sourcePrefixOfficerIds;

        public OpenSelectorEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            ObjectGenerationToken selector,
            FixedDigest sourceDigest,
            int sourceCount,
            IEnumerable<int> sourcePrefixOfficerIds,
            bool actionApplied,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.OpenSelector,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                selector)
        {
            if (sourceDigest == null)
            {
                throw new ArgumentNullException("sourceDigest");
            }

            SourceDigest = sourceDigest;
            SourceCount = sourceCount;
            _sourcePrefixOfficerIds = EvidenceLists.Copy(sourcePrefixOfficerIds, "sourcePrefixOfficerIds");
            ActionApplied = actionApplied;
            ObservedMoney = observedMoney;
        }

        public FixedDigest SourceDigest { get; private set; }

        public int SourceCount { get; private set; }

        public ReadOnlyCollection<int> SourcePrefixOfficerIds
        {
            get { return _sourcePrefixOfficerIds; }
        }

        public bool ActionApplied { get; private set; }

        public int ObservedMoney { get; private set; }
    }

    internal abstract class SelectorStageEvidence : SyntheticStageEvidence
    {
        protected SelectorStageEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            TransactionStage stage,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            ObjectGenerationToken selector,
            FixedDigest sourceDigest,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                stage,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                selector)
        {
            if (sourceDigest == null)
            {
                throw new ArgumentNullException("sourceDigest");
            }

            SourceDigest = sourceDigest;
            ObservedMoney = observedMoney;
        }

        public FixedDigest SourceDigest { get; private set; }

        public int ObservedMoney { get; private set; }
    }

    internal sealed class ClearEvidence : SelectorStageEvidence
    {
        private readonly ReadOnlyCollection<int> _selectedOfficerIds;

        public ClearEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            ObjectGenerationToken selector,
            FixedDigest sourceDigest,
            IEnumerable<int> selectedOfficerIds,
            bool actionApplied,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.Clear,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                selector,
                sourceDigest,
                observedMoney)
        {
            _selectedOfficerIds = EvidenceLists.Copy(selectedOfficerIds, "selectedOfficerIds");
            ActionApplied = actionApplied;
        }

        public ReadOnlyCollection<int> SelectedOfficerIds
        {
            get { return _selectedOfficerIds; }
        }

        public bool ActionApplied { get; private set; }
    }

    internal sealed class NativeFillMaxEvidence : SelectorStageEvidence
    {
        public NativeFillMaxEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            ObjectGenerationToken selector,
            FixedDigest sourceDigest,
            bool actionApplied,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.NativeFillMax,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                selector,
                sourceDigest,
                observedMoney)
        {
            ActionApplied = actionApplied;
        }

        public bool ActionApplied { get; private set; }
    }

    internal sealed class VerifyExactlyExpectedFiveEvidence : SelectorStageEvidence
    {
        private readonly ReadOnlyCollection<int> _selectedOfficerIds;

        public VerifyExactlyExpectedFiveEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            ObjectGenerationToken selector,
            FixedDigest sourceDigest,
            IEnumerable<int> selectedOfficerIds,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.VerifyExactlyExpected5,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                selector,
                sourceDigest,
                observedMoney)
        {
            _selectedOfficerIds = EvidenceLists.Copy(selectedOfficerIds, "selectedOfficerIds");
        }

        public ReadOnlyCollection<int> SelectedOfficerIds
        {
            get { return _selectedOfficerIds; }
        }
    }

    internal sealed class AcceptInnerEvidence : SelectorStageEvidence
    {
        private readonly ReadOnlyCollection<int> _selectedOfficerIds;

        public AcceptInnerEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            ObjectGenerationToken selector,
            FixedDigest sourceDigest,
            IEnumerable<int> selectedOfficerIds,
            bool actionApplied,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.AcceptInner,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                selector,
                sourceDigest,
                observedMoney)
        {
            _selectedOfficerIds = EvidenceLists.Copy(selectedOfficerIds, "selectedOfficerIds");
            ActionApplied = actionApplied;
        }

        public ReadOnlyCollection<int> SelectedOfficerIds
        {
            get { return _selectedOfficerIds; }
        }

        public bool ActionApplied { get; private set; }
    }

    internal abstract class OuterStageEvidence : SyntheticStageEvidence
    {
        private readonly ReadOnlyCollection<int> _workingOfficerIds;

        protected OuterStageEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            TransactionStage stage,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            FixedDigest sourceDigest,
            IEnumerable<int> workingOfficerIds,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                stage,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                ObjectGenerationToken.None)
        {
            if (sourceDigest == null)
            {
                throw new ArgumentNullException("sourceDigest");
            }

            SourceDigest = sourceDigest;
            _workingOfficerIds = EvidenceLists.Copy(workingOfficerIds, "workingOfficerIds");
            ObservedMoney = observedMoney;
        }

        public FixedDigest SourceDigest { get; private set; }

        public ReadOnlyCollection<int> WorkingOfficerIds
        {
            get { return _workingOfficerIds; }
        }

        public int ObservedMoney { get; private set; }
    }

    internal sealed class VerifyWorkingEvidence : OuterStageEvidence
    {
        public VerifyWorkingEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            FixedDigest sourceDigest,
            IEnumerable<int> workingOfficerIds,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.VerifyWorking,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                sourceDigest,
                workingOfficerIds,
                observedMoney)
        {
        }
    }

    internal sealed class AcceptOuterEvidence : OuterStageEvidence
    {
        public AcceptOuterEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int cityId,
            int corpsId,
            ObjectGenerationToken root,
            ObjectGenerationToken target,
            ObjectGenerationToken outer,
            FixedDigest sourceDigest,
            IEnumerable<int> workingOfficerIds,
            bool actionApplied,
            int observedMoney)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.AcceptOuter,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                generationDigest,
                cityId,
                corpsId,
                root,
                target,
                outer,
                sourceDigest,
                workingOfficerIds,
                observedMoney)
        {
            ActionApplied = actionApplied;
        }

        public bool ActionApplied { get; private set; }
    }

    internal sealed class VerifyCommittedEvidence : SyntheticStageEvidence
    {
        public VerifyCommittedEvidence(
            Guid requestId,
            FixedDigest requestFingerprint,
            ulong sequence,
            int stageOrdinal,
            long observedAtUtcTicks,
            FixedDigest contextDigest,
            FixedDigest initialGenerationDigest,
            int cityId,
            int corpsId,
            AuthenticatedStablePostSnapshot snapshot)
            : base(
                requestId,
                requestFingerprint,
                sequence,
                TransactionStage.VerifyCommitted,
                stageOrdinal,
                observedAtUtcTicks,
                contextDigest,
                initialGenerationDigest,
                cityId,
                corpsId,
                ObjectGenerationToken.None,
                ObjectGenerationToken.None,
                ObjectGenerationToken.None,
                ObjectGenerationToken.None)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            Snapshot = snapshot;
        }

        internal AuthenticatedStablePostSnapshot Snapshot { get; private set; }
    }

    internal static class EvidenceLists
    {
        internal static ReadOnlyCollection<int> Copy(IEnumerable<int> values, string parameterName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<int>(new List<int>(values));
        }
    }
}
