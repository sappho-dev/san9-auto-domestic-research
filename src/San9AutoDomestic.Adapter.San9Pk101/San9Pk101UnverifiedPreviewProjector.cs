using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public enum UnverifiedPreviewDecision
    {
        ProvisionallySelected = 1,
        Skipped = 2
    }

    public enum UnverifiedPreviewSkipReason
    {
        None = 0,
        NativeRuleBlocked = 1,
        InsufficientOfficers = 2,
        InsufficientMoney = 3,
        ReserveMoneyProtected = 4,
        DuplicateCommand = 5
    }

    public enum UnverifiedPreviewRankingSource
    {
        StaticStatProxy = 1,
        VerifiedNative = 2
    }

    public sealed class San9Pk101UnverifiedPreviewIssue
    {
        internal San9Pk101UnverifiedPreviewIssue(string code, string message)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Code { get; private set; }

        public string Message { get; private set; }

        public bool IsActionable { get { return false; } }

        public bool CommitAuthorized { get { return false; } }
    }

    public sealed class San9Pk101UnverifiedOfficerPreview
    {
        internal San9Pk101UnverifiedOfficerPreview(OfficerRankingPreview source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            PersonId = source.PersonId;
            Name = source.Name ?? string.Empty;
            SourceListIndex = source.SourceListIndex;
            Score = source.Score;
        }

        public int PersonId { get; private set; }

        public string Name { get; private set; }

        public int SourceListIndex { get; private set; }

        public int Score { get; private set; }

        public bool IsActionable { get { return false; } }

        public bool CommitAuthorized { get { return false; } }
    }

    public sealed class San9Pk101UnverifiedTaskPreview
    {
        private readonly ReadOnlyCollection<San9Pk101UnverifiedOfficerPreview> selectedOfficers;
        private readonly ReadOnlyCollection<string> failedConditionCodes;

        internal San9Pk101UnverifiedTaskPreview(
            DomesticCommand command,
            DomesticCommandKind observedCommand,
            SelectionPolicy requestedSelectionPolicy,
            UnverifiedPreviewRankingSource appliedRankingSource,
            UnverifiedPreviewDecision decision,
            UnverifiedPreviewSkipReason skipReason,
            string detail,
            int eligibleCandidateCount,
            IEnumerable<San9Pk101UnverifiedOfficerPreview> selectedOfficers,
            int estimatedCost,
            int? moneyBefore,
            int? moneyAfter,
            IEnumerable<string> failedConditionCodes)
        {
            Command = command;
            ObservedCommand = observedCommand;
            RequestedSelectionPolicy = requestedSelectionPolicy;
            AppliedRankingSource = appliedRankingSource;
            Decision = decision;
            SkipReason = skipReason;
            Detail = detail ?? string.Empty;
            EligibleCandidateCount = eligibleCandidateCount;
            this.selectedOfficers = new ReadOnlyCollection<San9Pk101UnverifiedOfficerPreview>(
                new List<San9Pk101UnverifiedOfficerPreview>(selectedOfficers));
            EstimatedCost = estimatedCost;
            MoneyBefore = moneyBefore;
            MoneyAfter = moneyAfter;
            this.failedConditionCodes = new ReadOnlyCollection<string>(
                new List<string>(failedConditionCodes));
        }

        public DomesticCommand Command { get; private set; }

        public DomesticCommandKind ObservedCommand { get; private set; }

        public SelectionPolicy RequestedSelectionPolicy { get; private set; }

        public UnverifiedPreviewRankingSource AppliedRankingSource { get; private set; }

        public UnverifiedPreviewDecision Decision { get; private set; }

        public UnverifiedPreviewSkipReason SkipReason { get; private set; }

        public string Detail { get; private set; }

        public int EligibleCandidateCount { get; private set; }

        public ReadOnlyCollection<San9Pk101UnverifiedOfficerPreview> SelectedOfficers
        {
            get { return selectedOfficers; }
        }

        public int EstimatedCost { get; private set; }

        public int? MoneyBefore { get; private set; }

        public int? MoneyAfter { get; private set; }

        public ReadOnlyCollection<string> FailedConditionCodes
        {
            get { return failedConditionCodes; }
        }

        public bool IsActionable { get { return false; } }

        public bool CommitAuthorized { get { return false; } }
    }

    public sealed class San9Pk101UnverifiedCityPreview
    {
        private readonly ReadOnlyCollection<San9Pk101UnverifiedTaskPreview> tasks;

        internal San9Pk101UnverifiedCityPreview(
            int cityId,
            string cityName,
            int corpsId,
            IEnumerable<San9Pk101UnverifiedTaskPreview> tasks)
        {
            CityId = cityId;
            CityName = cityName ?? string.Empty;
            CorpsId = corpsId;
            this.tasks = new ReadOnlyCollection<San9Pk101UnverifiedTaskPreview>(
                new List<San9Pk101UnverifiedTaskPreview>(tasks));
        }

        public int CityId { get; private set; }

        public string CityName { get; private set; }

        public int CorpsId { get; private set; }

        public ReadOnlyCollection<San9Pk101UnverifiedTaskPreview> Tasks
        {
            get { return tasks; }
        }

        public bool IsActionable { get { return false; } }

        public bool CommitAuthorized { get { return false; } }
    }

    public sealed class San9Pk101UnverifiedCorpsMoneyPreview
    {
        internal San9Pk101UnverifiedCorpsMoneyPreview(int corpsId, int remainingMoney)
        {
            CorpsId = corpsId;
            RemainingMoney = remainingMoney;
        }

        public int CorpsId { get; private set; }

        public int RemainingMoney { get; private set; }

        public bool IsActionable { get { return false; } }

        public bool CommitAuthorized { get { return false; } }
    }

    public sealed class San9Pk101UnverifiedPreviewResult
    {
        private readonly ReadOnlyCollection<San9Pk101UnverifiedCityPreview> cities;
        private readonly ReadOnlyCollection<San9Pk101UnverifiedCorpsMoneyPreview> remainingMoneyByCorps;
        private readonly ReadOnlyCollection<San9Pk101UnverifiedPreviewIssue> issues;

        internal San9Pk101UnverifiedPreviewResult(
            ExecutionPlan plan,
            bool diagnosticOnly,
            IEnumerable<San9Pk101UnverifiedCityPreview> cities,
            IEnumerable<San9Pk101UnverifiedCorpsMoneyPreview> remainingMoneyByCorps,
            IEnumerable<San9Pk101UnverifiedPreviewIssue> issues)
        {
            ProfileId = plan == null ? string.Empty : plan.ProfileId;
            DisplayName = plan == null ? string.Empty : plan.DisplayName;
            ConfigurationFingerprint = plan == null ? string.Empty : plan.ConfigurationFingerprint;
            DiagnosticOnly = diagnosticOnly;
            this.cities = new ReadOnlyCollection<San9Pk101UnverifiedCityPreview>(
                new List<San9Pk101UnverifiedCityPreview>(cities));
            this.remainingMoneyByCorps = new ReadOnlyCollection<San9Pk101UnverifiedCorpsMoneyPreview>(
                new List<San9Pk101UnverifiedCorpsMoneyPreview>(remainingMoneyByCorps));
            this.issues = new ReadOnlyCollection<San9Pk101UnverifiedPreviewIssue>(
                new List<San9Pk101UnverifiedPreviewIssue>(issues));
        }

        public string ProfileId { get; private set; }

        public string DisplayName { get; private set; }

        public string ConfigurationFingerprint { get; private set; }

        public bool DiagnosticOnly { get; private set; }

        public ReadOnlyCollection<San9Pk101UnverifiedCityPreview> Cities
        {
            get { return cities; }
        }

        public ReadOnlyCollection<San9Pk101UnverifiedCorpsMoneyPreview> RemainingMoneyByCorps
        {
            get { return remainingMoneyByCorps; }
        }

        public ReadOnlyCollection<San9Pk101UnverifiedPreviewIssue> Issues
        {
            get { return issues; }
        }

        public int ProvisionallySelectedTaskCount
        {
            get
            {
                return cities.Sum(city => city.Tasks.Count(task =>
                    task.Decision == UnverifiedPreviewDecision.ProvisionallySelected));
            }
        }

        public int SelectedOfficerCount
        {
            get { return cities.Sum(city => city.Tasks.Sum(task => task.SelectedOfficers.Count)); }
        }

        public bool IsActionable { get { return false; } }

        public bool CommitAuthorized { get { return false; } }
    }

    /// <summary>
    /// Projects a frozen configuration over one strict V2 read-only observation.
    /// The result is deliberately a separate, permanently non-actionable type.
    /// It is not a native command result and cannot authorize submission.
    /// </summary>
    public sealed class San9Pk101UnverifiedPreviewProjector
    {
        private const string DirectCondition = "DIRECT_VALID_CORPS";
        private const string CorpsCondition = "COMMON_HANDLER_CORPS_GATE";
        private const string StateCondition = "CITY_STATE_NOT_6";
        private const string CityCommonCondition = "CITY_VTABLE_90_RAW_EQUIVALENT";
        private const string CandidateCondition = "READY_CANDIDATE_AT_LEAST_1";
        private const string NativeMoneyCondition = "CORPS_MONEY_AT_LEAST_NATIVE_COST_CANDIDATE";
        private const string UnobservedNativeMoneyCondition = "NATIVE_MONEY_GATE_NOT_OBSERVED";
        private const string OrderCondition = "COMMAND_ORDER_BIT_CLEAR";
        private const string ValueCondition = "COMMAND_VALUE_GATE_OPEN";

        public San9Pk101UnverifiedPreviewResult Project(
            ExecutionPlan plan,
            San9Pk101AvailabilityReport report)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }
            if (report == null)
            {
                throw new ArgumentNullException("report");
            }

            List<San9Pk101UnverifiedPreviewIssue> gateIssues = ValidateReportGate(report);
            if (gateIssues.Count != 0)
            {
                return Diagnostic(plan, gateIssues);
            }

            try
            {
                ValidatePlan(plan);
                CityAvailabilityObservation[] orderedCities = ValidateAndOrderCities(report);
                return Simulate(plan, orderedCities);
            }
            catch (PreviewStructureException exception)
            {
                return Diagnostic(plan, new[]
                {
                    new San9Pk101UnverifiedPreviewIssue(exception.Code, exception.Message)
                });
            }
            catch (OverflowException exception)
            {
                return Diagnostic(plan, new[]
                {
                    new San9Pk101UnverifiedPreviewIssue(
                        "PREVIEW_ARITHMETIC_OVERFLOW",
                        exception.Message)
                });
            }
        }

        private static List<San9Pk101UnverifiedPreviewIssue> ValidateReportGate(
            San9Pk101AvailabilityReport report)
        {
            List<San9Pk101UnverifiedPreviewIssue> issues =
                new List<San9Pk101UnverifiedPreviewIssue>();
            AddGateFailure(issues, report.DataReadSucceeded,
                "REPORT_DATA_READ_NOT_SUCCESSFUL", "The V2 data read did not complete successfully.");
            AddGateFailure(issues, !report.ObservationBlocked,
                "REPORT_OBSERVATION_BLOCKED", "The V2 observation contains a blocking condition.");
            AddGateFailure(issues, report.RawFieldsWereStable,
                "REPORT_RAW_FIELDS_UNSTABLE", "The raw observation was not stable.");
            AddGateFailure(issues, report.CodeAnchorsWereStableAndExact,
                "REPORT_CODE_ANCHORS_UNVERIFIED", "Code anchors were not stable and exact.");
            AddGateFailure(issues, report.StructureWasRevalidated,
                "REPORT_STRUCTURE_NOT_REVALIDATED", "The V1 structure was not revalidated across V2.");
            AddGateFailure(issues, report.ProcessIdentityRevalidatedAfterObservation,
                "REPORT_PROCESS_IDENTITY_NOT_REVALIDATED", "Process identity was not revalidated after observation.");
            AddGateFailure(issues, report.ContextToken != null,
                "REPORT_CONTEXT_TOKEN_MISSING", "The raw context token is missing.");
            if (report.ContextToken != null)
            {
                AddGateFailure(issues, report.ContextToken.IsStrategicInputPhaseCandidate,
                    "REPORT_PHASE_CANDIDATE_REJECTED", "The raw phase candidate is not the accepted value.");
                AddGateFailure(issues, !string.IsNullOrWhiteSpace(report.ContextToken.TokenSha256),
                    "REPORT_CONTEXT_TOKEN_EMPTY", "The raw context token digest is empty.");
            }
            AddGateFailure(issues, report.ReadCompletedUtc.HasValue,
                "REPORT_COMPLETION_TIME_MISSING", "The V2 read completion time is missing.");
            AddGateFailure(issues, report.Cities != null,
                "REPORT_CITIES_MISSING", "The V2 city observation array is missing.");

            San9Pk101DiagnosticReport baseline = report.Baseline;
            AddGateFailure(issues, baseline != null,
                "REPORT_BASELINE_MISSING", "The validated V0 baseline is missing.");
            if (baseline != null)
            {
                AddGateFailure(issues, baseline.FileValidation != null
                        && baseline.FileValidation.IsValid,
                    "REPORT_FILE_NOT_EXACT", "The exact executable file validation is not valid.");
                AddGateFailure(issues, baseline.ProcessDiscovery != null
                        && baseline.ProcessDiscovery.Status == ProcessDiscoveryStatus.Unique,
                    "REPORT_PROCESS_NOT_UNIQUE", "A unique validated game process was not observed.");
                AddGateFailure(issues, baseline.ReadOnlyConnection != null
                        && baseline.ReadOnlyConnection.Connected,
                    "REPORT_READ_CONNECTION_MISSING", "The read-only process connection was not validated.");
                AddGateFailure(issues, baseline.ConflictScan != null,
                    "REPORT_CONFLICT_SCAN_MISSING", "The conflict scan is missing.");
                if (baseline.ConflictScan != null)
                {
                    AddGateFailure(issues, baseline.ConflictScan.ProcessScanSucceeded,
                        "REPORT_PROCESS_SCAN_FAILED", "The conflict process scan failed.");
                    AddGateFailure(issues, baseline.ConflictScan.ModuleScanAttempted,
                        "REPORT_MODULE_SCAN_NOT_ATTEMPTED", "The conflict module scan was not attempted.");
                    AddGateFailure(issues, baseline.ConflictScan.ModuleScanSucceeded,
                        "REPORT_MODULE_SCAN_FAILED", "The conflict module scan failed.");
                    AddGateFailure(issues, !baseline.ConflictScan.HasBlockingConflicts,
                        "REPORT_KNOWN_CONFLICT", "A known blocking process or module conflict is present.");
                }
                AddGateFailure(issues, baseline.ExecutionAllowed,
                    "REPORT_BASELINE_GATE_CLOSED", "The validated baseline gate is closed.");
            }

            foreach (AvailabilityIssue issue in report.Issues ?? new AvailabilityIssue[0])
            {
                if (issue != null && issue.Severity == DiagnosticSeverity.Blocking)
                {
                    issues.Add(new San9Pk101UnverifiedPreviewIssue(
                        string.IsNullOrWhiteSpace(issue.Code) ? "REPORT_BLOCKING_ISSUE" : issue.Code,
                        issue.Message));
                }
            }

            return issues;
        }

        private static void AddGateFailure(
            ICollection<San9Pk101UnverifiedPreviewIssue> issues,
            bool passed,
            string code,
            string message)
        {
            if (!passed)
            {
                issues.Add(new San9Pk101UnverifiedPreviewIssue(code, message));
            }
        }

        private static void ValidatePlan(ExecutionPlan plan)
        {
            if (plan.CityScope != CityScope.DirectCities)
            {
                throw Structure("PREVIEW_PLAN_SCOPE_UNSUPPORTED", "Only direct cities are supported.");
            }
            if (plan.CityOrder != CityOrder.GameIdAscending)
            {
                throw Structure("PREVIEW_PLAN_ORDER_UNSUPPORTED", "Only ascending game city id order is supported.");
            }
            if (plan.Tasks == null)
            {
                throw Structure("PREVIEW_PLAN_TASKS_MISSING", "The frozen task collection is missing.");
            }

            foreach (FrozenTaskPlan task in plan.Tasks)
            {
                if (task == null)
                {
                    throw Structure("PREVIEW_PLAN_TASK_NULL", "The frozen task collection contains null.");
                }
                MapCommand(task.Command);
                if (task.SelectionPolicy != SelectionPolicy.NativeBest
                    && task.SelectionPolicy != SelectionPolicy.VerifiedStatFallback)
                {
                    throw Structure("PREVIEW_SELECTION_POLICY_UNKNOWN", "The selection policy is not supported.");
                }
                if (task.MinOfficers < 1
                    || task.MaxOfficers < task.MinOfficers
                    || task.MaxOfficers > GameRules.MaximumDomesticOfficers)
                {
                    throw Structure("PREVIEW_OFFICER_RANGE_INVALID", "The frozen officer range is invalid.");
                }
                if (task.RequireExactCount && task.MinOfficers != task.MaxOfficers)
                {
                    throw Structure("PREVIEW_EXACT_COUNT_INVALID", "An exact-count task has unequal bounds.");
                }
                if (task.ReserveMoney < 0 || task.ReserveMoney > GameRules.GameMoneyMax)
                {
                    throw Structure("PREVIEW_TASK_RESERVE_INVALID", "A task reserve is outside the game range.");
                }
            }
        }

        private static CityAvailabilityObservation[] ValidateAndOrderCities(
            San9Pk101AvailabilityReport report)
        {
            CityAvailabilityObservation[] cities = report.Cities
                .OrderBy(city => city == null ? int.MinValue : city.CityId)
                .ToArray();
            HashSet<int> cityIds = new HashSet<int>();
            Dictionary<int, int> corpsMoney = new Dictionary<int, int>();
            Dictionary<int, int> readyOfficerCity = new Dictionary<int, int>();

            foreach (CityAvailabilityObservation city in cities)
            {
                if (city == null)
                {
                    throw Structure("PREVIEW_CITY_NULL", "The city observation array contains null.");
                }
                if (city.CityId < 0 || !cityIds.Add(city.CityId))
                {
                    throw Structure("PREVIEW_CITY_ID_INVALID", "City ids must be non-negative and unique.");
                }
                if (!city.IsDirectlyControlled || !city.CorpsId.HasValue || city.CorpsId.Value < 0)
                {
                    throw Structure("PREVIEW_DIRECT_CITY_INVALID", "An observed city is not a valid direct-control city.");
                }
                if (city.CorpsMoney < 0 || city.CorpsMoney > GameRules.GameMoneyMax)
                {
                    throw Structure("PREVIEW_CORPS_MONEY_INVALID", "Observed corps money is outside the game range.");
                }

                int priorMoney;
                if (corpsMoney.TryGetValue(city.CorpsId.Value, out priorMoney))
                {
                    if (priorMoney != city.CorpsMoney)
                    {
                        throw Structure(
                            "PREVIEW_SHARED_CORPS_MONEY_MISMATCH",
                            "Cities in the same corps report different money balances.");
                    }
                }
                else
                {
                    corpsMoney.Add(city.CorpsId.Value, city.CorpsMoney);
                }

                ValidateCityCommands(city, report);
                CommandAvailabilityObservation canonical = city.Commands[0];
                foreach (OfficerRankingPreview candidate in canonical.ReadyCandidatesInSourceOrder)
                {
                    int priorCity;
                    if (readyOfficerCity.TryGetValue(candidate.PersonId, out priorCity)
                        && priorCity != city.CityId)
                    {
                        throw Structure(
                            "PREVIEW_OFFICER_IN_MULTIPLE_CITIES",
                            "A ready officer appears in more than one city observation.");
                    }
                    readyOfficerCity[candidate.PersonId] = city.CityId;
                }
            }

            return cities;
        }

        private static void ValidateCityCommands(
            CityAvailabilityObservation city,
            San9Pk101AvailabilityReport report)
        {
            if (city.Commands == null || city.Commands.Length != 5)
            {
                throw Structure("PREVIEW_COMMAND_SET_INCOMPLETE", "Each city must expose exactly five command observations.");
            }

            Dictionary<DomesticCommandKind, CommandAvailabilityObservation> commands =
                new Dictionary<DomesticCommandKind, CommandAvailabilityObservation>();
            foreach (CommandAvailabilityObservation command in city.Commands)
            {
                if (command == null || !IsKnownCommand(command.Command)
                    || commands.ContainsKey(command.Command))
                {
                    throw Structure("PREVIEW_COMMAND_SET_INVALID", "The city command set contains null, unknown, or duplicate entries.");
                }
                commands.Add(command.Command, command);
                ValidateCommand(city, command);
            }

            foreach (DomesticCommandKind required in new[]
            {
                DomesticCommandKind.Patrol,
                DomesticCommandKind.Commerce,
                DomesticCommandKind.Cultivate,
                DomesticCommandKind.Train,
                DomesticCommandKind.Repair
            })
            {
                if (!commands.ContainsKey(required))
                {
                    throw Structure("PREVIEW_COMMAND_MISSING", "A required command observation is missing.");
                }
            }

            OfficerRankingPreview[] canonical = city.Commands[0].ReadyCandidatesInSourceOrder;
            foreach (CommandAvailabilityObservation command in city.Commands.Skip(1))
            {
                if (command.ReadyCandidatesInSourceOrder.Length != canonical.Length)
                {
                    throw Structure("PREVIEW_READY_SET_MISMATCH", "Commands in one city expose different ready-officer counts.");
                }
                for (int index = 0; index < canonical.Length; index++)
                {
                    OfficerRankingPreview expected = canonical[index];
                    OfficerRankingPreview actual = command.ReadyCandidatesInSourceOrder[index];
                    if (actual.PersonId != expected.PersonId
                        || actual.SourceListIndex != expected.SourceListIndex
                        || !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal))
                    {
                        throw Structure("PREVIEW_READY_SET_MISMATCH", "Commands in one city expose different ready-officer identities.");
                    }
                }
            }
        }

        private static void ValidateCommand(
            CityAvailabilityObservation city,
            CommandAvailabilityObservation command)
        {
            OfficerRankingPreview[] source = command.ReadyCandidatesInSourceOrder;
            OfficerRankingPreview[] ranked = command.RankedCandidates;
            if (source == null || ranked == null
                || command.ReadyCandidateCount != source.Length
                || ranked.Length != source.Length)
            {
                throw Structure("PREVIEW_CANDIDATE_COUNT_MISMATCH", "Candidate counts and arrays are inconsistent.");
            }

            Dictionary<int, OfficerRankingPreview> sourceById =
                new Dictionary<int, OfficerRankingPreview>();
            int lastSourceIndex = -1;
            foreach (OfficerRankingPreview candidate in source)
            {
                if (candidate == null || candidate.PersonId < 0
                    || candidate.SourceListIndex < 0
                    || candidate.SourceListIndex <= lastSourceIndex
                    || sourceById.ContainsKey(candidate.PersonId))
                {
                    throw Structure("PREVIEW_SOURCE_CANDIDATES_INVALID", "The source candidate list is null, duplicated, or out of order.");
                }
                sourceById.Add(candidate.PersonId, candidate);
                lastSourceIndex = candidate.SourceListIndex;
            }

            HashSet<int> rankedIds = new HashSet<int>();
            OfficerRankingPreview prior = null;
            foreach (OfficerRankingPreview candidate in ranked)
            {
                OfficerRankingPreview sourceCandidate;
                if (candidate == null
                    || !rankedIds.Add(candidate.PersonId)
                    || !sourceById.TryGetValue(candidate.PersonId, out sourceCandidate)
                    || sourceCandidate.SourceListIndex != candidate.SourceListIndex
                    || sourceCandidate.Score != candidate.Score
                    || !string.Equals(sourceCandidate.Name, candidate.Name, StringComparison.Ordinal))
                {
                    throw Structure("PREVIEW_RANKED_CANDIDATES_INVALID", "The ranked candidate list does not match the source set.");
                }
                if (prior != null
                    && (candidate.Score > prior.Score
                        || (candidate.Score == prior.Score
                            && candidate.SourceListIndex < prior.SourceListIndex)))
                {
                    throw Structure("PREVIEW_RANK_ORDER_INVALID", "Candidate ranking is not stable descending score order.");
                }
                prior = candidate;
            }

            San9Pk101CommandDescriptor descriptor = Descriptor(command.Command);
            int productCostPerOfficer = descriptor.ProvisionalCostPerOfficer;
            if (command.ProvisionalCostPerOfficer != productCostPerOfficer)
            {
                throw Structure("PREVIEW_COST_POLICY_MISMATCH", "The per-officer provisional cost does not match the target-locked game rule.");
            }
            bool nativeMoney;
            switch (descriptor.NativeMoneyEvidence)
            {
                case NativeMoneyEvidenceKind.NotObserved:
                    if (command.NativeOneOfficerCostCandidate.HasValue)
                    {
                        throw Structure("PREVIEW_MONEY_EVIDENCE_INVALID", "A command with no observed native money gate exposes a cost candidate.");
                    }
                    nativeMoney = true;
                    break;
                case NativeMoneyEvidenceKind.ObservedPerOfficerThreshold:
                    if (!command.NativeOneOfficerCostCandidate.HasValue
                        || command.NativeOneOfficerCostCandidate.Value
                            != descriptor.ProvisionalCostPerOfficer)
                    {
                        throw Structure("PREVIEW_NATIVE_COST_MISMATCH", "The native one-officer cost candidate is inconsistent.");
                    }
                    nativeMoney = false;
                    break;
                default:
                    throw Structure("PREVIEW_MONEY_EVIDENCE_UNKNOWN", "The command descriptor has an unsupported money-evidence kind.");
            }

            IDictionary<string, AvailabilityCondition> conditions = ValidateConditions(command);
            bool direct = RequiredBoolean(conditions, DirectCondition);
            bool corps = RequiredBoolean(conditions, CorpsCondition);
            bool state = RequiredBoolean(conditions, StateCondition);
            bool common = RequiredBoolean(conditions, CityCommonCondition);
            bool candidateAvailable = RequiredBoolean(conditions, CandidateCondition);
            bool order = RequiredBoolean(conditions, OrderCondition);
            bool value = RequiredBoolean(conditions, ValueCondition);
            if (descriptor.NativeMoneyEvidence == NativeMoneyEvidenceKind.ObservedPerOfficerThreshold)
            {
                nativeMoney = RequiredBoolean(conditions, NativeMoneyCondition);
            }

            if (!direct || !corps)
            {
                throw Structure("PREVIEW_DIRECT_CORPS_CONTRADICTION", "A direct city failed its validated corps-control condition.");
            }
            if (candidateAvailable != (command.ReadyCandidateCount >= 1))
            {
                throw Structure("PREVIEW_CANDIDATE_CONDITION_MISMATCH", "The candidate condition contradicts the candidate count.");
            }
            if (descriptor.NativeMoneyEvidence == NativeMoneyEvidenceKind.ObservedPerOfficerThreshold
                && nativeMoney != (city.CorpsMoney >= descriptor.ProvisionalCostPerOfficer))
            {
                throw Structure("PREVIEW_NATIVE_MONEY_CONDITION_MISMATCH", "The native money condition contradicts corps money.");
            }

            bool expectedStatic = direct && corps && state && common && candidateAvailable
                && nativeMoney && order && value;
            if (command.KnownStaticSubsetWouldPass != expectedStatic)
            {
                throw Structure("PREVIEW_STATIC_GATE_MISMATCH", "KnownStaticSubsetWouldPass contradicts its structured conditions.");
            }

            if (command.NativeEntryResultVerified)
            {
                throw Structure("PREVIEW_NATIVE_RESULT_UNEXPECTED", "An unverified V2 observation unexpectedly claims a native result.");
            }
        }

        private static IDictionary<string, AvailabilityCondition> ValidateConditions(
            CommandAvailabilityObservation command)
        {
            if (command.Conditions == null)
            {
                throw Structure("PREVIEW_CONDITIONS_MISSING", "The command condition array is missing.");
            }

            San9Pk101CommandDescriptor descriptor = Descriptor(command.Command);
            HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal)
            {
                DirectCondition,
                CorpsCondition,
                StateCondition,
                CityCommonCondition,
                CandidateCondition,
                OrderCondition,
                ValueCondition,
                MoneyConditionCode(descriptor)
            };
            Dictionary<string, AvailabilityCondition> values =
                new Dictionary<string, AvailabilityCondition>(StringComparer.Ordinal);
            foreach (AvailabilityCondition condition in command.Conditions)
            {
                if (condition == null
                    || string.IsNullOrWhiteSpace(condition.Code)
                    || !expected.Contains(condition.Code)
                    || values.ContainsKey(condition.Code))
                {
                    throw Structure("PREVIEW_CONDITION_SET_INVALID", "Conditions contain null, unknown, or duplicate entries.");
                }
                AvailabilityEvidence expectedEvidence = ExpectedEvidence(
                    command.Command,
                    condition.Code);
                if (condition.Evidence != expectedEvidence)
                {
                    throw Structure(
                        "PREVIEW_CONDITION_EVIDENCE_MISMATCH",
                        "A condition carries unexpected evidence classification: "
                            + condition.Code + ".");
                }
                values.Add(condition.Code, condition);
            }
            if (values.Count != expected.Count)
            {
                throw Structure("PREVIEW_CONDITION_SET_INCOMPLETE", "The command condition set is incomplete.");
            }

            foreach (string code in expected)
            {
                AvailabilityCondition condition = values[code];
                if (code == UnobservedNativeMoneyCondition)
                {
                    if (condition.Passed.HasValue)
                    {
                        throw Structure("PREVIEW_UNOBSERVED_MONEY_CONDITION_INVALID", "An unobserved native money gate must remain unknown.");
                    }
                }
                else if (!condition.Passed.HasValue)
                {
                    throw Structure("PREVIEW_CONDITION_UNKNOWN", "A required condition is technically unknown: " + code + ".");
                }
            }
            return values;
        }

        private static AvailabilityEvidence ExpectedEvidence(
            DomesticCommandKind command,
            string code)
        {
            if (code == DirectCondition
                || code == CorpsCondition
                || code == StateCondition
                || code == CityCommonCondition
                || code == OrderCondition
                || code == ValueCondition)
            {
                return AvailabilityEvidence.ConfirmedRawEquivalent;
            }
            if (code == CandidateCondition)
            {
                return AvailabilityEvidence.ConfirmedStaticRule;
            }
            San9Pk101CommandDescriptor descriptor = Descriptor(command);
            if (code == NativeMoneyCondition
                && descriptor.NativeMoneyEvidence == NativeMoneyEvidenceKind.ObservedPerOfficerThreshold)
            {
                return AvailabilityEvidence.DerivedRawCandidate;
            }
            if (code == UnobservedNativeMoneyCondition
                && descriptor.NativeMoneyEvidence == NativeMoneyEvidenceKind.NotObserved)
            {
                return AvailabilityEvidence.Unknown;
            }

            throw Structure(
                "PREVIEW_CONDITION_EVIDENCE_UNKNOWN",
                "No evidence classification is defined for condition: " + code + ".");
        }

        private static bool RequiredBoolean(
            IDictionary<string, AvailabilityCondition> conditions,
            string code)
        {
            AvailabilityCondition condition;
            if (!conditions.TryGetValue(code, out condition) || !condition.Passed.HasValue)
            {
                throw Structure("PREVIEW_CONDITION_UNKNOWN", "A required condition is missing or unknown: " + code + ".");
            }
            return condition.Passed.Value;
        }

        private static San9Pk101UnverifiedPreviewResult Simulate(
            ExecutionPlan plan,
            IEnumerable<CityAvailabilityObservation> cities)
        {
            Dictionary<int, int> remainingMoney = new Dictionary<int, int>();
            foreach (CityAvailabilityObservation city in cities)
            {
                if (!remainingMoney.ContainsKey(city.CorpsId.Value))
                {
                    remainingMoney.Add(city.CorpsId.Value, city.CorpsMoney);
                }
            }

            HashSet<int> consumedOfficerIds = new HashSet<int>();
            List<San9Pk101UnverifiedCityPreview> cityResults =
                new List<San9Pk101UnverifiedCityPreview>();
            foreach (CityAvailabilityObservation city in cities)
            {
                Dictionary<DomesticCommandKind, CommandAvailabilityObservation> commands =
                    city.Commands.ToDictionary(command => command.Command);
                HashSet<DomesticCommand> plannedCommands = new HashSet<DomesticCommand>();
                List<San9Pk101UnverifiedTaskPreview> taskResults =
                    new List<San9Pk101UnverifiedTaskPreview>();
                foreach (FrozenTaskPlan task in plan.Tasks)
                {
                    DomesticCommandKind observedCommand = MapCommand(task.Command);
                    CommandAvailabilityObservation observation = commands[observedCommand];
                    UnverifiedPreviewRankingSource appliedRankingSource;
                    List<OfficerRankingPreview> resolvedRanking = ResolvePreviewRanking(
                        task,
                        observation,
                        out appliedRankingSource);
                    List<OfficerRankingPreview> eligible = resolvedRanking
                        .Where(candidate => !consumedOfficerIds.Contains(candidate.PersonId))
                        .ToList();

                    if (plannedCommands.Contains(task.Command))
                    {
                        taskResults.Add(Skipped(
                            task.Command,
                            observedCommand,
                            task.SelectionPolicy,
                            appliedRankingSource,
                            UnverifiedPreviewSkipReason.DuplicateCommand,
                            "The same command already appeared for this city in this frozen plan.",
                            eligible.Count,
                            0,
                            remainingMoney[city.CorpsId.Value],
                            new string[0]));
                        continue;
                    }

                    IDictionary<string, AvailabilityCondition> conditions =
                        observation.Conditions.ToDictionary(condition => condition.Code, StringComparer.Ordinal);
                    string[] failedBusinessConditions = new[]
                    {
                        StateCondition,
                        CityCommonCondition,
                        OrderCondition,
                        ValueCondition
                    }
                        .Where(code => !RequiredBoolean(conditions, code))
                        .ToArray();
                    if (failedBusinessConditions.Length != 0)
                    {
                        taskResults.Add(Skipped(
                            task.Command,
                            observedCommand,
                            task.SelectionPolicy,
                            appliedRankingSource,
                            UnverifiedPreviewSkipReason.NativeRuleBlocked,
                            "Confirmed static command conditions are closed.",
                            eligible.Count,
                            0,
                            remainingMoney[city.CorpsId.Value],
                            failedBusinessConditions));
                        continue;
                    }

                    int selectedCount;
                    if (task.RequireExactCount)
                    {
                        selectedCount = task.MaxOfficers;
                        if (eligible.Count < selectedCount)
                        {
                            taskResults.Add(Skipped(
                                task.Command,
                                observedCommand,
                                task.SelectionPolicy,
                                appliedRankingSource,
                                UnverifiedPreviewSkipReason.InsufficientOfficers,
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Requires exactly {0} officers; {1} remain eligible.",
                                    selectedCount,
                                    eligible.Count),
                                eligible.Count,
                                0,
                                remainingMoney[city.CorpsId.Value],
                                new string[0]));
                            continue;
                        }
                    }
                    else
                    {
                        if (eligible.Count < task.MinOfficers)
                        {
                            taskResults.Add(Skipped(
                                task.Command,
                                observedCommand,
                                task.SelectionPolicy,
                                appliedRankingSource,
                                UnverifiedPreviewSkipReason.InsufficientOfficers,
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Requires at least {0} officers; {1} remain eligible.",
                                    task.MinOfficers,
                                    eligible.Count),
                                eligible.Count,
                                0,
                                remainingMoney[city.CorpsId.Value],
                                new string[0]));
                            continue;
                        }
                        selectedCount = Math.Min(task.MaxOfficers, eligible.Count);
                    }

                    int estimatedCost = checked(
                        selectedCount * observation.ProvisionalCostPerOfficer);
                    int moneyBefore = remainingMoney[city.CorpsId.Value];
                    if (moneyBefore < estimatedCost)
                    {
                        taskResults.Add(Skipped(
                            task.Command,
                            observedCommand,
                            task.SelectionPolicy,
                            appliedRankingSource,
                            UnverifiedPreviewSkipReason.InsufficientMoney,
                            "The shared corps balance is below the provisional product cost.",
                            eligible.Count,
                            estimatedCost,
                            moneyBefore,
                            new string[0]));
                        continue;
                    }

                    int moneyAfter = checked(moneyBefore - estimatedCost);
                    if (moneyAfter < task.ReserveMoney)
                    {
                        taskResults.Add(Skipped(
                            task.Command,
                            observedCommand,
                            task.SelectionPolicy,
                            appliedRankingSource,
                            UnverifiedPreviewSkipReason.ReserveMoneyProtected,
                            "The projected shared corps balance would fall below the frozen task reserve.",
                            eligible.Count,
                            estimatedCost,
                            moneyBefore,
                            new string[0]));
                        continue;
                    }

                    List<OfficerRankingPreview> selected = eligible.Take(selectedCount).ToList();
                    foreach (OfficerRankingPreview officer in selected)
                    {
                        if (!consumedOfficerIds.Add(officer.PersonId))
                        {
                            throw Structure("PREVIEW_OFFICER_REUSE", "An officer would be selected more than once in one profile preview.");
                        }
                    }
                    plannedCommands.Add(task.Command);
                    remainingMoney[city.CorpsId.Value] = moneyAfter;
                    taskResults.Add(new San9Pk101UnverifiedTaskPreview(
                        task.Command,
                        observedCommand,
                        task.SelectionPolicy,
                        appliedRankingSource,
                        UnverifiedPreviewDecision.ProvisionallySelected,
                        UnverifiedPreviewSkipReason.None,
                        task.SelectionPolicy == SelectionPolicy.NativeBest
                            ? "native_best was requested, but no native result is verified; the static attribute proxy was applied."
                            : "The requested verified_stat_fallback was applied as a stable static attribute ranking.",
                        eligible.Count,
                        selected.Select(item => new San9Pk101UnverifiedOfficerPreview(item)),
                        estimatedCost,
                        moneyBefore,
                        moneyAfter,
                        new string[0]));
                }

                cityResults.Add(new San9Pk101UnverifiedCityPreview(
                    city.CityId,
                    city.CityName,
                    city.CorpsId.Value,
                    taskResults));
            }

            IEnumerable<San9Pk101UnverifiedPreviewIssue> provenanceIssues = plan.Tasks.Any(
                task => task.SelectionPolicy == SelectionPolicy.NativeBest)
                ? new[]
                {
                    new San9Pk101UnverifiedPreviewIssue(
                        "PREVIEW_NATIVE_BEST_STATIC_PROXY",
                        "native_best was requested, but NativeEntryResultVerified=false; all displayed officers use a static attribute proxy.")
                }
                : new San9Pk101UnverifiedPreviewIssue[0];
            return new San9Pk101UnverifiedPreviewResult(
                plan,
                false,
                cityResults,
                remainingMoney.OrderBy(item => item.Key).Select(item =>
                    new San9Pk101UnverifiedCorpsMoneyPreview(item.Key, item.Value)),
                provenanceIssues);
        }

        private static San9Pk101UnverifiedTaskPreview Skipped(
            DomesticCommand command,
            DomesticCommandKind observedCommand,
            SelectionPolicy requestedSelectionPolicy,
            UnverifiedPreviewRankingSource appliedRankingSource,
            UnverifiedPreviewSkipReason reason,
            string detail,
            int eligibleCount,
            int estimatedCost,
            int money,
            IEnumerable<string> failedConditions)
        {
            return new San9Pk101UnverifiedTaskPreview(
                command,
                observedCommand,
                requestedSelectionPolicy,
                appliedRankingSource,
                UnverifiedPreviewDecision.Skipped,
                reason,
                detail,
                eligibleCount,
                new San9Pk101UnverifiedOfficerPreview[0],
                estimatedCost,
                money,
                money,
                failedConditions);
        }

        private static List<OfficerRankingPreview> ResolvePreviewRanking(
            FrozenTaskPlan task,
            CommandAvailabilityObservation observation,
            out UnverifiedPreviewRankingSource appliedRankingSource)
        {
            appliedRankingSource = UnverifiedPreviewRankingSource.StaticStatProxy;
            switch (task.SelectionPolicy)
            {
                case SelectionPolicy.NativeBest:
                    // V2 has no verified native entry result. RankedCandidates is a
                    // target-locked static proxy and the DTO exposes that provenance.
                    return observation.RankedCandidates.ToList();
                case SelectionPolicy.VerifiedStatFallback:
                    return observation.ReadyCandidatesInSourceOrder
                        .OrderByDescending(candidate => candidate.Score)
                        .ThenBy(candidate => candidate.SourceListIndex)
                        .ToList();
                default:
                    throw Structure(
                        "PREVIEW_SELECTION_POLICY_UNKNOWN",
                        "A frozen task has no explicit unverified ranking policy mapping.");
            }
        }

        private static San9Pk101UnverifiedPreviewResult Diagnostic(
            ExecutionPlan plan,
            IEnumerable<San9Pk101UnverifiedPreviewIssue> issues)
        {
            return new San9Pk101UnverifiedPreviewResult(
                plan,
                true,
                new San9Pk101UnverifiedCityPreview[0],
                new San9Pk101UnverifiedCorpsMoneyPreview[0],
                issues);
        }

        private static DomesticCommandKind MapCommand(DomesticCommand command)
        {
            switch (command)
            {
                case DomesticCommand.Patrol:
                    return DomesticCommandKind.Patrol;
                case DomesticCommand.Commerce:
                    return DomesticCommandKind.Commerce;
                case DomesticCommand.Cultivate:
                    return DomesticCommandKind.Cultivate;
                case DomesticCommand.Train:
                    return DomesticCommandKind.Train;
                case DomesticCommand.Repair:
                    return DomesticCommandKind.Repair;
                default:
                    throw Structure("PREVIEW_COMMAND_MAPPING_UNKNOWN", "A frozen command has no explicit V2 mapping.");
            }
        }

        private static bool IsKnownCommand(DomesticCommandKind command)
        {
            return command == DomesticCommandKind.Patrol
                || command == DomesticCommandKind.Commerce
                || command == DomesticCommandKind.Cultivate
                || command == DomesticCommandKind.Train
                || command == DomesticCommandKind.Repair;
        }

        private static string MoneyConditionCode(San9Pk101CommandDescriptor descriptor)
        {
            switch (descriptor.NativeMoneyEvidence)
            {
                case NativeMoneyEvidenceKind.ObservedPerOfficerThreshold:
                    return NativeMoneyCondition;
                case NativeMoneyEvidenceKind.NotObserved:
                    return UnobservedNativeMoneyCondition;
                default:
                    throw Structure(
                        "PREVIEW_MONEY_EVIDENCE_UNKNOWN",
                        "A command descriptor has no supported money-evidence mapping.");
            }
        }

        private static San9Pk101CommandDescriptor Descriptor(DomesticCommandKind command)
        {
            try
            {
                return San9Pk101CommandDescriptor.Get(command);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw Structure("PREVIEW_COMMAND_DESCRIPTOR_UNKNOWN", exception.Message);
            }
        }

        private static PreviewStructureException Structure(string code, string message)
        {
            return new PreviewStructureException(code, message);
        }

        private sealed class PreviewStructureException : Exception
        {
            internal PreviewStructureException(string code, string message)
                : base(message)
            {
                Code = code;
            }

            internal string Code { get; private set; }
        }
    }
}
