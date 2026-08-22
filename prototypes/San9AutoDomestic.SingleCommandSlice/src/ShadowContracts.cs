using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;
using San9AutoDomestic.V8Transaction;

namespace San9AutoDomestic.SingleCommandSlice
{
    /// <summary>
    /// Permanent safety declaration for this prototype.  No state transition in
    /// this assembly can turn any of these values into a live authorization.
    /// </summary>
    public static class ShadowBuildContract
    {
        public static bool ShadowOnly { get { return true; } }

        public static bool LiveAuthorized { get { return false; } }

        public static bool SupportsProcessAccess { get { return false; } }

        public static bool SupportsIpc { get { return false; } }

        public static bool SupportsNativeCallbacks { get { return false; } }
    }

    public enum ShadowSliceState
    {
        Ready = 1,
        TicketOutstanding = 2,
        Evaluating = 3,
        AwaitingCommit = 4,
        Completed = 5,
        Stopped = 6,
        Halted = 7
    }

    public enum ShadowTicketIssueRejection
    {
        None = 0,
        NotRunning = 1,
        OutstandingWork = 2,
        OutOfOrder = 3
    }

    public enum ShadowStepDisposition
    {
        Skipped = 1,
        Validated = 2,
        Halted = 3
    }

    public enum ShadowSkipReason
    {
        None = 0,
        NotOwnedByPlayer = 1,
        DelegatedCity = 2,
        GreyedOut = 3,
        CommandBlocked = 4,
        InsufficientOfficers = 5,
        InsufficientFunds = 6,
        ReserveMoneyProtected = 7
    }

    public enum ShadowHaltReason
    {
        None = 0,
        OutOfOrderTicketRequest = 1,
        TicketMismatch = 2,
        TicketAlreadyConsumed = 3,
        ObservationFailure = 4,
        GenerationChanged = 5,
        ObservationMismatch = 6,
        CommitMismatch = 7,
        EvaluationInvalidated = 8
    }

    /// <summary>
    /// The sole data source understood by this prototype.  Implementations are
    /// synthetic test fakes; this interface conveys no Core trusted capability.
    /// </summary>
    public interface IShadowObservationProvider
    {
        ShadowBatchObservation CaptureBatch(
            string profileId,
            string configurationFingerprint);

        ShadowCommandObservation ObserveCommand(
            ShadowCommandObservationRequest request);
    }

    public sealed class ShadowCorpsSeed
    {
        public ShadowCorpsSeed(int corpsId, int money)
        {
            if (corpsId < 0 || corpsId >= SingleCommandRequest.CorpsCount)
            {
                throw new ArgumentOutOfRangeException("corpsId");
            }

            if (money < 0 || money > SingleCommandRequest.GameMoneyMaximum)
            {
                throw new ArgumentOutOfRangeException("money");
            }

            CorpsId = corpsId;
            Money = money;
        }

        public int CorpsId { get; private set; }

        public int Money { get; private set; }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }
    }

    public sealed class ShadowCitySeed
    {
        private readonly ReadOnlyCollection<int> _availableOfficerIds;

        public ShadowCitySeed(
            int cityId,
            int ownerForceId,
            int corpsId,
            bool isDirectlyControlled,
            IEnumerable<int> availableOfficerIds)
        {
            if (cityId < 0 || cityId >= SingleCommandRequest.CityCount)
            {
                throw new ArgumentOutOfRangeException("cityId");
            }

            if (ownerForceId < 0)
            {
                throw new ArgumentOutOfRangeException("ownerForceId");
            }

            if (corpsId < 0 || corpsId >= SingleCommandRequest.CorpsCount)
            {
                throw new ArgumentOutOfRangeException("corpsId");
            }

            _availableOfficerIds = new ReadOnlyCollection<int>(
                CopyOfficerIds(availableOfficerIds, "availableOfficerIds"));
            CityId = cityId;
            OwnerForceId = ownerForceId;
            CorpsId = corpsId;
            IsDirectlyControlled = isDirectlyControlled;
        }

        public int CityId { get; private set; }

        public int OwnerForceId { get; private set; }

        public int CorpsId { get; private set; }

        public bool IsDirectlyControlled { get; private set; }

        public ReadOnlyCollection<int> AvailableOfficerIds
        {
            get { return _availableOfficerIds; }
        }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }

        internal static List<int> CopyOfficerIds(IEnumerable<int> officerIds, string parameterName)
        {
            if (officerIds == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            List<int> copied = new List<int>(officerIds);
            HashSet<int> unique = new HashSet<int>();
            foreach (int officerId in copied)
            {
                if (officerId < 0 || officerId >= SingleCommandRequest.OfficerCount)
                {
                    throw new ArgumentOutOfRangeException(parameterName);
                }

                if (!unique.Add(officerId))
                {
                    throw new ArgumentException("Officer ids must be unique.", parameterName);
                }
            }

            return copied;
        }
    }

    public sealed class ShadowBatchObservation
    {
        private readonly ReadOnlyCollection<ShadowCorpsSeed> _corps;
        private readonly ReadOnlyCollection<ShadowCitySeed> _cities;

        public ShadowBatchObservation(
            int playerForceId,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            IEnumerable<ShadowCorpsSeed> corps,
            IEnumerable<ShadowCitySeed> cities)
        {
            if (playerForceId < 0)
            {
                throw new ArgumentOutOfRangeException("playerForceId");
            }

            if (contextDigest == null)
            {
                throw new ArgumentNullException("contextDigest");
            }

            if (generationDigest == null)
            {
                throw new ArgumentNullException("generationDigest");
            }

            if (corps == null)
            {
                throw new ArgumentNullException("corps");
            }

            if (cities == null)
            {
                throw new ArgumentNullException("cities");
            }

            List<ShadowCorpsSeed> corpsList = new List<ShadowCorpsSeed>(corps);
            Dictionary<int, ShadowCorpsSeed> corpsById = new Dictionary<int, ShadowCorpsSeed>();
            foreach (ShadowCorpsSeed item in corpsList)
            {
                if (item == null)
                {
                    throw new ArgumentException("Corps seeds must not contain null.", "corps");
                }

                if (corpsById.ContainsKey(item.CorpsId))
                {
                    throw new ArgumentException("Corps ids must be unique.", "corps");
                }

                corpsById.Add(item.CorpsId, item);
            }

            List<ShadowCitySeed> cityList = new List<ShadowCitySeed>(cities);
            HashSet<int> cityIds = new HashSet<int>();
            HashSet<int> globallyAssignedOfficerIds = new HashSet<int>();
            foreach (ShadowCitySeed item in cityList)
            {
                if (item == null)
                {
                    throw new ArgumentException("City seeds must not contain null.", "cities");
                }

                if (!cityIds.Add(item.CityId))
                {
                    throw new ArgumentException("City ids must be unique.", "cities");
                }

                if (!corpsById.ContainsKey(item.CorpsId))
                {
                    throw new ArgumentException("Every city must reference a seeded corps.", "cities");
                }

                foreach (int officerId in item.AvailableOfficerIds)
                {
                    if (!globallyAssignedOfficerIds.Add(officerId))
                    {
                        throw new ArgumentException(
                            "An available officer may belong to only one frozen city seed.",
                            "cities");
                    }
                }
            }

            PlayerForceId = playerForceId;
            ContextDigest = contextDigest;
            GenerationDigest = generationDigest;
            _corps = new ReadOnlyCollection<ShadowCorpsSeed>(corpsList);
            _cities = new ReadOnlyCollection<ShadowCitySeed>(cityList);
        }

        public int PlayerForceId { get; private set; }

        public FixedDigest ContextDigest { get; private set; }

        public FixedDigest GenerationDigest { get; private set; }

        public ReadOnlyCollection<ShadowCorpsSeed> Corps
        {
            get { return _corps; }
        }

        public ReadOnlyCollection<ShadowCitySeed> Cities
        {
            get { return _cities; }
        }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }
    }

    public sealed class ShadowCommandObservationRequest
    {
        internal ShadowCommandObservationRequest(
            Guid runId,
            string profileId,
            string configurationFingerprint,
            int cityOrdinal,
            int taskOrdinal,
            int cityId,
            DomesticCommand command,
            FixedDigest expectedGenerationDigest)
        {
            RunId = runId;
            ProfileId = profileId;
            ConfigurationFingerprint = configurationFingerprint;
            CityOrdinal = cityOrdinal;
            TaskOrdinal = taskOrdinal;
            CityId = cityId;
            Command = command;
            ExpectedGenerationDigest = expectedGenerationDigest;
        }

        public Guid RunId { get; private set; }

        public string ProfileId { get; private set; }

        public string ConfigurationFingerprint { get; private set; }

        public int CityOrdinal { get; private set; }

        public int TaskOrdinal { get; private set; }

        public int CityId { get; private set; }

        public DomesticCommand Command { get; private set; }

        public FixedDigest ExpectedGenerationDigest { get; private set; }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }
    }

    public sealed class ShadowCommandObservation
    {
        private readonly ReadOnlyCollection<int> _rankedCandidateOfficerIds;

        public ShadowCommandObservation(
            FixedDigest generationDigest,
            int cityId,
            int ownerForceId,
            int corpsId,
            bool isDirectlyControlled,
            DomesticCommand command,
            bool canExecute,
            NativeCommandBlockReason blockReason,
            IEnumerable<int> rankedCandidateOfficerIds)
        {
            if (generationDigest == null)
            {
                throw new ArgumentNullException("generationDigest");
            }

            if (cityId < 0 || cityId >= SingleCommandRequest.CityCount)
            {
                throw new ArgumentOutOfRangeException("cityId");
            }

            if (ownerForceId < 0)
            {
                throw new ArgumentOutOfRangeException("ownerForceId");
            }

            if (corpsId < 0 || corpsId >= SingleCommandRequest.CorpsCount)
            {
                throw new ArgumentOutOfRangeException("corpsId");
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
                throw new ArgumentException("Executable observations cannot have a block reason.", "blockReason");
            }

            if (!canExecute && blockReason == NativeCommandBlockReason.None)
            {
                throw new ArgumentException("Blocked observations require a reason.", "blockReason");
            }

            _rankedCandidateOfficerIds = new ReadOnlyCollection<int>(
                ShadowCitySeed.CopyOfficerIds(rankedCandidateOfficerIds, "rankedCandidateOfficerIds"));
            GenerationDigest = generationDigest;
            CityId = cityId;
            OwnerForceId = ownerForceId;
            CorpsId = corpsId;
            IsDirectlyControlled = isDirectlyControlled;
            Command = command;
            CanExecute = canExecute;
            BlockReason = blockReason;
        }

        public FixedDigest GenerationDigest { get; private set; }

        public int CityId { get; private set; }

        public int OwnerForceId { get; private set; }

        public int CorpsId { get; private set; }

        public bool IsDirectlyControlled { get; private set; }

        public DomesticCommand Command { get; private set; }

        public bool CanExecute { get; private set; }

        public NativeCommandBlockReason BlockReason { get; private set; }

        public ReadOnlyCollection<int> RankedCandidateOfficerIds
        {
            get { return _rankedCandidateOfficerIds; }
        }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }
    }
}
