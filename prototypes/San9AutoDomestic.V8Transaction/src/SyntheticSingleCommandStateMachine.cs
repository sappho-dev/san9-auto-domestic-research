using System;
using System.Collections.Generic;

namespace San9AutoDomestic.V8Transaction
{
    /// <summary>
    /// Pure synthetic transaction validator.  It calls no native function,
    /// sends no Windows message, opens no process, and can never authorize a
    /// live mutation even when every synthetic stage succeeds.
    /// </summary>
    internal sealed class SyntheticSingleCommandStateMachine
    {
        private readonly SingleCommandRequest _request;
        private readonly ProcessGenerationKey _processKey;
        private readonly SessionObservationCapability _observationCapability;
        private readonly CoordinatorActionClaimAuthority _actionClaimAuthority;
        private readonly Guid _coordinatorSessionId;
        private readonly HashSet<TransactionStage> _issuedActionStages;
        private TransactionState _state;
        private TransactionOutcome _outcome;
        private BusinessSkipReason _skipReason;
        private AbortUncertainReason _abortReason;
        private string _detail;
        private int _nextOrdinal;
        private bool _mutationStarted;
        private int _innerAcceptAttempts;
        private int _outerAcceptAttempts;
        private int _initialMoney;
        private ObjectGenerationToken _root;
        private ObjectGenerationToken _target;
        private ObjectGenerationToken _outer;
        private ObjectGenerationToken _selector;
        private FixedDigest _sourceDigest;
        private int _beginAvailableOfficerCount;
        private OneShotActionIntent _pendingIntent;
        private bool _actionReceiptAuthorized;
        private AbortCleanupDirective _cleanupDirective;
        private ulong _verifiedNextGenerationNumber;
        private FixedDigest _verifiedNextGenerationDigest;

        internal SyntheticSingleCommandStateMachine(
            SingleCommandRequest request,
            ProcessGenerationKey processKey,
            SessionObservationCapability observationCapability,
            CoordinatorActionClaimAuthority actionClaimAuthority,
            Guid coordinatorSessionId)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (processKey == null)
            {
                throw new ArgumentNullException("processKey");
            }

            if (observationCapability == null
                || actionClaimAuthority == null
                || coordinatorSessionId == Guid.Empty)
            {
                throw new ArgumentException("A live coordinator session binding is required.");
            }

            _request = request;
            _processKey = processKey;
            _observationCapability = observationCapability;
            _actionClaimAuthority = actionClaimAuthority;
            _coordinatorSessionId = coordinatorSessionId;
            _issuedActionStages = new HashSet<TransactionStage>();
            _state = TransactionState.AwaitBegin;
            _outcome = TransactionOutcome.InProgress;
            _skipReason = BusinessSkipReason.None;
            _abortReason = AbortUncertainReason.None;
            _detail = "Awaiting synthetic Begin evidence.";
            _nextOrdinal = 1;
            _root = ObjectGenerationToken.None;
            _target = ObjectGenerationToken.None;
            _outer = ObjectGenerationToken.None;
            _selector = ObjectGenerationToken.None;
        }

        public SingleCommandRequest Request
        {
            get { return _request; }
        }

        public TransactionState State
        {
            get { return _state; }
        }

        public TransactionOutcome Outcome
        {
            get { return _outcome; }
        }

        public bool IsTerminal
        {
            get { return _outcome != TransactionOutcome.InProgress; }
        }

        public bool AuthorizesLiveMutation
        {
            get { return false; }
        }

        public bool RetryConfirmationAllowed
        {
            get { return false; }
        }

        public int InnerAcceptAttempts
        {
            get { return _innerAcceptAttempts; }
        }

        public int OuterAcceptAttempts
        {
            get { return _outerAcceptAttempts; }
        }

        internal TransactionStage ExpectedStage
        {
            get { return GetExpectedStage(); }
        }

        internal int ExpectedStageOrdinal
        {
            get { return _nextOrdinal; }
        }

        internal OneShotActionIntent PendingIntent
        {
            get { return _pendingIntent; }
        }

        internal ulong VerifiedNextGenerationNumber
        {
            get { return _verifiedNextGenerationNumber; }
        }

        internal FixedDigest VerifiedNextGenerationDigest
        {
            get { return _verifiedNextGenerationDigest; }
        }

        internal TransactionTransition AdvanceObservation(SyntheticStageEvidence evidence)
        {
            if (IsTerminal)
            {
                return Snapshot(false, "The transaction is terminal; late or replayed evidence was ignored.");
            }

            if (evidence == null)
            {
                return Abort(
                    AbortUncertainReason.NullOrMalformedEvidence,
                    "Null evidence cannot advance a transaction.");
            }

            if (!ReferenceEquals(evidence.ObservationCapability, _observationCapability))
            {
                return Abort(
                    AbortUncertainReason.UntrustedObservation,
                    "Evidence is not sealed by this coordinator session.");
            }

            if (!EvidenceBindsRequest(evidence))
            {
                return Abort(
                    AbortUncertainReason.RequestBindingMismatch,
                    "Request id, fingerprint, or sequence does not match the active transaction.");
            }

            MarkPossibleMutation(evidence);

            TransactionStage expectedStage = GetExpectedStage();
            if (ActionStages.IsAction(expectedStage) && !_actionReceiptAuthorized)
            {
                return Abort(
                    AbortUncertainReason.ActionIntentRequired,
                    "Action evidence cannot advance without a consumed one-shot intent.");
            }

            if (evidence.Stage != expectedStage)
            {
                return Abort(
                    AbortUncertainReason.UnexpectedStageOrReplay,
                    "Expected " + expectedStage + " but received " + evidence.Stage + ".");
            }

            if (evidence.StageOrdinal != _nextOrdinal)
            {
                return Abort(
                    AbortUncertainReason.StageOrdinalMismatch,
                    "Expected stage ordinal " + _nextOrdinal + " but received " + evidence.StageOrdinal + ".");
            }

            if (!evidence.ContextDigest.Equals(_request.ContextDigest))
            {
                return Abort(
                    AbortUncertainReason.ContextDigestMismatch,
                    "Process/scenario/turn/phase/player context changed.");
            }

            if (!evidence.GenerationDigest.Equals(_request.GenerationDigest))
            {
                return Abort(
                    AbortUncertainReason.GenerationDigestMismatch,
                    "The initial transaction generation binding changed.");
            }

            if (evidence.CityId != _request.CityId || evidence.CorpsId != _request.CorpsId)
            {
                return Abort(
                    AbortUncertainReason.CityOrCorpsMismatch,
                    "Observed city/corps does not match the atomic request.");
            }

            switch (expectedStage)
            {
                case TransactionStage.Begin:
                    return AdvanceBegin(evidence as BeginEvidence);
                case TransactionStage.BindTargetCandidate:
                    return AdvanceBindTarget(evidence as BindTargetCandidateEvidence);
                case TransactionStage.OpenOuter:
                    return AdvanceOpenOuter(evidence as OpenOuterEvidence);
                case TransactionStage.OpenSelector:
                    return AdvanceOpenSelector(evidence as OpenSelectorEvidence);
                case TransactionStage.Clear:
                    return AdvanceClear(evidence as ClearEvidence);
                case TransactionStage.NativeFillMax:
                    return AdvanceNativeFillMax(evidence as NativeFillMaxEvidence);
                case TransactionStage.VerifyExactlyExpected5:
                    return AdvanceVerifyExactlyFive(evidence as VerifyExactlyExpectedFiveEvidence);
                case TransactionStage.AcceptInner:
                    return AdvanceAcceptInner(evidence as AcceptInnerEvidence);
                case TransactionStage.VerifyWorking:
                    return AdvanceVerifyWorking(evidence as VerifyWorkingEvidence);
                case TransactionStage.AcceptOuter:
                    return AdvanceAcceptOuter(evidence as AcceptOuterEvidence);
                case TransactionStage.VerifyCommitted:
                    return AdvanceVerifyCommitted(evidence as VerifyCommittedEvidence);
                default:
                    return Abort(
                        AbortUncertainReason.NullOrMalformedEvidence,
                        "The state maps to no known V8 stage.");
            }
        }

        internal TransactionTransition Snapshot()
        {
            return Snapshot(false, null);
        }

        internal bool TryIssueActionIntent(
            Guid coordinatorSessionId,
            out OneShotActionIntent intent,
            out TransactionTransition failure)
        {
            intent = null;
            failure = null;
            if (IsTerminal)
            {
                failure = Snapshot(false, "A terminal transaction cannot issue an action intent.");
                return false;
            }

            TransactionStage stage = GetExpectedStage();
            if (!ActionStages.IsAction(stage))
            {
                failure = Abort(
                    AbortUncertainReason.UnexpectedStageOrReplay,
                    "The expected stage is an observation, not an action.");
                return false;
            }

            if (_pendingIntent != null || _issuedActionStages.Contains(stage))
            {
                failure = Abort(
                    AbortUncertainReason.ActionIntentReplay,
                    "An action intent for this stage was already issued and is never reissued.");
                return false;
            }

            _issuedActionStages.Add(stage);
            if (stage == TransactionStage.AcceptInner)
            {
                _innerAcceptAttempts++;
            }
            else if (stage == TransactionStage.AcceptOuter)
            {
                _outerAcceptAttempts++;
            }

            // Once an action capability is handed to a callback, its side
            // effect is uncertain until a receipt is consumed.
            _mutationStarted = true;
            _pendingIntent = new OneShotActionIntent(
                _observationCapability,
                _actionClaimAuthority,
                coordinatorSessionId,
                _processKey,
                _request.RequestId,
                _request.RequestFingerprint,
                _request.Sequence,
                stage,
                _nextOrdinal,
                1);
            intent = _pendingIntent;
            return true;
        }

        internal bool MatchesPendingIntent(OneShotActionIntent intent)
        {
            return intent != null
                && ReferenceEquals(intent, _pendingIntent)
                && intent.RequestId == _request.RequestId
                && intent.Sequence == _request.Sequence
                && intent.RequestFingerprint.Equals(_request.RequestFingerprint)
                && intent.Stage == GetExpectedStage()
                && intent.StageOrdinal == _nextOrdinal
                && intent.AttemptNumber == 1;
        }

        internal TransactionTransition ConsumeActionReceipt(
            OneShotActionIntent intent,
            SyntheticStageEvidence evidence)
        {
            if (!MatchesPendingIntent(intent))
            {
                return Abort(
                    AbortUncertainReason.ActionReceiptBindingMismatch,
                    "Receipt does not match the pending one-shot capability.");
            }

            _pendingIntent = null;
            _actionReceiptAuthorized = true;
            try
            {
                return AdvanceObservation(evidence);
            }
            finally
            {
                _actionReceiptAuthorized = false;
            }
        }

        internal TransactionTransition ForceAbort(AbortUncertainReason reason, string detail)
        {
            if (reason == AbortUncertainReason.None)
            {
                throw new ArgumentOutOfRangeException("reason");
            }

            return IsTerminal ? Snapshot(false, detail) : Abort(reason, detail);
        }

        internal TransactionTransition ForceHaltRestart(
            AbortUncertainReason reason,
            string detail)
        {
            TransactionTransition transition = ForceAbort(reason, detail);
            if (_outcome == TransactionOutcome.AbortUncertain
                && _cleanupDirective != null
                && !_cleanupDirective.RequiresProcessRestart)
            {
                _cleanupDirective = _cleanupDirective.EscalateToHaltRestart(detail);
                transition = Snapshot(false, null);
            }

            return transition;
        }

        private TransactionTransition AdvanceBegin(BeginEvidence evidence)
        {
            if (evidence == null
                || evidence.AvailableOfficerCount < 0
                || evidence.AvailableOfficerCount > SingleCommandRequest.OfficerCount
                || evidence.ObservedMoney < 0
                || evidence.ObservedMoney > SingleCommandRequest.GameMoneyMaximum)
            {
                return Abort(
                    AbortUncertainReason.InvalidBusinessObservation,
                    "Begin evidence contains impossible officer or money values.");
            }

            if (!evidence.IsDirectlyControlled)
            {
                return Skip(BusinessSkipReason.DelegatedCity, "City is delegated at the final pre-mutation gate.");
            }

            if (!evidence.NativeCanExecute)
            {
                return Skip(BusinessSkipReason.NativeGreyedOut, "The native command is greyed out.");
            }

            if (evidence.AvailableOfficerCount < _request.Descriptor.RequiredOfficerCount)
            {
                return Skip(
                    BusinessSkipReason.InsufficientOfficers,
                    "Fewer than exactly five native candidates are available.");
            }

            int expectedCost = _request.Descriptor.ExpectedCost;
            if (evidence.ObservedMoney < expectedCost
                || evidence.ObservedMoney - expectedCost < _request.ReserveMoney)
            {
                return Skip(
                    BusinessSkipReason.InsufficientFundsOrReserve,
                    "The command would violate the frozen reserve-money constraint.");
            }

            _initialMoney = evidence.ObservedMoney;
            _beginAvailableOfficerCount = evidence.AvailableOfficerCount;
            return MoveTo(
                TransactionState.AwaitBindTargetCandidate,
                "Pre-mutation business gates passed; no mutation has occurred.");
        }

        private TransactionTransition AdvanceBindTarget(BindTargetCandidateEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.BindTargetCandidate);
            }

            if (evidence.ObservedMoney != _initialMoney)
            {
                return Abort(
                    AbortUncertainReason.MoneyChangedDuringTransaction,
                    "Money changed after Begin; the original validation cannot be reused.");
            }

            if (!evidence.ActionApplied)
            {
                return Abort(
                    AbortUncertainReason.NativeStageActionNotApplied,
                    "The target-binding action did not report completion.");
            }

            if (!evidence.PriorTargetWasEmpty || !evidence.ExactTargetBound)
            {
                return Abort(
                    AbortUncertainReason.TargetBindingMismatch,
                    "Target binding was not an empty-to-exact-city transition.");
            }

            if (!evidence.NativeCanExecuteRevalidated)
            {
                return Abort(
                    AbortUncertainReason.NativeCanExecuteChanged,
                    "Native CanExecute changed between preflight and dispatch.");
            }

            if (!IsLiveKind(evidence.Root, ObjectKind.RootController)
                || !IsLiveKind(evidence.Target, ObjectKind.CityTarget)
                || !evidence.Outer.IsAbsent
                || !evidence.Selector.IsAbsent)
            {
                return Abort(
                    AbortUncertainReason.ObjectMissingDestroyedOrReused,
                    "Root/target identity or lifecycle is invalid at target binding.");
            }

            _root = evidence.Root;
            _target = evidence.Target;
            return MoveTo(TransactionState.AwaitOpenOuter, "Exact target candidate is synthetically bound.");
        }

        private TransactionTransition AdvanceOpenOuter(OpenOuterEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.OpenOuter);
            }

            AbortUncertainReason objectFailure;
            if (!ValidateRootTarget(evidence, out objectFailure)
                || !IsLiveKind(evidence.Outer, ObjectKind.OuterTask)
                || !evidence.Selector.IsAbsent)
            {
                return Abort(
                    objectFailure == AbortUncertainReason.None
                        ? AbortUncertainReason.ObjectMissingDestroyedOrReused
                        : objectFailure,
                    "Root, target, or outer object changed while opening the command task.");
            }

            if (!evidence.ActionApplied)
            {
                return Abort(
                    AbortUncertainReason.NativeStageActionNotApplied,
                    "The outer task did not open.");
            }

            if (evidence.ObservedNativeCommandId != _request.Descriptor.NativeCommandId
                || evidence.ObservedOuterTaskType != _request.Descriptor.OuterTaskType
                || evidence.ObservedOuterTaskVptr != _request.Descriptor.OuterTaskVptr)
            {
                return Abort(
                    AbortUncertainReason.CommandDescriptorMismatch,
                    "The typed native id/task type/vptr observation does not match the frozen descriptor.");
            }

            if (evidence.ObservedMoney != _initialMoney)
            {
                return Abort(
                    AbortUncertainReason.MoneyChangedDuringTransaction,
                    "Money changed before outer confirmation.");
            }

            _outer = evidence.Outer;
            return MoveTo(TransactionState.AwaitOpenSelector, "Expected outer task is synthetically active.");
        }

        private TransactionTransition AdvanceOpenSelector(OpenSelectorEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.OpenSelector);
            }

            if (!ValidateRootTargetOuter(evidence)
                || !IsLiveKind(evidence.Selector, ObjectKind.InnerSelector))
            {
                return Abort(
                    AbortUncertainReason.ObjectMissingDestroyedOrReused,
                    "Outer or selector object identity/generation is not exact.");
            }

            if (!evidence.ActionApplied)
            {
                return Abort(
                    AbortUncertainReason.NativeStageActionNotApplied,
                    "The native selector did not open.");
            }

            if (evidence.ObservedMoney != _initialMoney)
            {
                return Abort(
                    AbortUncertainReason.MoneyChangedDuringTransaction,
                    "Money changed after target binding.");
            }

            if (evidence.SourceCount != _beginAvailableOfficerCount
                || evidence.SourceCount < _request.Descriptor.RequiredOfficerCount
                || evidence.SourceCount > SingleCommandRequest.OfficerCount)
            {
                return Abort(
                    AbortUncertainReason.SourceCountChanged,
                    "Selector source count is outside bounds or differs from the final Begin gate.");
            }

            if (!SequenceEquals(evidence.SourcePrefixOfficerIds, _request.OfficerIds))
            {
                return Abort(
                    AbortUncertainReason.CandidateListMismatch,
                    "The native source prefix is not the exact ordered five bound by the request.");
            }

            _selector = evidence.Selector;
            _sourceDigest = evidence.SourceDigest;
            return MoveTo(TransactionState.AwaitClear, "Exact selector and source digest are bound.");
        }

        private TransactionTransition AdvanceClear(ClearEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.Clear);
            }

            TransactionTransition invalid = ValidateSelectorStage(evidence);
            if (invalid != null)
            {
                return invalid;
            }

            if (!evidence.ActionApplied)
            {
                return Abort(
                    AbortUncertainReason.NativeStageActionNotApplied,
                    "Native Clear was not applied.");
            }

            if (evidence.SelectedOfficerIds.Count != 0)
            {
                return Abort(
                    AbortUncertainReason.SelectionListMismatch,
                    "Clear did not produce an empty native selection.");
            }

            return MoveTo(TransactionState.AwaitNativeFillMax, "Native selection is empty after Clear.");
        }

        private TransactionTransition AdvanceNativeFillMax(NativeFillMaxEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.NativeFillMax);
            }

            TransactionTransition invalid = ValidateSelectorStage(evidence);
            if (invalid != null)
            {
                return invalid;
            }

            if (!evidence.ActionApplied)
            {
                return Abort(
                    AbortUncertainReason.NativeStageActionNotApplied,
                    "Native prefix-fill was not applied.");
            }

            return MoveTo(
                TransactionState.AwaitVerifyExactlyExpected5,
                "Native prefix-fill completed; exact five must now be independently verified.");
        }

        private TransactionTransition AdvanceVerifyExactlyFive(VerifyExactlyExpectedFiveEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.VerifyExactlyExpected5);
            }

            TransactionTransition invalid = ValidateSelectorStage(evidence);
            if (invalid != null)
            {
                return invalid;
            }

            if (!SequenceEquals(evidence.SelectedOfficerIds, _request.OfficerIds))
            {
                return Abort(
                    AbortUncertainReason.SelectionListMismatch,
                    "Native fill did not select exactly the expected ordered five.");
            }

            return MoveTo(TransactionState.AwaitAcceptInner, "Exactly the expected five are selected.");
        }

        private TransactionTransition AdvanceAcceptInner(AcceptInnerEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.AcceptInner);
            }

            TransactionTransition invalid = ValidateSelectorStage(evidence);
            if (invalid != null)
            {
                return invalid;
            }

            if (_innerAcceptAttempts != 1 || !evidence.ActionApplied)
            {
                return Abort(
                    AbortUncertainReason.NativeStageActionNotApplied,
                    "Inner confirmation must be attempted exactly once.");
            }

            if (!SequenceEquals(evidence.SelectedOfficerIds, _request.OfficerIds))
            {
                return Abort(
                    AbortUncertainReason.SelectionListMismatch,
                    "The exact five changed immediately before inner confirmation.");
            }

            return MoveTo(
                TransactionState.AwaitVerifyWorking,
                "Inner confirmation was attempted once; its selector token is now discarded.");
        }

        private TransactionTransition AdvanceVerifyWorking(VerifyWorkingEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.VerifyWorking);
            }

            if (!ValidateRootTargetOuter(evidence))
            {
                return Abort(
                    AbortUncertainReason.ObjectMissingDestroyedOrReused,
                    "Outer object was destroyed or reused after inner confirmation.");
            }

            if (!evidence.Selector.IsAbsent)
            {
                return Abort(
                    AbortUncertainReason.SelectorLifecycleMismatch,
                    "VerifyWorking carried a cached selector; it must be discarded after AcceptInner.");
            }

            if (!_sourceDigest.Equals(evidence.SourceDigest))
            {
                return Abort(
                    AbortUncertainReason.SourceDigestMismatch,
                    "Source digest changed across the inner modal boundary.");
            }

            if (evidence.ObservedMoney != _initialMoney)
            {
                return Abort(
                    AbortUncertainReason.MoneyChangedDuringTransaction,
                    "Money changed before outer confirmation.");
            }

            if (!SequenceEquals(evidence.WorkingOfficerIds, _request.OfficerIds))
            {
                return Abort(
                    AbortUncertainReason.WorkingListMismatch,
                    "Outer working list is not exactly the expected five.");
            }

            _selector = ObjectGenerationToken.None;
            return MoveTo(TransactionState.AwaitAcceptOuter, "Outer working list is independently verified.");
        }

        private TransactionTransition AdvanceAcceptOuter(AcceptOuterEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.AcceptOuter);
            }

            if (!ValidateRootTargetOuter(evidence))
            {
                return Abort(
                    AbortUncertainReason.ObjectMissingDestroyedOrReused,
                    "Outer task identity/generation changed before confirmation.");
            }

            if (!evidence.Selector.IsAbsent)
            {
                return Abort(
                    AbortUncertainReason.SelectorLifecycleMismatch,
                    "A selector token was reused after inner confirmation.");
            }

            if (!_sourceDigest.Equals(evidence.SourceDigest)
                || !SequenceEquals(evidence.WorkingOfficerIds, _request.OfficerIds))
            {
                return Abort(
                    AbortUncertainReason.WorkingListMismatch,
                    "The source or working list changed before outer confirmation.");
            }

            if (evidence.ObservedMoney != _initialMoney)
            {
                return Abort(
                    AbortUncertainReason.MoneyChangedDuringTransaction,
                    "Money changed before outer confirmation.");
            }

            if (_outerAcceptAttempts != 1 || !evidence.ActionApplied)
            {
                return Abort(
                    AbortUncertainReason.NativeStageActionNotApplied,
                    "Outer confirmation must be attempted exactly once.");
            }

            return MoveTo(
                TransactionState.AwaitVerifyCommitted,
                "Outer confirmation was attempted once; stale outer/selector pointers are now forbidden.");
        }

        private TransactionTransition AdvanceVerifyCommitted(VerifyCommittedEvidence evidence)
        {
            if (evidence == null)
            {
                return MalformedForStage(TransactionStage.VerifyCommitted);
            }

            if (!evidence.Root.IsAbsent
                || !evidence.Target.IsAbsent
                || !evidence.Outer.IsAbsent
                || !evidence.Selector.IsAbsent)
            {
                return Abort(
                    AbortUncertainReason.ObjectMissingDestroyedOrReused,
                    "Post-commit verification must use a fresh snapshot, not stale task objects.");
            }

            AuthenticatedStablePostSnapshot snapshot = evidence.Snapshot;
            if (snapshot == null
                || !ReferenceEquals(snapshot.ObservationCapability, _observationCapability))
            {
                return Abort(
                    AbortUncertainReason.PostSnapshotUnauthenticated,
                    "Post-state snapshot is not authenticated by this coordinator session.");
            }

            PostReadTicket firstTicket = snapshot.FirstReceipt.Ticket;
            PostReadTicket secondTicket = snapshot.SecondReceipt.Ticket;
            if (firstTicket.Ordinal != 1
                || secondTicket.Ordinal != 2
                || firstTicket.TicketId == secondTicket.TicketId
                || firstTicket.CoordinatorSessionId != _coordinatorSessionId
                || secondTicket.CoordinatorSessionId != _coordinatorSessionId
                || firstTicket.ProcessId != _processKey.ProcessId
                || secondTicket.ProcessId != _processKey.ProcessId
                || firstTicket.ProcessCreationUtcTicks != _processKey.ProcessCreationUtcTicks
                || secondTicket.ProcessCreationUtcTicks != _processKey.ProcessCreationUtcTicks
                || firstTicket.InitialGenerationNumber != _processKey.GenerationNumber
                || secondTicket.InitialGenerationNumber != _processKey.GenerationNumber
                || !firstTicket.InitialGenerationDigest.Equals(_processKey.GenerationDigest)
                || !secondTicket.InitialGenerationDigest.Equals(_processKey.GenerationDigest)
                || firstTicket.RequestId != _request.RequestId
                || secondTicket.RequestId != _request.RequestId
                || firstTicket.Sequence != _request.Sequence
                || secondTicket.Sequence != _request.Sequence
                || !firstTicket.RequestFingerprint.Equals(_request.RequestFingerprint)
                || !secondTicket.RequestFingerprint.Equals(_request.RequestFingerprint))
            {
                return Abort(
                    AbortUncertainReason.PostReadTicketMismatch,
                    "Post-state A/B tickets are not independently bound to this process generation and request.");
            }

            if (!snapshot.IsStable)
            {
                return Abort(
                    AbortUncertainReason.PostSnapshotUnstable,
                    "Independent post-state reads do not have the same canonical digest.");
            }

            PostStateRead read = snapshot.Read;
            if (read.GenerationNumber <= _processKey.GenerationNumber
                || read.GenerationDigest.Equals(_request.GenerationDigest))
            {
                return Abort(
                    AbortUncertainReason.ResultGenerationNotFresh,
                    "Post-commit generation is not strictly newer than the transaction generation.");
            }

            if (read.ProcessId != _processKey.ProcessId
                || read.ProcessCreationUtcTicks != _processKey.ProcessCreationUtcTicks
                || !read.ContextDigest.Equals(_request.ContextDigest)
                || read.RequestId != _request.RequestId
                || read.Sequence != _request.Sequence
                || !read.RequestFingerprint.Equals(_request.RequestFingerprint)
                || read.CityId != _request.CityId
                || read.CorpsId != _request.CorpsId
                || read.Command != _request.Descriptor.Command
                || read.ObservedNativeCommandId != _request.Descriptor.NativeCommandId
                || read.ObservedOuterTaskType != _request.Descriptor.OuterTaskType
                || read.ObservedOuterTaskVptr != _request.Descriptor.OuterTaskVptr)
            {
                return Abort(
                    AbortUncertainReason.PostSnapshotBindingMismatch,
                    "Post-state process/context/city/corps/descriptor binding changed.");
            }

            if (!read.CommandStateVerified
                || !read.NativeOrderVerified
                || !SequenceEquals(read.OrderedOfficerIds, _request.OfficerIds)
                || !SequenceEquals(read.BusyOfficerIds, _request.OfficerIds)
                || read.MoneyBefore != _initialMoney
                || read.MoneyAfter != _initialMoney - _request.Descriptor.ExpectedCost)
            {
                return Abort(
                    AbortUncertainReason.PartialCommitObserved,
                    "At least one authenticated postcondition is absent; confirmation is never retried.");
            }

            _state = TransactionState.SyntheticCommittedVerified;
            _outcome = TransactionOutcome.SyntheticCommittedVerified;
            _verifiedNextGenerationNumber = read.GenerationNumber;
            _verifiedNextGenerationDigest = read.GenerationDigest;
            _detail = "All synthetic postconditions were verified. Live execution remains unauthorized.";
            _nextOrdinal++;
            return Snapshot(true, null);
        }

        private TransactionTransition ValidateSelectorStage(SelectorStageEvidence evidence)
        {
            if (!ValidateRootTargetOuter(evidence)
                || !SameLiveObject(evidence.Selector, _selector, ObjectKind.InnerSelector))
            {
                return Abort(
                    AbortUncertainReason.ObjectMissingDestroyedOrReused,
                    "Selector/root/target/outer identity or generation changed.");
            }

            if (!_sourceDigest.Equals(evidence.SourceDigest))
            {
                return Abort(
                    AbortUncertainReason.SourceDigestMismatch,
                    "Native source digest changed between selector stages.");
            }

            if (evidence.ObservedMoney != _initialMoney)
            {
                return Abort(
                    AbortUncertainReason.MoneyChangedDuringTransaction,
                    "Money changed while the selector transaction was active.");
            }

            return null;
        }

        private bool ValidateRootTarget(SyntheticStageEvidence evidence, out AbortUncertainReason failure)
        {
            failure = AbortUncertainReason.None;
            if (!SameLiveObject(evidence.Root, _root, ObjectKind.RootController)
                || !SameLiveObject(evidence.Target, _target, ObjectKind.CityTarget))
            {
                failure = AbortUncertainReason.ObjectMissingDestroyedOrReused;
                return false;
            }

            return true;
        }

        private bool ValidateRootTargetOuter(SyntheticStageEvidence evidence)
        {
            AbortUncertainReason ignored;
            return ValidateRootTarget(evidence, out ignored)
                && SameLiveObject(evidence.Outer, _outer, ObjectKind.OuterTask);
        }

        private bool EvidenceBindsRequest(SyntheticStageEvidence evidence)
        {
            return evidence.RequestId == _request.RequestId
                && evidence.Sequence == _request.Sequence
                && evidence.RequestFingerprint.Equals(_request.RequestFingerprint);
        }

        private void MarkPossibleMutation(SyntheticStageEvidence evidence)
        {
            BindTargetCandidateEvidence bind = evidence as BindTargetCandidateEvidence;
            if (bind != null && bind.ActionApplied)
            {
                _mutationStarted = true;
                return;
            }

            if (evidence.Stage > TransactionStage.BindTargetCandidate)
            {
                _mutationStarted = true;
            }
        }

        private static bool IsLiveKind(ObjectGenerationToken token, ObjectKind expectedKind)
        {
            return token != null
                && token.Kind == expectedKind
                && token.Identity != 0
                && token.Generation != 0
                && token.IsAlive;
        }

        private static bool SameLiveObject(
            ObjectGenerationToken observed,
            ObjectGenerationToken expected,
            ObjectKind expectedKind)
        {
            return IsLiveKind(observed, expectedKind)
                && IsLiveKind(expected, expectedKind)
                && observed.Identity == expected.Identity
                && observed.Generation == expected.Generation;
        }

        private static bool SequenceEquals(IList<int> observed, IList<int> expected)
        {
            if (observed == null || expected == null || observed.Count != expected.Count)
            {
                return false;
            }

            for (int index = 0; index < observed.Count; index++)
            {
                if (observed[index] != expected[index])
                {
                    return false;
                }
            }

            return true;
        }

        private TransactionTransition MalformedForStage(TransactionStage stage)
        {
            return Abort(
                AbortUncertainReason.NullOrMalformedEvidence,
                "Evidence payload type does not match stage " + stage + ".");
        }

        private TransactionTransition MoveTo(TransactionState state, string detail)
        {
            _state = state;
            _detail = detail;
            _nextOrdinal++;
            return Snapshot(true, null);
        }

        private TransactionTransition Skip(BusinessSkipReason reason, string detail)
        {
            if (_mutationStarted)
            {
                return Abort(
                    AbortUncertainReason.InvalidBusinessObservation,
                    "SkipBeforeMutation was requested after mutation had started.");
            }

            _state = TransactionState.SkipBeforeMutation;
            _outcome = TransactionOutcome.SkipBeforeMutation;
            _skipReason = reason;
            _detail = detail;
            return Snapshot(true, null);
        }

        private TransactionTransition Abort(AbortUncertainReason reason, string detail)
        {
            IntentAbortDisposition intentDisposition = IntentAbortDisposition.None;
            if (_pendingIntent != null)
            {
                intentDisposition = _pendingIntent.RevokeForAbort();
            }

            if (_cleanupDirective == null)
            {
                _cleanupDirective = BuildCleanupDirective(detail, intentDisposition);
            }

            _state = TransactionState.AbortUncertain;
            _outcome = TransactionOutcome.AbortUncertain;
            _abortReason = reason;
            _detail = detail;
            return Snapshot(false, null);
        }

        private TransactionTransition Snapshot(bool accepted, string detailOverride)
        {
            return new TransactionTransition(
                accepted,
                _state,
                _outcome,
                IsTerminal ? (TransactionStage?)null : GetExpectedStage(),
                _skipReason,
                _abortReason,
                detailOverride ?? _detail,
                _mutationStarted,
                _innerAcceptAttempts,
                _outerAcceptAttempts,
                _cleanupDirective);
        }

        private AbortCleanupDirective BuildCleanupDirective(
            string detail,
            IntentAbortDisposition intentDisposition)
        {
            TransactionStage stage = GetExpectedStage();
            AbortCleanupKind kind;
            if (intentDisposition == IntentAbortDisposition.ExecutionInFlight)
            {
                kind = AbortCleanupKind.HaltRestart;
            }
            else if (_outerAcceptAttempts > 0
                || stage == TransactionStage.VerifyCommitted)
            {
                kind = AbortCleanupKind.DoNotRollbackAfterAccept;
            }
            else if (_innerAcceptAttempts > 0)
            {
                kind = HasExactRootTargetOuter()
                    ? AbortCleanupKind.CancelOuter
                    : AbortCleanupKind.HaltRestart;
            }
            else if (!_selector.IsAbsent)
            {
                kind = HasExactRootTargetOuter()
                    && IsLiveKind(_selector, ObjectKind.InnerSelector)
                    ? AbortCleanupKind.CancelSelector
                    : AbortCleanupKind.HaltRestart;
            }
            else if (!_outer.IsAbsent)
            {
                kind = HasExactRootTargetOuter()
                    ? AbortCleanupKind.CancelOuter
                    : AbortCleanupKind.HaltRestart;
            }
            else if (!_target.IsAbsent)
            {
                kind = IsLiveKind(_root, ObjectKind.RootController)
                    && IsLiveKind(_target, ObjectKind.CityTarget)
                    ? AbortCleanupKind.ConditionalRestoreTarget
                    : AbortCleanupKind.HaltRestart;
            }
            else if (!_mutationStarted
                || intentDisposition == IntentAbortDisposition.RevokedBeforeExecution)
            {
                kind = AbortCleanupKind.NoMutation;
            }
            else
            {
                // A callback settled or failed without enough exact object
                // generations to prove a targeted recovery.  Never guess.
                kind = AbortCleanupKind.HaltRestart;
            }

            return new AbortCleanupDirective(
                kind,
                _coordinatorSessionId,
                _processKey,
                _request,
                stage,
                _root,
                _target,
                _outer,
                _selector,
                detail);
        }

        private bool HasExactRootTargetOuter()
        {
            return IsLiveKind(_root, ObjectKind.RootController)
                && IsLiveKind(_target, ObjectKind.CityTarget)
                && IsLiveKind(_outer, ObjectKind.OuterTask);
        }

        private TransactionStage GetExpectedStage()
        {
            switch (_state)
            {
                case TransactionState.AwaitBegin:
                    return TransactionStage.Begin;
                case TransactionState.AwaitBindTargetCandidate:
                    return TransactionStage.BindTargetCandidate;
                case TransactionState.AwaitOpenOuter:
                    return TransactionStage.OpenOuter;
                case TransactionState.AwaitOpenSelector:
                    return TransactionStage.OpenSelector;
                case TransactionState.AwaitClear:
                    return TransactionStage.Clear;
                case TransactionState.AwaitNativeFillMax:
                    return TransactionStage.NativeFillMax;
                case TransactionState.AwaitVerifyExactlyExpected5:
                    return TransactionStage.VerifyExactlyExpected5;
                case TransactionState.AwaitAcceptInner:
                    return TransactionStage.AcceptInner;
                case TransactionState.AwaitVerifyWorking:
                    return TransactionStage.VerifyWorking;
                case TransactionState.AwaitAcceptOuter:
                    return TransactionStage.AcceptOuter;
                case TransactionState.AwaitVerifyCommitted:
                    return TransactionStage.VerifyCommitted;
                default:
                    throw new InvalidOperationException("A terminal state has no expected stage.");
            }
        }
    }
}
