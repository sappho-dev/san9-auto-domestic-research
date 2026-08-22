using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.Linq;
using San9AutoDomestic.Core.Collections;
using San9AutoDomestic.Core.Configuration;

namespace San9AutoDomestic.Core.Planning
{
    public enum PlanningMode
    {
        Preview = 1,
        Submission = 2
    }

    public enum PlanningOutcome
    {
        Completed = 1,
        Aborted = 2
    }

    public enum PlanningDecision
    {
        Projected = 1,
        Skipped = 2,
        Discarded = 3
    }

    public enum PlanningSkipReason
    {
        None = 0,
        CityControlChanged = 1,
        NativeGreyedOut = 2,
        AlreadyExecuted = 3,
        ReachedLimit = 4,
        CityInCombat = 5,
        CityConfused = 6,
        NoTroops = 7,
        NativeInsufficientFunds = 8,
        NativeStateChanged = 9,
        InsufficientOfficers = 10,
        InsufficientMoney = 11,
        ReserveMoneyProtected = 12,
        AlreadyPlannedInThisBatch = 13
    }

    public static class PlanningSkipPolicy
    {
        public static bool IsKnownBusinessCondition(PlanningSkipReason reason)
        {
            switch (reason)
            {
                case PlanningSkipReason.CityControlChanged:
                case PlanningSkipReason.NativeGreyedOut:
                case PlanningSkipReason.AlreadyExecuted:
                case PlanningSkipReason.ReachedLimit:
                case PlanningSkipReason.CityInCombat:
                case PlanningSkipReason.CityConfused:
                case PlanningSkipReason.NoTroops:
                case PlanningSkipReason.NativeInsufficientFunds:
                case PlanningSkipReason.NativeStateChanged:
                case PlanningSkipReason.InsufficientOfficers:
                case PlanningSkipReason.InsufficientMoney:
                case PlanningSkipReason.ReserveMoneyProtected:
                case PlanningSkipReason.AlreadyPlannedInThisBatch:
                    return true;
                default:
                    return false;
            }
        }
    }

    public enum PlanningFatalReason
    {
        None = 0,
        InvalidPlanningMode = 1,
        SnapshotIncomplete = 2,
        BatchContextMissing = 3,
        BatchContextChanged = 4,
        PlanContextMismatch = 5,
        UnknownFacilityType = 6,
        CityMissingAfterSnapshot = 7,
        MissingCommandSnapshot = 8,
        InvalidNativeCommandState = 9,
        NativeSelectionUnavailable = 10,
        InvalidCandidateSnapshot = 11,
        MissingCorpsMoney = 12,
        InvalidEstimatedCost = 13,
        PreviewOnlyPlan = 14,
        StepwiseSubmissionRequired = 15,
        FacilityIdentityChanged = 16
    }

    public sealed class PlanningFatalIssue
    {
        internal PlanningFatalIssue(
            PlanningFatalReason reason,
            string detail,
            int? cityId,
            DomesticCommand? command)
        {
            if (reason == PlanningFatalReason.None)
            {
                throw new ArgumentOutOfRangeException("reason");
            }

            Reason = reason;
            Detail = detail ?? string.Empty;
            CityId = cityId;
            Command = command;
        }

        public PlanningFatalReason Reason { get; private set; }

        public string Detail { get; private set; }

        public int? CityId { get; private set; }

        public DomesticCommand? Command { get; private set; }
    }

    public sealed class TaskPlanningResult
    {
        private readonly ReadOnlyCollection<int> _selectedOfficerIds;

        internal TaskPlanningResult(
            DomesticCommand command,
            PlanningDecision decision,
            PlanningSkipReason skipReason,
            string detail,
            int candidateCount,
            IEnumerable<int> selectedOfficerIds,
            int estimatedCost,
            int? moneyBefore,
            int? moneyAfter)
        {
            Command = command;
            Decision = decision;
            SkipReason = skipReason;
            Detail = detail ?? string.Empty;
            CandidateCount = candidateCount;
            _selectedOfficerIds = new ReadOnlyCollection<int>(new List<int>(selectedOfficerIds));
            EstimatedCost = estimatedCost;
            MoneyBefore = moneyBefore;
            MoneyAfter = moneyAfter;
        }

        public DomesticCommand Command { get; private set; }

        public PlanningDecision Decision { get; private set; }

        public PlanningSkipReason SkipReason { get; private set; }

        public string Detail { get; private set; }

        public int CandidateCount { get; private set; }

        public ReadOnlyCollection<int> SelectedOfficerIds
        {
            get { return _selectedOfficerIds; }
        }

        public int EstimatedCost { get; private set; }

        public int? MoneyBefore { get; private set; }

        public int? MoneyAfter { get; private set; }

        internal TaskPlanningResult DiscardProjection(string fatalDetail)
        {
            if (Decision != PlanningDecision.Projected)
            {
                return this;
            }

            return new TaskPlanningResult(
                Command,
                PlanningDecision.Discarded,
                PlanningSkipReason.None,
                "Projection discarded because the batch aborted: " + (fatalDetail ?? string.Empty),
                CandidateCount,
                new int[0],
                0,
                MoneyBefore,
                MoneyBefore);
        }
    }

    public sealed class CityPlanningResult
    {
        private readonly ReadOnlyCollection<TaskPlanningResult> _tasks;

        internal CityPlanningResult(int cityId, string cityName, IEnumerable<TaskPlanningResult> tasks)
        {
            CityId = cityId;
            CityName = cityName ?? string.Empty;
            _tasks = new ReadOnlyCollection<TaskPlanningResult>(new List<TaskPlanningResult>(tasks));
        }

        public int CityId { get; private set; }

        public string CityName { get; private set; }

        public ReadOnlyCollection<TaskPlanningResult> Tasks
        {
            get { return _tasks; }
        }

        internal CityPlanningResult DiscardProjections(string fatalDetail)
        {
            return new CityPlanningResult(
                CityId,
                CityName,
                _tasks.Select(item => item.DiscardProjection(fatalDetail)));
        }
    }

    public sealed class PlanningResult
    {
        private readonly ReadOnlyCollection<CityPlanningResult> _cities;
        private readonly ReadOnlyCollection<ScopeSkip> _scopeSkips;
        private readonly ReadOnlyMap<int, int> _remainingMoneyByCorps;

        internal PlanningResult(
            IEnumerable<CityPlanningResult> cities,
            IEnumerable<ScopeSkip> scopeSkips,
            IDictionary<int, int> remainingMoneyByCorps,
            PlanningMode mode,
            PlanningOutcome outcome,
            PlanningFatalIssue fatalIssue)
        {
            if (outcome == PlanningOutcome.Aborted && fatalIssue == null)
            {
                throw new ArgumentException("An aborted result requires a fatal issue.", "fatalIssue");
            }

            if (outcome == PlanningOutcome.Completed && fatalIssue != null)
            {
                throw new ArgumentException("A completed result cannot contain a fatal issue.", "fatalIssue");
            }

            List<CityPlanningResult> cityList = new List<CityPlanningResult>(cities);
            _cities = new ReadOnlyCollection<CityPlanningResult>(cityList);
            _scopeSkips = new ReadOnlyCollection<ScopeSkip>(new List<ScopeSkip>(scopeSkips));
            _remainingMoneyByCorps = new ReadOnlyMap<int, int>(remainingMoneyByCorps);
            Mode = mode;
            Outcome = outcome;
            FatalIssue = fatalIssue;

            int projected = 0;
            int skipped = 0;
            int discarded = 0;
            foreach (CityPlanningResult city in cityList)
            {
                foreach (TaskPlanningResult task in city.Tasks)
                {
                    if (task.Decision == PlanningDecision.Projected)
                    {
                        if (outcome == PlanningOutcome.Aborted)
                        {
                            throw new ArgumentException(
                                "An aborted result must not expose usable projections.",
                                "cities");
                        }

                        projected++;
                    }
                    else if (task.Decision == PlanningDecision.Skipped)
                    {
                        skipped++;
                    }
                    else if (task.Decision == PlanningDecision.Discarded)
                    {
                        discarded++;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException("cities", "Unknown planning decision.");
                    }
                }
            }

            ProjectedTaskCount = projected;
            SkippedTaskCount = skipped;
            DiscardedTaskCount = discarded;
        }

        public ReadOnlyCollection<CityPlanningResult> Cities
        {
            get { return _cities; }
        }

        public ReadOnlyCollection<ScopeSkip> ScopeSkips
        {
            get { return _scopeSkips; }
        }

        public ReadOnlyMap<int, int> RemainingMoneyByCorps
        {
            get { return _remainingMoneyByCorps; }
        }

        public PlanningMode Mode { get; private set; }

        public PlanningOutcome Outcome { get; private set; }

        public bool IsAborted
        {
            get { return Outcome == PlanningOutcome.Aborted; }
        }

        public PlanningFatalIssue FatalIssue { get; private set; }

        /// <summary>
        /// Full-batch planner output is never a commit authorization. A future
        /// stepwise validator must return a different, narrowly scoped type.
        /// </summary>
        public bool CommitAuthorized
        {
            get { return false; }
        }

        public int ProjectedTaskCount { get; private set; }

        public int SkippedTaskCount { get; private set; }

        public int DiscardedTaskCount { get; private set; }
    }
}
