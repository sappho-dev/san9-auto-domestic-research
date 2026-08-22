using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Core.Planning
{
    public sealed class DomesticPlanner
    {
        public PlanningResult Build(ExecutionPlan plan, GameSnapshot snapshot)
        {
            return Build(plan, snapshot, PlanningMode.Preview);
        }

        public PlanningResult Build(
            ExecutionPlan plan,
            GameSnapshot snapshot,
            PlanningMode mode)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            CityQueueSnapshot queue = CityQueueSnapshot.Capture(plan, snapshot);
            return Build(plan, queue, snapshot, mode);
        }

        public PlanningResult Build(
            ExecutionPlan plan,
            CityQueueSnapshot queue,
            GameSnapshot revalidatedSnapshot)
        {
            return Build(plan, queue, revalidatedSnapshot, PlanningMode.Preview);
        }

        public PlanningResult Build(
            ExecutionPlan plan,
            CityQueueSnapshot queue,
            GameSnapshot revalidatedSnapshot,
            PlanningMode mode)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            if (revalidatedSnapshot == null)
            {
                throw new ArgumentNullException("revalidatedSnapshot");
            }

            IDictionary<int, int> remainingMoney = revalidatedSnapshot.CopyMoneyByCorps();
            List<CityPlanningResult> cityResults = new List<CityPlanningResult>();

            PlanningFatalIssue startFailure = ValidateStart(
                plan,
                queue,
                revalidatedSnapshot,
                mode);
            if (startFailure != null)
            {
                return Aborted(
                    cityResults,
                    queue.ScopeSkips,
                    remainingMoney,
                    mode,
                    startFailure);
            }

            HashSet<int> consumedOfficerIds = new HashSet<int>();
            foreach (int cityId in queue.CityIds)
            {
                FacilitySnapshot city;
                if (!revalidatedSnapshot.TryGetFacility(cityId, out city))
                {
                    return Aborted(
                        cityResults,
                        queue.ScopeSkips,
                        remainingMoney,
                        mode,
                        Fatal(
                            PlanningFatalReason.CityMissingAfterSnapshot,
                            "City no longer exists in the revalidated snapshot.",
                            cityId,
                            null));
                }

                if (city.FacilityType == FacilityType.Unknown)
                {
                    return Aborted(
                        cityResults,
                        queue.ScopeSkips,
                        remainingMoney,
                        mode,
                        Fatal(
                            PlanningFatalReason.UnknownFacilityType,
                            "Revalidated facility type could not be verified.",
                            city.Id,
                            null));
                }

                if (city.FacilityType != FacilityType.City)
                {
                    return Aborted(
                        cityResults,
                        queue.ScopeSkips,
                        remainingMoney,
                        mode,
                        Fatal(
                            PlanningFatalReason.FacilityIdentityChanged,
                            "A queued city id now resolves to a different facility type.",
                            city.Id,
                            null));
                }

                if (city.OwnerForceId != revalidatedSnapshot.PlayerForceId
                    || !city.IsDirectlyControlled)
                {
                    cityResults.Add(CreateAllSkippedCity(
                        city.Id,
                        city.Name,
                        plan,
                        PlanningSkipReason.CityControlChanged,
                        "City ownership or direct-control state changed after queue capture."));
                    continue;
                }

                CityPlanOutcome cityOutcome = PlanCity(
                    plan,
                    city,
                    remainingMoney,
                    consumedOfficerIds);
                cityResults.Add(cityOutcome.City);
                if (cityOutcome.FatalIssue != null)
                {
                    return Aborted(
                        cityResults,
                        queue.ScopeSkips,
                        remainingMoney,
                        mode,
                        cityOutcome.FatalIssue);
                }
            }

            return new PlanningResult(
                cityResults,
                queue.ScopeSkips,
                remainingMoney,
                mode,
                PlanningOutcome.Completed,
                null);
        }

        private static PlanningFatalIssue ValidateStart(
            ExecutionPlan plan,
            CityQueueSnapshot queue,
            GameSnapshot snapshot,
            PlanningMode mode)
        {
            if (!Enum.IsDefined(typeof(PlanningMode), mode))
            {
                return Fatal(
                    PlanningFatalReason.InvalidPlanningMode,
                    "Planning mode is not supported.",
                    null,
                    null);
            }

            if (mode == PlanningMode.Submission)
            {
                if (plan.PreviewOnly || !plan.EligibleForStepwiseValidation)
                {
                    return Fatal(
                        PlanningFatalReason.PreviewOnlyPlan,
                        "This plan contains a preview-only selection policy and cannot authorize submission.",
                        null,
                        null);
                }

                return Fatal(
                    PlanningFatalReason.StepwiseSubmissionRequired,
                    "A full-batch plan is never a submission authorization. Revalidate one command at a time.",
                    null,
                    null);
            }

            BatchContext batch = queue.Context;
            if (batch == null || snapshot.Context == null)
            {
                return Fatal(
                    PlanningFatalReason.BatchContextMissing,
                    "Batch or snapshot context is missing.",
                    null,
                    null);
            }

            if (!string.Equals(batch.ProfileId, plan.ProfileId, StringComparison.Ordinal)
                || !string.Equals(
                    batch.ConfigurationFingerprint,
                    plan.ConfigurationFingerprint,
                    StringComparison.Ordinal))
            {
                return Fatal(
                    PlanningFatalReason.PlanContextMismatch,
                    "The city queue was captured for a different profile or configuration fingerprint.",
                    null,
                    null);
            }

            GameSnapshotContext current = snapshot.Context;
            if (batch.InitialReadiness != SnapshotReadiness.PlanningReady
                || current.Readiness != SnapshotReadiness.PlanningReady)
            {
                string detail = current.Detail;
                if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = "The snapshot is not marked planning-ready.";
                }

                return Fatal(
                    PlanningFatalReason.SnapshotIncomplete,
                    detail,
                    null,
                    null);
            }

            if (!batch.ProcessId.HasValue
                || !batch.ProcessStartUtcTicks.HasValue
                || batch.ScenarioToken == null
                || batch.TurnToken == null
                || batch.PhaseToken == null
                || !batch.InitialSnapshotGeneration.HasValue
                || !current.ProcessId.HasValue
                || !current.ProcessStartUtcTicks.HasValue
                || current.ScenarioToken == null
                || current.TurnToken == null
                || current.PhaseToken == null
                || !current.SnapshotGeneration.HasValue)
            {
                return Fatal(
                    PlanningFatalReason.BatchContextMissing,
                    "Process, player, scenario, turn, phase, and generation context is required for planning.",
                    null,
                    null);
            }

            if (batch.ProcessId.Value != current.ProcessId.Value
                || batch.ProcessStartUtcTicks.Value != current.ProcessStartUtcTicks.Value
                || batch.PlayerForceId != snapshot.PlayerForceId
                || !SameToken(batch.ScenarioToken, current.ScenarioToken)
                || !SameToken(batch.TurnToken, current.TurnToken)
                || !SameToken(batch.PhaseToken, current.PhaseToken)
                || current.SnapshotGeneration.Value < batch.InitialSnapshotGeneration.Value)
            {
                return Fatal(
                    PlanningFatalReason.BatchContextChanged,
                    "Process, player, scenario, turn, phase, verification state, or snapshot generation changed.",
                    null,
                    null);
            }

            foreach (ScopeSkip skip in queue.ScopeSkips)
            {
                if (skip.Reason == ScopeSkipReason.UnknownFacilityType)
                {
                    return Fatal(
                        PlanningFatalReason.UnknownFacilityType,
                        "Facility " + skip.FacilityId.ToString(CultureInfo.InvariantCulture)
                            + " has an unverified facility type.",
                        skip.FacilityId,
                        null);
                }
            }

            foreach (FacilitySnapshot facility in snapshot.Facilities)
            {
                if (facility.FacilityType == FacilityType.Unknown)
                {
                    return Fatal(
                        PlanningFatalReason.UnknownFacilityType,
                        "Revalidated facility "
                            + facility.Id.ToString(CultureInfo.InvariantCulture)
                            + " has an unverified facility type.",
                        facility.Id,
                        null);
                }
            }

            if (!batch.InitialPlanningComplete || !snapshot.IsPlanningComplete)
            {
                string detail = !batch.InitialPlanningComplete
                    ? batch.InitialPlanningIncompleteDetail
                    : snapshot.PlanningIncompleteDetail;
                if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = "The planning-ready snapshot failed cross-field completeness validation.";
                }

                return Fatal(
                    PlanningFatalReason.SnapshotIncomplete,
                    detail,
                    null,
                    null);
            }

            return null;
        }

        private static bool SameToken(GameContextToken expected, GameContextToken current)
        {
            return expected != null
                && current != null
                && expected.IsVerified == current.IsVerified
                && string.Equals(expected.Value, current.Value, StringComparison.Ordinal);
        }

        private static CityPlanOutcome PlanCity(
            ExecutionPlan plan,
            FacilitySnapshot city,
            IDictionary<int, int> remainingMoney,
            ISet<int> consumedOfficerIds)
        {
            List<TaskPlanningResult> taskResults = new List<TaskPlanningResult>();
            HashSet<DomesticCommand> plannedCommands = new HashSet<DomesticCommand>();
            foreach (FrozenTaskPlan task in plan.Tasks)
            {
                if (plannedCommands.Contains(task.Command))
                {
                    taskResults.Add(Skipped(
                        task.Command,
                        PlanningSkipReason.AlreadyPlannedInThisBatch,
                        "The same command was already planned for this city in this batch.",
                        0,
                        null,
                        null));
                    continue;
                }

                NativeCommandSnapshot commandSnapshot;
                if (!city.TryGetCommand(task.Command, out commandSnapshot))
                {
                    return FatalCity(
                        city,
                        taskResults,
                        PlanningFatalReason.MissingCommandSnapshot,
                        "No native command snapshot is available.",
                        task.Command);
                }

                if (!commandSnapshot.CanExecute)
                {
                    PlanningSkipReason businessReason;
                    if (!TryMapBusinessBlockReason(commandSnapshot.BlockReason, out businessReason))
                    {
                        return FatalCity(
                            city,
                            taskResults,
                            PlanningFatalReason.InvalidNativeCommandState,
                            "Native command has an unclassified block reason: "
                                + commandSnapshot.BlockReason + ".",
                            task.Command);
                    }

                    taskResults.Add(Skipped(
                        task.Command,
                        businessReason,
                        "Native command is unavailable: " + commandSnapshot.BlockReason + ".",
                        0,
                        null,
                        null));
                    continue;
                }

                IList<OfficerSnapshot> candidates;
                string candidateFailure;
                if (!TryBuildCandidates(
                    task,
                    city,
                    commandSnapshot,
                    consumedOfficerIds,
                    out candidates,
                    out candidateFailure))
                {
                    return FatalCity(
                        city,
                        taskResults,
                        PlanningFatalReason.InvalidCandidateSnapshot,
                        candidateFailure,
                        task.Command);
                }

                if (task.SelectionPolicy == SelectionPolicy.NativeBest
                    && !commandSnapshot.HasVerifiedNativeRanking)
                {
                    return FatalCity(
                        city,
                        taskResults,
                        PlanningFatalReason.NativeSelectionUnavailable,
                        "The plan requires verified native ranking, but the snapshot does not provide it.",
                        task.Command);
                }

                int selectedCount;
                if (task.RequireExactCount)
                {
                    selectedCount = task.MaxOfficers;
                    if (candidates.Count < selectedCount)
                    {
                        taskResults.Add(Skipped(
                            task.Command,
                            PlanningSkipReason.InsufficientOfficers,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Requires exactly {0} officers but only {1} eligible officers remain.",
                                selectedCount,
                                candidates.Count),
                            candidates.Count,
                            null,
                            null));
                        continue;
                    }
                }
                else
                {
                    if (candidates.Count < task.MinOfficers)
                    {
                        taskResults.Add(Skipped(
                            task.Command,
                            PlanningSkipReason.InsufficientOfficers,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Requires at least {0} officers but only {1} eligible officers remain.",
                                task.MinOfficers,
                                candidates.Count),
                            candidates.Count,
                            null,
                            null));
                        continue;
                    }

                    selectedCount = Math.Min(task.MaxOfficers, candidates.Count);
                }

                List<int> selectedIds = candidates.Take(selectedCount).Select(item => item.Id).ToList();
                int moneyBefore;
                if (!remainingMoney.TryGetValue(city.CorpsId, out moneyBefore))
                {
                    return FatalCity(
                        city,
                        taskResults,
                        PlanningFatalReason.MissingCorpsMoney,
                        "No money snapshot is available for the city's corps.",
                        task.Command);
                }

                long estimatedCostLong = (long)commandSnapshot.EstimatedCostPerOfficer * selectedCount;
                if (estimatedCostLong < 0 || estimatedCostLong > int.MaxValue)
                {
                    return FatalCity(
                        city,
                        taskResults,
                        PlanningFatalReason.InvalidEstimatedCost,
                        "Estimated command cost is negative or exceeds the supported integer range.",
                        task.Command);
                }

                int estimatedCost = (int)estimatedCostLong;
                if (moneyBefore < estimatedCost)
                {
                    taskResults.Add(Skipped(
                        task.Command,
                        PlanningSkipReason.InsufficientMoney,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Needs {0} money for {1} officers but only {2} is available.",
                            estimatedCost,
                            selectedCount,
                            moneyBefore),
                        candidates.Count,
                        moneyBefore,
                        moneyBefore,
                        estimatedCost));
                    continue;
                }

                int moneyAfter = moneyBefore - estimatedCost;
                if (moneyAfter < task.ReserveMoney)
                {
                    taskResults.Add(Skipped(
                        task.Command,
                        PlanningSkipReason.ReserveMoneyProtected,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Projected balance {0} is below the configured reserve {1}.",
                            moneyAfter,
                            task.ReserveMoney),
                        candidates.Count,
                        moneyBefore,
                        moneyBefore,
                        estimatedCost));
                    continue;
                }

                foreach (int officerId in selectedIds)
                {
                    consumedOfficerIds.Add(officerId);
                }

                remainingMoney[city.CorpsId] = moneyAfter;
                plannedCommands.Add(task.Command);
                taskResults.Add(new TaskPlanningResult(
                    task.Command,
                    PlanningDecision.Projected,
                    PlanningSkipReason.None,
                    "Planned with the best currently eligible officers.",
                    candidates.Count,
                    selectedIds,
                    estimatedCost,
                    moneyBefore,
                    moneyAfter));
            }

            return new CityPlanOutcome(
                new CityPlanningResult(city.Id, city.Name, taskResults),
                null);
        }

        private static bool TryBuildCandidates(
            FrozenTaskPlan task,
            FacilitySnapshot city,
            NativeCommandSnapshot commandSnapshot,
            ISet<int> consumedOfficerIds,
            out IList<OfficerSnapshot> candidates,
            out string failure)
        {
            List<OfficerSnapshot> result = new List<OfficerSnapshot>();
            HashSet<int> seenIds = new HashSet<int>();
            foreach (int officerId in commandSnapshot.RankedCandidateOfficerIds)
            {
                OfficerSnapshot officer;
                if (!seenIds.Add(officerId))
                {
                    candidates = new OfficerSnapshot[0];
                    failure = "Native candidate ranking contains a duplicate officer id.";
                    return false;
                }

                if (!city.TryGetOfficer(officerId, out officer))
                {
                    candidates = new OfficerSnapshot[0];
                    failure = "Native candidate ranking references an officer outside the city snapshot.";
                    return false;
                }

                if (!officer.CanAct)
                {
                    candidates = new OfficerSnapshot[0];
                    failure = "Native candidate ranking contains an officer who cannot act.";
                    return false;
                }

                if (!consumedOfficerIds.Contains(officerId))
                {
                    result.Add(officer);
                }
            }

            if (task.SelectionPolicy == SelectionPolicy.VerifiedStatFallback)
            {
                result = result
                    .OrderByDescending(item => RelevantAbility(task.Command, item))
                    .ToList();
            }

            candidates = result;
            failure = string.Empty;
            return true;
        }

        private static int RelevantAbility(DomesticCommand command, OfficerSnapshot officer)
        {
            switch (command)
            {
                case DomesticCommand.Patrol:
                    return officer.EffectiveIntelligence;
                case DomesticCommand.Commerce:
                case DomesticCommand.Cultivate:
                    return officer.EffectivePolitics;
                case DomesticCommand.Train:
                    return officer.EffectiveMight;
                case DomesticCommand.Repair:
                    return officer.EffectiveLeadership;
                default:
                    throw new InvalidOperationException(
                        "No relevant-ability mapping exists for command " + command + ".");
            }
        }

        private static CityPlanningResult CreateAllSkippedCity(
            int cityId,
            string cityName,
            ExecutionPlan plan,
            PlanningSkipReason reason,
            string detail)
        {
            List<TaskPlanningResult> results = new List<TaskPlanningResult>();
            foreach (FrozenTaskPlan task in plan.Tasks)
            {
                results.Add(Skipped(task.Command, reason, detail, 0, null, null));
            }

            return new CityPlanningResult(cityId, cityName, results);
        }

        private static TaskPlanningResult Skipped(
            DomesticCommand command,
            PlanningSkipReason reason,
            string detail,
            int candidateCount,
            int? moneyBefore,
            int? moneyAfter)
        {
            return Skipped(command, reason, detail, candidateCount, moneyBefore, moneyAfter, 0);
        }

        private static TaskPlanningResult Skipped(
            DomesticCommand command,
            PlanningSkipReason reason,
            string detail,
            int candidateCount,
            int? moneyBefore,
            int? moneyAfter,
            int estimatedCost)
        {
            if (!PlanningSkipPolicy.IsKnownBusinessCondition(reason))
            {
                throw new InvalidOperationException(
                    "Technical uncertainty cannot be represented as a business skip: " + reason + ".");
            }

            return new TaskPlanningResult(
                command,
                PlanningDecision.Skipped,
                reason,
                detail,
                candidateCount,
                new int[0],
                estimatedCost,
                moneyBefore,
                moneyAfter);
        }

        private static bool TryMapBusinessBlockReason(
            NativeCommandBlockReason reason,
            out PlanningSkipReason skipReason)
        {
            switch (reason)
            {
                case NativeCommandBlockReason.GreyedOut:
                    skipReason = PlanningSkipReason.NativeGreyedOut;
                    return true;
                case NativeCommandBlockReason.AlreadyExecuted:
                    skipReason = PlanningSkipReason.AlreadyExecuted;
                    return true;
                case NativeCommandBlockReason.ReachedLimit:
                    skipReason = PlanningSkipReason.ReachedLimit;
                    return true;
                case NativeCommandBlockReason.CityInCombat:
                    skipReason = PlanningSkipReason.CityInCombat;
                    return true;
                case NativeCommandBlockReason.CityConfused:
                    skipReason = PlanningSkipReason.CityConfused;
                    return true;
                case NativeCommandBlockReason.NoTroops:
                    skipReason = PlanningSkipReason.NoTroops;
                    return true;
                case NativeCommandBlockReason.InsufficientFunds:
                    skipReason = PlanningSkipReason.NativeInsufficientFunds;
                    return true;
                case NativeCommandBlockReason.StateChanged:
                    skipReason = PlanningSkipReason.NativeStateChanged;
                    return true;
                default:
                    skipReason = PlanningSkipReason.None;
                    return false;
            }
        }

        private static CityPlanOutcome FatalCity(
            FacilitySnapshot city,
            IEnumerable<TaskPlanningResult> completedTasks,
            PlanningFatalReason reason,
            string detail,
            DomesticCommand command)
        {
            return new CityPlanOutcome(
                new CityPlanningResult(city.Id, city.Name, completedTasks),
                Fatal(reason, detail, city.Id, command));
        }

        private static PlanningFatalIssue Fatal(
            PlanningFatalReason reason,
            string detail,
            int? cityId,
            DomesticCommand? command)
        {
            return new PlanningFatalIssue(reason, detail, cityId, command);
        }

        private static PlanningResult Aborted(
            IEnumerable<CityPlanningResult> cities,
            IEnumerable<ScopeSkip> scopeSkips,
            IDictionary<int, int> remainingMoney,
            PlanningMode mode,
            PlanningFatalIssue issue)
        {
            List<CityPlanningResult> discardedCities = cities
                .Select(city => city.DiscardProjections(issue.Detail))
                .ToList();
            return new PlanningResult(
                discardedCities,
                scopeSkips,
                new Dictionary<int, int>(),
                mode,
                PlanningOutcome.Aborted,
                issue);
        }

        private sealed class CityPlanOutcome
        {
            public CityPlanOutcome(CityPlanningResult city, PlanningFatalIssue fatalIssue)
            {
                City = city;
                FatalIssue = fatalIssue;
            }

            public CityPlanningResult City { get; private set; }

            public PlanningFatalIssue FatalIssue { get; private set; }
        }
    }
}
