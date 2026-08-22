using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;
using San9AutoDomestic.V8Transaction;

namespace San9AutoDomestic.SingleCommandSlice
{
    /// <summary>
    /// Process-local single-flight entry point for the offline shadow slice.
    /// The coordinator owns no process handle, transport, callback, or native
    /// address.  Completing, stopping, halting, or disposing releases the slot.
    /// </summary>
    public static class ShadowSingleCommandSlice
    {
        private static readonly object Sync = new object();
        private static object _activeOwner;

        public static bool IsRunActive
        {
            get
            {
                lock (Sync)
                {
                    return _activeOwner != null;
                }
            }
        }

        public static bool TryStart(
            DomesticConfiguration configuration,
            string profileId,
            IShadowObservationProvider provider,
            out SingleCommandShadowRun run,
            out string detail)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            if (string.IsNullOrWhiteSpace(profileId))
            {
                throw new ArgumentException("A profile id is required.", "profileId");
            }

            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            ExecutionPlan plan = new ExecutionPlanCompiler().Compile(configuration).GetRequired(profileId);
            ValidateShadowPlan(plan);

            object owner = new object();
            lock (Sync)
            {
                if (_activeOwner != null)
                {
                    run = null;
                    detail = "Another shadow slice run owns the process-local single-flight slot.";
                    return false;
                }

                _activeOwner = owner;
            }

            try
            {
                ShadowBatchObservation batch = provider.CaptureBatch(
                    plan.ProfileId,
                    plan.ConfigurationFingerprint);
                if (batch == null)
                {
                    throw new InvalidOperationException("The fake provider returned no batch observation.");
                }

                run = new SingleCommandShadowRun(owner, plan, provider, batch);
                detail = "Offline shadow run started. Live authorization is permanently false.";
                return true;
            }
            catch
            {
                Release(owner);
                throw;
            }
        }

        internal static void Release(object owner)
        {
            lock (Sync)
            {
                if (ReferenceEquals(_activeOwner, owner))
                {
                    _activeOwner = null;
                }
            }
        }

        private static void ValidateShadowPlan(ExecutionPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            if (plan.CityScope != CityScope.DirectCities
                || plan.CityOrder != CityOrder.GameIdAscending)
            {
                throw new NotSupportedException(
                    "The shadow slice requires direct-city scope and ascending game city ids.");
            }

            if (!plan.EligibleForStepwiseValidation || plan.PreviewOnly)
            {
                throw new NotSupportedException(
                    "The selected profile is not eligible for one-command validation.");
            }

            foreach (FrozenTaskPlan task in plan.Tasks)
            {
                if (task.MinOfficers != 5
                    || task.MaxOfficers != 5
                    || !task.RequireExactCount
                    || task.SelectionPolicy != SelectionPolicy.NativeBest)
                {
                    throw new NotSupportedException(
                        "Every shadow task must require exactly five native-best officers.");
                }

                ShadowV8RequestMapper.MapCommand(task.Command);
            }
        }
    }

    public sealed class SingleCommandShadowRun : IDisposable
    {
        private sealed class CityRuntime
        {
            public CityRuntime(ShadowCitySeed seed)
            {
                Seed = seed;
                RemainingOfficerIds = new HashSet<int>(seed.AvailableOfficerIds);
                AllOfficerIds = new HashSet<int>(seed.AvailableOfficerIds);
            }

            public ShadowCitySeed Seed { get; private set; }

            public HashSet<int> RemainingOfficerIds { get; private set; }

            public HashSet<int> AllOfficerIds { get; private set; }
        }

        private sealed class EvaluationOperation
        {
            public EvaluationOperation(
                object ownerToken,
                Guid runId,
                SingleCommandSliceTicket ticket,
                int cityOrdinal,
                int taskOrdinal,
                FixedDigest generationDigest,
                ShadowCommandObservationRequest request)
            {
                Nonce = Guid.NewGuid();
                OwnerToken = ownerToken;
                RunId = runId;
                Ticket = ticket;
                CityOrdinal = cityOrdinal;
                TaskOrdinal = taskOrdinal;
                GenerationDigest = generationDigest;
                Request = request;
            }

            public Guid Nonce { get; private set; }

            public object OwnerToken { get; private set; }

            public Guid RunId { get; private set; }

            public SingleCommandSliceTicket Ticket { get; private set; }

            public int CityOrdinal { get; private set; }

            public int TaskOrdinal { get; private set; }

            public FixedDigest GenerationDigest { get; private set; }

            public ShadowCommandObservationRequest Request { get; private set; }
        }

        public const int ShadowRequestTimeToLiveMilliseconds = 5000;

        private readonly object _sync = new object();
        private readonly object _ownerToken;
        private readonly ExecutionPlan _plan;
        private readonly IShadowObservationProvider _provider;
        private readonly ShadowBatchObservation _batch;
        private readonly List<CityRuntime> _cities;
        private readonly Dictionary<int, int> _remainingMoneyByCorps;
        private readonly HashSet<Guid> _consumedTicketIds;
        private readonly HashSet<Guid> _committedTicketIds;
        private bool _slotReleased;
        private ShadowSliceState _state;
        private int _cityOrdinal;
        private int _taskOrdinal;
        private SingleCommandSliceTicket _outstandingTicket;
        private EvaluationOperation _activeEvaluation;
        private ValidatedSingleCommand _pendingCommand;
        private ShadowHaltReason _haltReason;
        private string _haltDetail;

        internal SingleCommandShadowRun(
            object ownerToken,
            ExecutionPlan plan,
            IShadowObservationProvider provider,
            ShadowBatchObservation batch)
        {
            _ownerToken = ownerToken;
            _plan = plan;
            _provider = provider;
            _batch = batch;
            _consumedTicketIds = new HashSet<Guid>();
            _committedTicketIds = new HashSet<Guid>();
            _remainingMoneyByCorps = new Dictionary<int, int>();
            foreach (ShadowCorpsSeed corps in batch.Corps)
            {
                _remainingMoneyByCorps.Add(corps.CorpsId, corps.Money);
            }

            List<ShadowCitySeed> sortedSeeds = new List<ShadowCitySeed>(batch.Cities);
            sortedSeeds.Sort(delegate(ShadowCitySeed left, ShadowCitySeed right)
            {
                return left.CityId.CompareTo(right.CityId);
            });
            _cities = new List<CityRuntime>();
            foreach (ShadowCitySeed seed in sortedSeeds)
            {
                _cities.Add(new CityRuntime(seed));
            }

            RunId = Guid.NewGuid();
            _state = _cities.Count == 0
                ? ShadowSliceState.Completed
                : ShadowSliceState.Ready;
            _haltDetail = string.Empty;
            if (_state == ShadowSliceState.Completed)
            {
                ReleaseSlotLocked();
            }
        }

        public Guid RunId { get; private set; }

        public string ProfileId { get { return _plan.ProfileId; } }

        public string ConfigurationFingerprint { get { return _plan.ConfigurationFingerprint; } }

        public ShadowSliceState State
        {
            get
            {
                lock (_sync)
                {
                    return _state;
                }
            }
        }

        public ShadowHaltReason HaltReason
        {
            get
            {
                lock (_sync)
                {
                    return _haltReason;
                }
            }
        }

        public string HaltDetail
        {
            get
            {
                lock (_sync)
                {
                    return _haltDetail;
                }
            }
        }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }

        public int CityCount { get { return _cities.Count; } }

        public int TaskCount { get { return _plan.Tasks.Count; } }

        public int CurrentCityOrdinal
        {
            get
            {
                lock (_sync)
                {
                    return IsCursorAvailableLocked() ? _cityOrdinal : -1;
                }
            }
        }

        public int CurrentTaskOrdinal
        {
            get
            {
                lock (_sync)
                {
                    return IsCursorAvailableLocked() ? _taskOrdinal : -1;
                }
            }
        }

        public int CurrentCityId
        {
            get
            {
                lock (_sync)
                {
                    return IsCursorAvailableLocked()
                        ? _cities[_cityOrdinal].Seed.CityId
                        : -1;
                }
            }
        }

        public DomesticCommand CurrentCommand
        {
            get
            {
                lock (_sync)
                {
                    return IsCursorAvailableLocked()
                        ? _plan.Tasks[_taskOrdinal].Command
                        : DomesticCommand.Unknown;
                }
            }
        }

        public bool TryIssueCurrentTicket(
            out SingleCommandSliceTicket ticket,
            out ShadowTicketIssueRejection rejection)
        {
            lock (_sync)
            {
                return TryIssueTicketLocked(_cityOrdinal, _taskOrdinal, out ticket, out rejection);
            }
        }

        /// <summary>
        /// Explicit ordinal entry used to prove that callers cannot jump over a
        /// higher-priority pair.  Any mismatch fail-closes the whole shadow run.
        /// </summary>
        public bool TryIssueTicketFor(
            int cityOrdinal,
            int taskOrdinal,
            out SingleCommandSliceTicket ticket,
            out ShadowTicketIssueRejection rejection)
        {
            lock (_sync)
            {
                return TryIssueTicketLocked(cityOrdinal, taskOrdinal, out ticket, out rejection);
            }
        }

        public ShadowStepResult Evaluate(SingleCommandSliceTicket ticket)
        {
            EvaluationOperation operation;
            lock (_sync)
            {
                if (ticket == null)
                {
                    return HaltLocked(
                        ShadowHaltReason.TicketMismatch,
                        "A null ticket cannot be evaluated.",
                        null);
                }

                if (_consumedTicketIds.Contains(ticket.TicketId))
                {
                    return HaltLocked(
                        ShadowHaltReason.TicketAlreadyConsumed,
                        "A single-command ticket may be evaluated only once.",
                        ticket);
                }

                if (_state != ShadowSliceState.TicketOutstanding
                    || !ReferenceEquals(ticket.OwnerToken, _ownerToken)
                    || !ReferenceEquals(ticket, _outstandingTicket)
                    || ticket.RunId != RunId
                    || ticket.CityOrdinal != _cityOrdinal
                    || ticket.TaskOrdinal != _taskOrdinal)
                {
                    return HaltLocked(
                        ShadowHaltReason.TicketMismatch,
                        "The ticket does not bind the outstanding current ordinal.",
                        ticket);
                }

                _consumedTicketIds.Add(ticket.TicketId);
                _outstandingTicket = null;

                CityRuntime city = _cities[_cityOrdinal];
                FrozenTaskPlan task = _plan.Tasks[_taskOrdinal];
                if (city.Seed.OwnerForceId != _batch.PlayerForceId)
                {
                    return SkipLocked(ticket, ShadowSkipReason.NotOwnedByPlayer,
                        "City is not owned by the synthetic player force.");
                }

                if (!city.Seed.IsDirectlyControlled)
                {
                    return SkipLocked(ticket, ShadowSkipReason.DelegatedCity,
                        "City is synthetically marked as delegated.");
                }

                ShadowCommandObservationRequest observationRequest =
                    new ShadowCommandObservationRequest(
                        RunId,
                        _plan.ProfileId,
                        _plan.ConfigurationFingerprint,
                        _cityOrdinal,
                        _taskOrdinal,
                        city.Seed.CityId,
                        task.Command,
                        _batch.GenerationDigest);
                operation = new EvaluationOperation(
                    _ownerToken,
                    RunId,
                    ticket,
                    _cityOrdinal,
                    _taskOrdinal,
                    _batch.GenerationDigest,
                    observationRequest);
                _activeEvaluation = operation;
                _state = ShadowSliceState.Evaluating;
            }

            ShadowCommandObservation observation = null;
            Exception observationFailure = null;
            try
            {
                // External fake callback is deliberately outside _sync.  It may
                // re-enter any public method; the exact operation check below
                // prevents that re-entry from resurrecting invalidated state.
                observation = _provider.ObserveCommand(operation.Request);
            }
            catch (Exception exception)
            {
                observationFailure = exception;
            }

            lock (_sync)
            {
                if (!IsExactEvaluationOperationLocked(operation, ticket))
                {
                    return EvaluationInvalidatedResultLocked(operation, ticket);
                }

                _activeEvaluation = null;
                if (observationFailure != null)
                {
                    return HaltLocked(
                        ShadowHaltReason.ObservationFailure,
                        "Fake observation provider failed: " + observationFailure.GetType().Name,
                        ticket);
                }

                if (observation == null)
                {
                    return HaltLocked(
                        ShadowHaltReason.ObservationFailure,
                        "Fake observation provider returned null.",
                        ticket);
                }

                CityRuntime city = _cities[operation.CityOrdinal];
                FrozenTaskPlan task = _plan.Tasks[operation.TaskOrdinal];
                if (!observation.GenerationDigest.Equals(_batch.GenerationDigest))
                {
                    return HaltLocked(
                        ShadowHaltReason.GenerationChanged,
                        "Synthetic generation digest changed before validation.",
                        ticket);
                }

                if (observation.CityId != city.Seed.CityId
                    || observation.CorpsId != city.Seed.CorpsId
                    || observation.Command != task.Command)
                {
                    return HaltLocked(
                        ShadowHaltReason.ObservationMismatch,
                        "Fresh observation does not match the current city, corps, and task.",
                        ticket);
                }

                if (observation.OwnerForceId != _batch.PlayerForceId)
                {
                    return SkipLocked(ticket, ShadowSkipReason.NotOwnedByPlayer,
                        "Fresh synthetic observation no longer belongs to the player force.");
                }

                if (!observation.IsDirectlyControlled)
                {
                    return SkipLocked(ticket, ShadowSkipReason.DelegatedCity,
                        "Fresh synthetic observation is delegated.");
                }

                if (!observation.CanExecute)
                {
                    ShadowSkipReason reason = observation.BlockReason == NativeCommandBlockReason.GreyedOut
                        ? ShadowSkipReason.GreyedOut
                        : ShadowSkipReason.CommandBlocked;
                    return SkipLocked(ticket, reason,
                        "Synthetic native availability blocked the command: " + observation.BlockReason + ".");
                }

                List<int> selected = new List<int>();
                foreach (int officerId in observation.RankedCandidateOfficerIds)
                {
                    if (!city.AllOfficerIds.Contains(officerId))
                    {
                        return HaltLocked(
                            ShadowHaltReason.ObservationMismatch,
                            "Ranked officer does not belong to the frozen city seed.",
                            ticket);
                    }

                    if (city.RemainingOfficerIds.Contains(officerId) && selected.Count < 5)
                    {
                        selected.Add(officerId);
                    }
                }

                if (selected.Count != 5)
                {
                    return SkipLocked(ticket, ShadowSkipReason.InsufficientOfficers,
                        "Fewer than five currently unconsumed ranked officers remain.");
                }

                NativeCommandDescriptor descriptor;
                try
                {
                    descriptor = NativeCommandDescriptorRegistry.GetRequired(
                        ShadowV8RequestMapper.MapCommand(task.Command));
                }
                catch (Exception exception)
                {
                    return HaltLocked(
                        ShadowHaltReason.ObservationMismatch,
                        "Frozen command descriptor mapping failed: " + exception.GetType().Name,
                        ticket);
                }
                int remainingMoney = _remainingMoneyByCorps[city.Seed.CorpsId];
                if (descriptor.ExpectedCost > 0 && remainingMoney < descriptor.ExpectedCost)
                {
                    return SkipLocked(ticket, ShadowSkipReason.InsufficientFunds,
                        "Shared synthetic corps money is below the command cost.");
                }

                if (descriptor.ExpectedCost > 0
                    && remainingMoney - descriptor.ExpectedCost < task.ReserveMoney)
                {
                    return SkipLocked(ticket, ShadowSkipReason.ReserveMoneyProtected,
                        "The command would cross the frozen reserve-money boundary.");
                }

                SingleCommandRequest request;
                try
                {
                    request = ShadowV8RequestMapper.CreateRequest(
                        ticket,
                        task,
                        city.Seed.CorpsId,
                        selected,
                        _batch.ContextDigest,
                        _batch.GenerationDigest,
                        ShadowRequestTimeToLiveMilliseconds);
                }
                catch (Exception exception)
                {
                    return HaltLocked(
                        ShadowHaltReason.ObservationMismatch,
                        "V8 shadow request mapping failed: " + exception.GetType().Name,
                        ticket);
                }
                _pendingCommand = new ValidatedSingleCommand(
                    _ownerToken,
                    ticket,
                    city.Seed.CorpsId,
                    selected,
                    descriptor.ExpectedCost,
                    task.ReserveMoney,
                    request);
                _state = ShadowSliceState.AwaitingCommit;
                return new ShadowStepResult(
                    ShadowStepDisposition.Validated,
                    ticket,
                    ShadowSkipReason.None,
                    ShadowHaltReason.None,
                    "One shadow command was structurally validated; no native submission occurred.",
                    _pendingCommand);
            }
        }

        public bool TryCommit(
            ValidatedSingleCommand command,
            out ShadowCommitReceipt receipt)
        {
            lock (_sync)
            {
                if (command == null
                    || _state != ShadowSliceState.AwaitingCommit
                    || !ReferenceEquals(command.OwnerToken, _ownerToken)
                    || !ReferenceEquals(command, _pendingCommand)
                    || command.CityOrdinal != _cityOrdinal
                    || command.TaskOrdinal != _taskOrdinal
                    || _committedTicketIds.Contains(command.TicketId))
                {
                    HaltLocked(
                        ShadowHaltReason.CommitMismatch,
                        "Commit does not bind the one pending validated shadow command.",
                        command == null ? null : command.Ticket);
                    receipt = null;
                    return false;
                }

                CityRuntime city = _cities[_cityOrdinal];
                foreach (int officerId in command.OfficerIds)
                {
                    if (!city.RemainingOfficerIds.Contains(officerId))
                    {
                        HaltLocked(
                            ShadowHaltReason.CommitMismatch,
                            "A selected officer is no longer available in the shadow ledger.",
                            command.Ticket);
                        receipt = null;
                        return false;
                    }
                }

                int money = _remainingMoneyByCorps[command.CorpsId];
                if (money < command.ExpectedCost
                    || (command.ExpectedCost > 0
                        && money - command.ExpectedCost < command.ReserveMoney))
                {
                    HaltLocked(
                        ShadowHaltReason.CommitMismatch,
                        "Shared shadow funds changed before commit.",
                        command.Ticket);
                    receipt = null;
                    return false;
                }

                foreach (int officerId in command.OfficerIds)
                {
                    city.RemainingOfficerIds.Remove(officerId);
                }

                int afterMoney = money - command.ExpectedCost;
                _remainingMoneyByCorps[command.CorpsId] = afterMoney;
                _committedTicketIds.Add(command.TicketId);
                _pendingCommand = null;
                receipt = new ShadowCommitReceipt(
                    command,
                    city.RemainingOfficerIds.Count,
                    afterMoney);
                AdvanceLocked();
                return true;
            }
        }

        public bool Stop()
        {
            lock (_sync)
            {
                if (_state == ShadowSliceState.Completed
                    || _state == ShadowSliceState.Stopped
                    || _state == ShadowSliceState.Halted)
                {
                    return false;
                }

                if (_outstandingTicket != null)
                {
                    _consumedTicketIds.Add(_outstandingTicket.TicketId);
                }

                _outstandingTicket = null;
                _activeEvaluation = null;
                _pendingCommand = null;
                _state = ShadowSliceState.Stopped;
                ReleaseSlotLocked();
                return true;
            }
        }

        public int GetRemainingMoney(int corpsId)
        {
            lock (_sync)
            {
                int money;
                if (!_remainingMoneyByCorps.TryGetValue(corpsId, out money))
                {
                    throw new ArgumentOutOfRangeException("corpsId");
                }

                return money;
            }
        }

        public ReadOnlyCollection<int> GetRemainingOfficerIds(int cityId)
        {
            lock (_sync)
            {
                foreach (CityRuntime city in _cities)
                {
                    if (city.Seed.CityId == cityId)
                    {
                        List<int> ids = new List<int>(city.RemainingOfficerIds);
                        ids.Sort();
                        return new ReadOnlyCollection<int>(ids);
                    }
                }

                throw new ArgumentOutOfRangeException("cityId");
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private bool TryIssueTicketLocked(
            int cityOrdinal,
            int taskOrdinal,
            out SingleCommandSliceTicket ticket,
            out ShadowTicketIssueRejection rejection)
        {
            if (_state == ShadowSliceState.Completed
                || _state == ShadowSliceState.Stopped
                || _state == ShadowSliceState.Halted)
            {
                ticket = null;
                rejection = ShadowTicketIssueRejection.NotRunning;
                return false;
            }

            if (cityOrdinal != _cityOrdinal || taskOrdinal != _taskOrdinal)
            {
                HaltLocked(
                    ShadowHaltReason.OutOfOrderTicketRequest,
                    "A ticket request attempted to skip the current higher-priority ordinal.",
                    null);
                ticket = null;
                rejection = ShadowTicketIssueRejection.OutOfOrder;
                return false;
            }

            if (_state != ShadowSliceState.Ready)
            {
                ticket = null;
                rejection = ShadowTicketIssueRejection.OutstandingWork;
                return false;
            }

            ulong sequence = checked(
                ((ulong)_cityOrdinal * (ulong)_plan.Tasks.Count)
                + (ulong)_taskOrdinal
                + 1UL);
            CityRuntime city = _cities[_cityOrdinal];
            FrozenTaskPlan task = _plan.Tasks[_taskOrdinal];
            ticket = new SingleCommandSliceTicket(
                _ownerToken,
                Guid.NewGuid(),
                RunId,
                sequence,
                _cityOrdinal,
                _taskOrdinal,
                city.Seed.CityId,
                task.Command);
            _outstandingTicket = ticket;
            _state = ShadowSliceState.TicketOutstanding;
            rejection = ShadowTicketIssueRejection.None;
            return true;
        }

        private bool IsExactEvaluationOperationLocked(
            EvaluationOperation operation,
            SingleCommandSliceTicket ticket)
        {
            return operation != null
                && ticket != null
                && _state == ShadowSliceState.Evaluating
                && ReferenceEquals(_activeEvaluation, operation)
                && _activeEvaluation.Nonce == operation.Nonce
                && operation.Nonce != Guid.Empty
                && ReferenceEquals(operation.OwnerToken, _ownerToken)
                && ReferenceEquals(ticket.OwnerToken, _ownerToken)
                && ReferenceEquals(operation.Ticket, ticket)
                && operation.RunId == RunId
                && ticket.RunId == RunId
                && operation.CityOrdinal == _cityOrdinal
                && operation.TaskOrdinal == _taskOrdinal
                && ticket.CityOrdinal == _cityOrdinal
                && ticket.TaskOrdinal == _taskOrdinal
                && operation.GenerationDigest.Equals(_batch.GenerationDigest)
                && operation.Request.RunId == RunId
                && operation.Request.CityOrdinal == _cityOrdinal
                && operation.Request.TaskOrdinal == _taskOrdinal
                && operation.Request.CityId == ticket.CityId
                && operation.Request.Command == ticket.Command
                && operation.Request.ExpectedGenerationDigest.Equals(_batch.GenerationDigest)
                && _consumedTicketIds.Contains(ticket.TicketId)
                && _outstandingTicket == null
                && _pendingCommand == null;
        }

        private ShadowStepResult EvaluationInvalidatedResultLocked(
            EvaluationOperation operation,
            SingleCommandSliceTicket ticket)
        {
            const string Detail =
                "The external fake callback invalidated the exact pending evaluation operation.";
            if (_state != ShadowSliceState.Completed
                && _state != ShadowSliceState.Stopped
                && _state != ShadowSliceState.Halted)
            {
                return HaltLocked(
                    ShadowHaltReason.EvaluationInvalidated,
                    Detail,
                    ticket);
            }

            return new ShadowStepResult(
                ShadowStepDisposition.Halted,
                ticket,
                ShadowSkipReason.None,
                ShadowHaltReason.EvaluationInvalidated,
                Detail,
                null);
        }

        private ShadowStepResult SkipLocked(
            SingleCommandSliceTicket ticket,
            ShadowSkipReason reason,
            string detail)
        {
            AdvanceLocked();
            return new ShadowStepResult(
                ShadowStepDisposition.Skipped,
                ticket,
                reason,
                ShadowHaltReason.None,
                detail,
                null);
        }

        private ShadowStepResult HaltLocked(
            ShadowHaltReason reason,
            string detail,
            SingleCommandSliceTicket ticket)
        {
            if (_state != ShadowSliceState.Completed
                && _state != ShadowSliceState.Stopped
                && _state != ShadowSliceState.Halted)
            {
                _state = ShadowSliceState.Halted;
                _haltReason = reason;
                _haltDetail = detail ?? string.Empty;
                _outstandingTicket = null;
                _activeEvaluation = null;
                _pendingCommand = null;
                ReleaseSlotLocked();
            }

            return new ShadowStepResult(
                ShadowStepDisposition.Halted,
                ticket,
                ShadowSkipReason.None,
                reason,
                detail,
                null);
        }

        private void AdvanceLocked()
        {
            _taskOrdinal++;
            if (_taskOrdinal >= _plan.Tasks.Count)
            {
                _taskOrdinal = 0;
                _cityOrdinal++;
            }

            if (_cityOrdinal >= _cities.Count)
            {
                _state = ShadowSliceState.Completed;
                ReleaseSlotLocked();
            }
            else
            {
                _state = ShadowSliceState.Ready;
            }
        }

        private bool IsCursorAvailableLocked()
        {
            return _cityOrdinal >= 0
                && _cityOrdinal < _cities.Count
                && _taskOrdinal >= 0
                && _taskOrdinal < _plan.Tasks.Count
                && _state != ShadowSliceState.Completed
                && _state != ShadowSliceState.Stopped
                && _state != ShadowSliceState.Halted;
        }

        private void ReleaseSlotLocked()
        {
            if (!_slotReleased)
            {
                _slotReleased = true;
                ShadowSingleCommandSlice.Release(_ownerToken);
            }
        }
    }
}
