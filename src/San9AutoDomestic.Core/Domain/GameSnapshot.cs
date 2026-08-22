using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Planning;

namespace San9AutoDomestic.Core.Domain
{
    public enum FacilityType
    {
        Unknown = 0,
        City = 1,
        Gate = 2,
        Port = 3,
        Camp = 4,
        Other = 5
    }

    public enum NativeCommandBlockReason
    {
        None = 0,
        GreyedOut = 1,
        AlreadyExecuted = 2,
        ReachedLimit = 3,
        CityInCombat = 4,
        CityConfused = 5,
        NoTroops = 6,
        InsufficientFunds = 7,
        StateChanged = 8,
        Other = 9
    }

    public sealed class OfficerSnapshot
    {
        public OfficerSnapshot(
            int id,
            string name,
            bool canAct,
            int effectiveLeadership,
            int effectiveMight,
            int effectiveIntelligence,
            int effectivePolitics)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException("id");
            }

            Id = id;
            Name = name ?? string.Empty;
            CanAct = canAct;
            EffectiveLeadership = effectiveLeadership;
            EffectiveMight = effectiveMight;
            EffectiveIntelligence = effectiveIntelligence;
            EffectivePolitics = effectivePolitics;
        }

        public int Id { get; private set; }

        public string Name { get; private set; }

        public bool CanAct { get; private set; }

        public int EffectiveLeadership { get; private set; }

        public int EffectiveMight { get; private set; }

        public int EffectiveIntelligence { get; private set; }

        public int EffectivePolitics { get; private set; }
    }

    public sealed class NativeCommandSnapshot
    {
        private readonly ReadOnlyCollection<int> _rankedCandidateOfficerIds;

        public NativeCommandSnapshot(
            DomesticCommand command,
            bool canExecute,
            NativeCommandBlockReason blockReason,
            IEnumerable<int> rankedCandidateOfficerIds,
            int estimatedCostPerOfficer)
            : this(
                command,
                canExecute,
                blockReason,
                rankedCandidateOfficerIds,
                estimatedCostPerOfficer,
                null)
        {
        }

        internal NativeCommandSnapshot(
            DomesticCommand command,
            bool canExecute,
            NativeCommandBlockReason blockReason,
            IEnumerable<int> rankedCandidateOfficerIds,
            int estimatedCostPerOfficer,
            TrustedObservationCapability observationCapability)
        {
            if (rankedCandidateOfficerIds == null)
            {
                throw new ArgumentNullException("rankedCandidateOfficerIds");
            }

            if (!Enum.IsDefined(typeof(DomesticCommand), command)
                || command == DomesticCommand.Unknown)
            {
                throw new ArgumentOutOfRangeException("command");
            }

            if (!Enum.IsDefined(typeof(NativeCommandBlockReason), blockReason))
            {
                throw new ArgumentOutOfRangeException("blockReason");
            }

            if (canExecute && blockReason != NativeCommandBlockReason.None)
            {
                throw new ArgumentException("An executable command cannot have a block reason.", "blockReason");
            }

            if (!canExecute && blockReason == NativeCommandBlockReason.None)
            {
                throw new ArgumentException("A blocked command requires an explicit block reason.", "blockReason");
            }

            if (estimatedCostPerOfficer < 0
                || estimatedCostPerOfficer > GameRules.GameMoneyMax)
            {
                throw new ArgumentOutOfRangeException("estimatedCostPerOfficer");
            }

            List<int> candidateIds = new List<int>(rankedCandidateOfficerIds);
            HashSet<int> uniqueCandidateIds = new HashSet<int>();
            foreach (int officerId in candidateIds)
            {
                if (officerId < 0)
                {
                    throw new ArgumentOutOfRangeException("rankedCandidateOfficerIds");
                }

                if (!uniqueCandidateIds.Add(officerId))
                {
                    throw new ArgumentException(
                        "Ranked candidate officer ids must be unique.",
                        "rankedCandidateOfficerIds");
                }
            }

            Command = command;
            CanExecute = canExecute;
            BlockReason = blockReason;
            _rankedCandidateOfficerIds = new ReadOnlyCollection<int>(candidateIds);
            ObservationCapability = observationCapability;
            HasVerifiedNativeRanking = observationCapability != null;
            EstimatedCostPerOfficer = estimatedCostPerOfficer;
        }

        public DomesticCommand Command { get; private set; }

        public bool CanExecute { get; private set; }

        public NativeCommandBlockReason BlockReason { get; private set; }

        public ReadOnlyCollection<int> RankedCandidateOfficerIds
        {
            get { return _rankedCandidateOfficerIds; }
        }

        public bool HasVerifiedNativeRanking { get; private set; }

        public string ObservationId
        {
            get
            {
                return ObservationCapability == null
                    ? string.Empty
                    : ObservationCapability.ObservationId;
            }
        }

        public int EstimatedCostPerOfficer { get; private set; }

        internal TrustedObservationCapability ObservationCapability { get; private set; }
    }

    public sealed class FacilitySnapshot
    {
        private readonly ReadOnlyCollection<OfficerSnapshot> _officers;
        private readonly ReadOnlyCollection<NativeCommandSnapshot> _commandSnapshots;
        private readonly IDictionary<int, OfficerSnapshot> _officersById;
        private readonly IDictionary<DomesticCommand, NativeCommandSnapshot> _commands;

        public FacilitySnapshot(
            int id,
            string name,
            FacilityType facilityType,
            int ownerForceId,
            int corpsId,
            bool isDirectlyControlled,
            IEnumerable<OfficerSnapshot> officers,
            IEnumerable<NativeCommandSnapshot> commands)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException("id");
            }

            if (!Enum.IsDefined(typeof(FacilityType), facilityType))
            {
                throw new ArgumentOutOfRangeException("facilityType");
            }

            if (ownerForceId < -1)
            {
                throw new ArgumentOutOfRangeException("ownerForceId");
            }

            if (corpsId < -1)
            {
                throw new ArgumentOutOfRangeException("corpsId");
            }

            if (officers == null)
            {
                throw new ArgumentNullException("officers");
            }

            if (commands == null)
            {
                throw new ArgumentNullException("commands");
            }

            List<OfficerSnapshot> officerList = new List<OfficerSnapshot>(officers);
            Dictionary<int, OfficerSnapshot> officersById = new Dictionary<int, OfficerSnapshot>();
            foreach (OfficerSnapshot officer in officerList)
            {
                if (officer == null)
                {
                    throw new ArgumentException("Officer snapshots must not contain null.", "officers");
                }

                if (officersById.ContainsKey(officer.Id))
                {
                    throw new ArgumentException("Officer ids must be unique within a facility.", "officers");
                }

                officersById.Add(officer.Id, officer);
            }

            List<NativeCommandSnapshot> commandList = new List<NativeCommandSnapshot>(commands);
            Dictionary<DomesticCommand, NativeCommandSnapshot> commandsByType =
                new Dictionary<DomesticCommand, NativeCommandSnapshot>();
            foreach (NativeCommandSnapshot command in commandList)
            {
                if (command == null)
                {
                    throw new ArgumentException("Command snapshots must not contain null.", "commands");
                }

                if (commandsByType.ContainsKey(command.Command))
                {
                    throw new ArgumentException("A command may appear only once per facility snapshot.", "commands");
                }

                foreach (int officerId in command.RankedCandidateOfficerIds)
                {
                    OfficerSnapshot candidate;
                    if (!officersById.TryGetValue(officerId, out candidate))
                    {
                        throw new ArgumentException(
                            "Every ranked candidate must belong to the same facility snapshot.",
                            "commands");
                    }

                    if (!candidate.CanAct)
                    {
                        throw new ArgumentException(
                            "A ranked candidate must be currently able to act.",
                            "commands");
                    }
                }

                commandsByType.Add(command.Command, command);
            }

            Id = id;
            Name = name ?? string.Empty;
            FacilityType = facilityType;
            OwnerForceId = ownerForceId;
            CorpsId = corpsId;
            IsDirectlyControlled = isDirectlyControlled;
            _officers = new ReadOnlyCollection<OfficerSnapshot>(officerList);
            _commandSnapshots = new ReadOnlyCollection<NativeCommandSnapshot>(commandList);
            _officersById = officersById;
            _commands = commandsByType;
        }

        public int Id { get; private set; }

        public string Name { get; private set; }

        public FacilityType FacilityType { get; private set; }

        public int OwnerForceId { get; private set; }

        public int CorpsId { get; private set; }

        public bool IsDirectlyControlled { get; private set; }

        public ReadOnlyCollection<OfficerSnapshot> Officers
        {
            get { return _officers; }
        }

        internal ReadOnlyCollection<NativeCommandSnapshot> CommandSnapshots
        {
            get { return _commandSnapshots; }
        }

        public bool TryGetOfficer(int officerId, out OfficerSnapshot officer)
        {
            return _officersById.TryGetValue(officerId, out officer);
        }

        public bool TryGetCommand(DomesticCommand command, out NativeCommandSnapshot snapshot)
        {
            return _commands.TryGetValue(command, out snapshot);
        }
    }

    public sealed class CorpsMoneySnapshot
    {
        public CorpsMoneySnapshot(int corpsId, int money)
        {
            if (corpsId < 0)
            {
                throw new ArgumentOutOfRangeException("corpsId");
            }

            if (money < 0 || money > GameRules.GameMoneyMax)
            {
                throw new ArgumentOutOfRangeException("money");
            }

            CorpsId = corpsId;
            Money = money;
        }

        public int CorpsId { get; private set; }

        public int Money { get; private set; }
    }

    public sealed class GameSnapshot
    {
        private readonly ReadOnlyCollection<FacilitySnapshot> _facilities;
        private readonly IDictionary<int, FacilitySnapshot> _facilitiesById;
        private readonly IDictionary<int, int> _moneyByCorpsId;

        public GameSnapshot(
            int playerForceId,
            IEnumerable<FacilitySnapshot> facilities,
            IEnumerable<CorpsMoneySnapshot> corpsMoney)
            : this(
                playerForceId,
                facilities,
                corpsMoney,
                GameSnapshotContext.CreateStructureOnly(
                    playerForceId >= 0 ? (int?)playerForceId : null,
                    "No planning context was supplied; this is a structure-only snapshot."))
        {
        }

        public GameSnapshot(
            int playerForceId,
            IEnumerable<FacilitySnapshot> facilities,
            IEnumerable<CorpsMoneySnapshot> corpsMoney,
            GameSnapshotContext context)
        {
            if (facilities == null)
            {
                throw new ArgumentNullException("facilities");
            }

            if (corpsMoney == null)
            {
                throw new ArgumentNullException("corpsMoney");
            }


            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            if (context.PlayerForceId.HasValue
                && context.PlayerForceId.Value != playerForceId)
            {
                throw new ArgumentException(
                    "Snapshot context player force does not match the snapshot player force.",
                    "context");
            }

            List<FacilitySnapshot> facilityList = new List<FacilitySnapshot>(facilities);
            Dictionary<int, FacilitySnapshot> facilitiesById = new Dictionary<int, FacilitySnapshot>();
            HashSet<int> officerIds = new HashSet<int>();
            foreach (FacilitySnapshot facility in facilityList)
            {
                if (facility == null)
                {
                    throw new ArgumentException("Facility snapshots must not contain null.", "facilities");
                }

                if (facilitiesById.ContainsKey(facility.Id))
                {
                    throw new ArgumentException("Facility ids must be unique.", "facilities");
                }

                facilitiesById.Add(facility.Id, facility);
                foreach (OfficerSnapshot officer in facility.Officers)
                {
                    if (!officerIds.Add(officer.Id))
                    {
                        throw new ArgumentException("Officer ids must be unique across the game snapshot.", "facilities");
                    }
                }
            }

            Dictionary<int, int> moneyByCorps = new Dictionary<int, int>();
            foreach (CorpsMoneySnapshot money in corpsMoney)
            {
                if (money == null)
                {
                    throw new ArgumentException("Corps money snapshots must not contain null.", "corpsMoney");
                }

                if (moneyByCorps.ContainsKey(money.CorpsId))
                {
                    throw new ArgumentException("Corps ids must be unique in the money snapshot.", "corpsMoney");
                }

                moneyByCorps.Add(money.CorpsId, money.Money);
            }

            PlayerForceId = playerForceId;
            Context = context;
            _facilities = new ReadOnlyCollection<FacilitySnapshot>(facilityList);
            _facilitiesById = facilitiesById;
            _moneyByCorpsId = moneyByCorps;
            EvaluatePlanningCompleteness();
        }

        public int PlayerForceId { get; private set; }

        public GameSnapshotContext Context { get; private set; }

        public bool IsPlanningComplete { get; private set; }

        public string PlanningIncompleteDetail { get; private set; }

        public ReadOnlyCollection<FacilitySnapshot> Facilities
        {
            get { return _facilities; }
        }

        public bool TryGetFacility(int facilityId, out FacilitySnapshot facility)
        {
            return _facilitiesById.TryGetValue(facilityId, out facility);
        }

        public bool TryGetCorpsMoney(int corpsId, out int money)
        {
            return _moneyByCorpsId.TryGetValue(corpsId, out money);
        }

        public IDictionary<int, int> CopyMoneyByCorps()
        {
            return new Dictionary<int, int>(_moneyByCorpsId);
        }

        private void EvaluatePlanningCompleteness()
        {
            IsPlanningComplete = false;
            PlanningIncompleteDetail = Context.Detail ?? string.Empty;
            if (Context.Readiness != SnapshotReadiness.PlanningReady)
            {
                if (string.IsNullOrWhiteSpace(PlanningIncompleteDetail))
                {
                    PlanningIncompleteDetail = "The snapshot is structure-only.";
                }

                return;
            }

            if (PlayerForceId < 0)
            {
                PlanningIncompleteDetail = "The player force id is invalid.";
                return;
            }

            TrustedObservationCapability contextCapability = Context.ObservationCapability;
            int verifiedCommandCount = 0;
            foreach (FacilitySnapshot facility in _facilities)
            {
                if (facility.FacilityType == FacilityType.Unknown)
                {
                    PlanningIncompleteDetail = "Facility " + facility.Id
                        + " has an unknown facility type.";
                    return;
                }

                if (facility.FacilityType == FacilityType.City
                    && facility.IsDirectlyControlled)
                {
                    if (facility.OwnerForceId != PlayerForceId)
                    {
                        PlanningIncompleteDetail = "Direct city " + facility.Id
                            + " is not owned by the player force.";
                        return;
                    }

                    if (facility.CorpsId < 0)
                    {
                        PlanningIncompleteDetail = "Direct city " + facility.Id
                            + " has no verified corps id.";
                        return;
                    }

                    if (!_moneyByCorpsId.ContainsKey(facility.CorpsId))
                    {
                        PlanningIncompleteDetail = "Direct city " + facility.Id
                            + " has no corps-money snapshot.";
                        return;
                    }
                }

                foreach (NativeCommandSnapshot command in facility.CommandSnapshots)
                {
                    if (!command.HasVerifiedNativeRanking)
                    {
                        continue;
                    }

                    verifiedCommandCount++;
                    if (contextCapability == null
                        || !ReferenceEquals(command.ObservationCapability, contextCapability))
                    {
                        PlanningIncompleteDetail = "Verified command ranking does not belong to the snapshot observation.";
                        return;
                    }

                    CommandObservationRequest request = contextCapability.CommandRequest;
                    if (request != null
                        && (facility.Id != request.CityId || command.Command != request.Command))
                    {
                        PlanningIncompleteDetail = "A command observation was bound to a different city or command.";
                        return;
                    }
                }
            }

            if (contextCapability != null && contextCapability.CommandRequest != null)
            {
                if (!contextCapability.CommandRequest.Matches(Context))
                {
                    PlanningIncompleteDetail = "The command observation does not satisfy its batch challenge.";
                    return;
                }

                if (verifiedCommandCount != 1)
                {
                    PlanningIncompleteDetail = "A command re-read must contain exactly one verified native ranking.";
                    return;
                }
            }

            IsPlanningComplete = true;
            PlanningIncompleteDetail = string.Empty;
        }
    }
}
