using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Core.Configuration;

namespace San9AutoDomestic.V2AvailabilitySelfTest
{
    internal static class Program
    {
        private static int passed;
        private static readonly List<string> Failures = new List<string>();

        private static int Main()
        {
            Run("five commands expose ranked candidates and per-officer fees", VerifyFiveCommands);
            Run("equal scores preserve city-list order", VerifyTieOrder);
            Run("gray order bit is reported", VerifyGrayItem);
            Run("observer preserves four ready officers without product sizing", VerifyFourPeople);
            Run("configured exact-five cost blocks paid commands but not training", VerifyMoney249);
            Run("non-one phase fails closed", VerifyPhase);
            Run("raw A/B/A change is rejected", VerifyRawChange);
            Run("live code change is rejected", VerifyCodeChange);
            Run("invalid baseline is rejected", VerifyInvalidBaseline);
            Run("unknown command descriptor is rejected", VerifyUnknownCommandDescriptor);
            Run("projector consumes officers in task order", VerifyProjectorSequential);
            Run("projector skips gray task without consumption", VerifyProjectorGrayThenContinue);
            Run("projector skips after only four officers remain", VerifyProjectorInsufficientAfterConsume);
            Run("projector shares corps money across cities", VerifyProjectorSharedCorpsMoney);
            Run("projector applies per-task reserve without consumption", VerifyProjectorReserveThenContinue);
            Run("projector emits diagnostic-only result for conflict", VerifyProjectorConflictDiagnosticOnly);
            Run("projector maps five commands and training is free", VerifyProjectorMappingAndTrainingCost);
            Run("projector exposes native-best static-proxy provenance", VerifyProjectorNativeBestProvenance);
            Run("projector exposes verified fallback provenance", VerifyProjectorFallbackProvenance);
            Run("projector trains five officers with zero money", VerifyProjectorTrainingWithZeroMoney);
            Run("projector supports general officer ranges", VerifyProjectorGeneralOfficerRange);
            Run("projector prevents duplicate commands", VerifyProjectorDuplicateCommand);
            Run("projector discards partial output on contradiction", VerifyProjectorStructuralAbort);
            Run("projector rejects contradictory condition evidence", VerifyProjectorEvidenceAbort);
            Run("projector rejects ranked/source score contradiction", VerifyProjectorScoreContradiction);
            Run("UI observer reports exact selector order and selection", VerifyUiSelectorObservation);
            Run("UI observer rejects contradictory current city", VerifyUiCityContradiction);
            Run("exact Easy profile is allowlisted and path drift is rejected", VerifyEasyCompatibilityProfile);
            Run("frozen Easy descriptor matches independent manifest oracle", VerifyEasyManifestOracle);
            Run("Easy verifier rejects every redirect mutation and invalid auxiliary state", VerifyEasyRuntimePointMatrix);
            Run("Easy verifier handles ASLR modulo rel32 and rejects image overflow", VerifyEasyRuntimeBases);
            Run("Easy verifier rejects unstable A/B and identity drift", VerifyEasyRuntimeStabilityAndIdentity);
            Run("Easy hook epoch is process-wide, bounded, and restart-latched only after arm", VerifyEasyHookEpoch);
            Console.WriteLine("V2 synthetic summary: {0} passed, {1} failed.", passed, Failures.Count);
            foreach (string failure in Failures) Console.Error.WriteLine("FAIL: " + failure);
            return Failures.Count == 0 ? 0 : 1;
        }

        private static void VerifyFiveCommands()
        {
            FakeMemory fake = FakeMemory.Create(6, 1000);
            San9Pk101AvailabilityReport report = Read(fake);
            Assert(!report.ObservationBlocked, Join(report));
            Assert(report.Cities.Length == 1, "Expected one direct city.");
            foreach (CommandAvailabilityObservation command in report.Cities[0].Commands)
            {
                Assert(command.ReadyCandidatesInSourceOrder.Length == 6,
                    command.Command + " lost source candidates.");
                Assert(command.RankedCandidates.Length == 6,
                    command.Command + " lost ranked candidates.");
                int expectedCost = command.Command == DomesticCommandKind.Train ? 0 : 50;
                Assert(command.ProvisionalCostPerOfficer == expectedCost,
                    "Unexpected per-officer provisional cost.");
            }
            Assert(!report.PlanningReady && !report.VerifiedNativeCapability,
                "V2 observer exposed a verified/planning capability.");
        }

        private static void VerifyTieOrder()
        {
            FakeMemory fake = FakeMemory.Create(6, 1000);
            fake.SetPersonAbility(0, San9Pk101MemoryLayout.PersonEffectiveIntelligenceOffset, 99);
            fake.SetPersonAbility(1, San9Pk101MemoryLayout.PersonEffectiveIntelligenceOffset, 99);
            CommandAvailabilityObservation patrol = Read(fake).Cities[0].Commands
                .Single(command => command.Command == DomesticCommandKind.Patrol);
            Assert(patrol.RankedCandidates[0].PersonId == 0 && patrol.RankedCandidates[1].PersonId == 1,
                "Tie was not stable in city+0xDC source order.");
        }

        private static void VerifyGrayItem()
        {
            FakeMemory fake = FakeMemory.Create(6, 1000);
            fake.SetUInt32(San9Pk101MemoryLayout.CityBase + San9Pk101MemoryLayout.CityOrderFlags1e0Offset, 1u << 4);
            CommandAvailabilityObservation commerce = Read(fake).Cities[0].Commands
                .Single(command => command.Command == DomesticCommandKind.Commerce);
            Assert(!commerce.KnownStaticSubsetWouldPass,
                "Occupied commerce command passed the static native-entry subset.");
            Assert(commerce.Conditions.Any(condition => condition.Code == "COMMAND_ORDER_BIT_CLEAR" && condition.Passed == false),
                "Gray reason was not structured.");
        }

        private static void VerifyFourPeople()
        {
            CommandAvailabilityObservation command = Read(FakeMemory.Create(4, 1000)).Cities[0].Commands[0];
            Assert(command.KnownStaticSubsetWouldPass, "Four people should pass the one-person static entry subset.");
            Assert(command.ReadyCandidateCount == 4 && command.RankedCandidates.Length == 4,
                "The observer applied a fixed product size to the ready set.");
        }

        private static void VerifyMoney249()
        {
            San9Pk101AvailabilityReport report = Read(FakeMemory.Create(6, 249));
            DomesticCommand[] paid =
            {
                DomesticCommand.Patrol,
                DomesticCommand.Commerce,
                DomesticCommand.Cultivate,
                DomesticCommand.Repair
            };
            foreach (DomesticCommand command in paid)
            {
                San9Pk101UnverifiedTaskPreview task = Project(
                    CompilePlan(ExactTask(command, null)),
                    report).Cities.Single().Tasks.Single();
                Assert(task.SkipReason == UnverifiedPreviewSkipReason.InsufficientMoney
                        && task.EstimatedCost == 250,
                    command + " did not apply its configured exact-five cost.");
            }

            San9Pk101UnverifiedTaskPreview training = Project(
                CompilePlan(ExactTask(DomesticCommand.Train, null)),
                report).Cities.Single().Tasks.Single();
            Assert(training.Decision == UnverifiedPreviewDecision.ProvisionallySelected
                    && training.EstimatedCost == 0,
                "Training was not provisionally zero-cost.");
        }

        private static void VerifyPhase()
        {
            FakeMemory fake = FakeMemory.Create(6, 1000);
            fake.SetUInt32(San9Pk101MemoryLayout.RawPhaseCandidateAddress, 2);
            San9Pk101AvailabilityReport report = Read(fake);
            Assert(report.ObservationBlocked && report.Issues.Any(issue => issue.Code == "V2_PHASE_NOT_STRATEGIC_INPUT_CANDIDATE"),
                "Non-one phase did not fail closed.");
        }

        private static void VerifyRawChange()
        {
            FakeMemory fake = FakeMemory.Create(6, 1000);
            fake.AlternateRawContext = true;
            San9Pk101AvailabilityReport report = Read(fake);
            Assert(report.ObservationBlocked && report.Issues.Any(issue => issue.Code == "V2_RAW_CONTEXT_UNSTABLE"),
                "Alternating raw context passed A/B/A.");
        }

        private static void VerifyCodeChange()
        {
            FakeMemory fake = FakeMemory.Create(6, 1000);
            fake.AlternateFirstCodeAnchor = true;
            San9Pk101AvailabilityReport report = Read(fake);
            Assert(report.ObservationBlocked && report.Issues.Any(issue => issue.Code == "V2_CODE_ANCHOR_MISMATCH"),
                "Alternating code bytes passed the anchor gate.");
        }

        private static void VerifyInvalidBaseline()
        {
            FakeMemory fake = FakeMemory.Create(6, 1000);
            San9Pk101AvailabilityReport report = new San9Pk101AvailabilityReader().Read(
                fake, new San9Pk101DiagnosticReport(), San9Pk101Target.ExpectedExecutablePath);
            Assert(report.ObservationBlocked && report.Issues.Any(issue => issue.Code == "V2_BASELINE_NOT_VALIDATED"),
                "Invalid baseline was not blocked.");
        }

        private static void VerifyUnknownCommandDescriptor()
        {
            bool rejected = false;
            try
            {
                San9Pk101CommandDescriptor.Get((DomesticCommandKind)99);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            Assert(rejected, "An unknown command silently inherited a target descriptor.");
        }

        private static void VerifyProjectorSequential()
        {
            San9Pk101AvailabilityReport report = Read(FakeMemory.Create(10, 500));
            ExecutionPlan plan = CompilePlan(
                ExactTask(DomesticCommand.Commerce, null),
                ExactTask(DomesticCommand.Cultivate, null));
            San9Pk101UnverifiedPreviewResult result = Project(plan, report);

            Assert(!result.DiagnosticOnly && result.Cities.Count == 1, "Clean preview was diagnostic-only.");
            Assert(result.ProvisionallySelectedTaskCount == 2 && result.SelectedOfficerCount == 10,
                "Two exact-five tasks were not selected.");
            AssertSequence(result.Cities[0].Tasks[0].SelectedOfficers.Select(item => item.PersonId),
                new[] { 9, 8, 7, 6, 5 }, "Commerce ranking/selection changed.");
            AssertSequence(result.Cities[0].Tasks[1].SelectedOfficers.Select(item => item.PersonId),
                new[] { 4, 3, 2, 1, 0 }, "Cultivate reused prior officers.");
            Assert(result.RemainingMoneyByCorps.Single().RemainingMoney == 0,
                "Sequential paid commands did not consume 500 money.");
            AssertNonActionable(result);
        }

        private static void VerifyProjectorGrayThenContinue()
        {
            FakeMemory fake = FakeMemory.Create(5, 250);
            fake.SetUInt32(
                San9Pk101MemoryLayout.CityBase + San9Pk101MemoryLayout.CityOrderFlags1e0Offset,
                1u << 4);
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(
                    ExactTask(DomesticCommand.Commerce, null),
                    ExactTask(DomesticCommand.Cultivate, null)),
                Read(fake));

            Assert(result.Cities[0].Tasks[0].SkipReason == UnverifiedPreviewSkipReason.NativeRuleBlocked,
                "Gray commerce was not skipped.");
            Assert(result.Cities[0].Tasks[0].SelectedOfficers.Count == 0,
                "Gray commerce selected officers.");
            Assert(result.Cities[0].Tasks[1].Decision == UnverifiedPreviewDecision.ProvisionallySelected
                    && result.Cities[0].Tasks[1].SelectedOfficers.Count == 5,
                "Cultivate did not receive the untouched five officers.");
            Assert(result.RemainingMoneyByCorps.Single().RemainingMoney == 0,
                "Only the succeeding paid task should consume money.");
        }

        private static void VerifyProjectorInsufficientAfterConsume()
        {
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(
                    ExactTask(DomesticCommand.Commerce, null),
                    ExactTask(DomesticCommand.Cultivate, null)),
                Read(FakeMemory.Create(9, 500)));
            Assert(result.Cities[0].Tasks[0].SelectedOfficers.Count == 5,
                "The first task did not select five officers.");
            Assert(result.Cities[0].Tasks[1].SkipReason == UnverifiedPreviewSkipReason.InsufficientOfficers
                    && result.Cities[0].Tasks[1].EligibleCandidateCount == 4
                    && result.Cities[0].Tasks[1].SelectedOfficers.Count == 0,
                "The second task did not skip with four remaining officers.");
            Assert(result.RemainingMoneyByCorps.Single().RemainingMoney == 250,
                "An officer-count skip consumed money.");
        }

        private static void VerifyProjectorSharedCorpsMoney()
        {
            San9Pk101AvailabilityReport report = Read(FakeMemory.Create(5, 500));
            report.Cities = new[]
            {
                report.Cities[0],
                CloneCity(report.Cities[0], 1, 5)
            };
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(ExactTask(DomesticCommand.Commerce, null)),
                report);

            Assert(result.Cities.Count == 2
                    && result.Cities.All(city =>
                        city.Tasks[0].Decision == UnverifiedPreviewDecision.ProvisionallySelected),
                "Both shared-corps cities were not independently selected.");
            Assert(result.Cities[0].Tasks[0].MoneyBefore == 500
                    && result.Cities[0].Tasks[0].MoneyAfter == 250
                    && result.Cities[1].Tasks[0].MoneyBefore == 250
                    && result.Cities[1].Tasks[0].MoneyAfter == 0,
                "The second city did not observe the first city's provisional deduction.");
            Assert(result.RemainingMoneyByCorps.Count == 1
                    && result.RemainingMoneyByCorps[0].RemainingMoney == 0,
                "Shared corps money was duplicated instead of shared.");
        }

        private static void VerifyProjectorReserveThenContinue()
        {
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(
                    ExactTask(DomesticCommand.Commerce, 300),
                    ExactTask(DomesticCommand.Cultivate, 0)),
                Read(FakeMemory.Create(5, 500)));
            Assert(result.Cities[0].Tasks[0].SkipReason == UnverifiedPreviewSkipReason.ReserveMoneyProtected
                    && result.Cities[0].Tasks[0].MoneyBefore == 500
                    && result.Cities[0].Tasks[0].MoneyAfter == 500,
                "Reserve skip changed the balance.");
            Assert(result.Cities[0].Tasks[1].SelectedOfficers.Count == 5
                    && result.Cities[0].Tasks[1].MoneyBefore == 500
                    && result.Cities[0].Tasks[1].MoneyAfter == 250,
                "The later task did not receive untouched money/officers.");
        }

        private static void VerifyProjectorConflictDiagnosticOnly()
        {
            San9Pk101AvailabilityReport report = Read(FakeMemory.Create(10, 1000));
            report.ObservationBlocked = true;
            report.Baseline.ExecutionAllowed = false;
            report.Baseline.ConflictScan.HasBlockingConflicts = true;
            report.Issues = report.Issues.Concat(new[]
            {
                new AvailabilityIssue(
                    "V2_KNOWN_MODIFIER_CONFLICT",
                    DiagnosticSeverity.Blocking,
                    "Synthetic Easy conflict.")
            }).ToArray();
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(ExactTask(DomesticCommand.Commerce, null)),
                report);
            Assert(result.DiagnosticOnly
                    && result.Cities.Count == 0
                    && result.RemainingMoneyByCorps.Count == 0
                    && result.SelectedOfficerCount == 0,
                "A blocking conflict leaked a provisional selection.");
            Assert(result.Issues.Any(issue => issue.Code == "V2_KNOWN_MODIFIER_CONFLICT"),
                "The source blocking issue was not preserved.");
            AssertNonActionable(result);
        }

        private static void VerifyProjectorMappingAndTrainingCost()
        {
            ExecutionPlan plan = CompilePlan(
                ExactTask(DomesticCommand.Patrol, null),
                ExactTask(DomesticCommand.Commerce, null),
                ExactTask(DomesticCommand.Cultivate, null),
                ExactTask(DomesticCommand.Train, null),
                ExactTask(DomesticCommand.Repair, null));
            San9Pk101UnverifiedPreviewResult result = Project(
                plan,
                Read(FakeMemory.Create(25, 1000)));
            San9Pk101UnverifiedTaskPreview[] tasks = result.Cities[0].Tasks.ToArray();
            Assert(!result.DiagnosticOnly && tasks.Length == 5, "Five-command preview failed.");
            Assert(tasks[0].ObservedCommand == DomesticCommandKind.Patrol
                    && tasks[1].ObservedCommand == DomesticCommandKind.Commerce
                    && tasks[2].ObservedCommand == DomesticCommandKind.Cultivate
                    && tasks[3].ObservedCommand == DomesticCommandKind.Train
                    && tasks[4].ObservedCommand == DomesticCommandKind.Repair,
                "Core and adapter commands were not explicitly mapped.");
            Assert(tasks[3].EstimatedCost == 0
                    && tasks[3].MoneyBefore == tasks[3].MoneyAfter,
                "Training was not projected as zero-cost.");
            Assert(tasks.Where(task => task.Command != DomesticCommand.Train)
                    .All(task => task.EstimatedCost == 250),
                "A paid command did not cost 50 per selected officer.");
            Assert(result.SelectedOfficerCount == 25
                    && tasks.SelectMany(task => task.SelectedOfficers)
                        .Select(officer => officer.PersonId).Distinct().Count() == 25,
                "Five commands reused officers.");
            Assert(result.RemainingMoneyByCorps.Single().RemainingMoney == 0,
                "Four paid commands plus free training should consume exactly 1000.");
        }

        private static void VerifyProjectorTrainingWithZeroMoney()
        {
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(ExactTask(DomesticCommand.Train, null)),
                Read(FakeMemory.Create(5, 0)));
            San9Pk101UnverifiedTaskPreview task = result.Cities[0].Tasks[0];
            Assert(!result.DiagnosticOnly
                    && task.Decision == UnverifiedPreviewDecision.ProvisionallySelected
                    && task.SelectedOfficers.Count == 5
                    && task.EstimatedCost == 0
                    && task.MoneyBefore == 0
                    && task.MoneyAfter == 0
                    && result.RemainingMoneyByCorps.Single().RemainingMoney == 0,
                "Zero-money training was not projected as a free five-officer task.");
        }

        private static void VerifyProjectorGeneralOfficerRange()
        {
            TaskConfiguration flexible = new TaskConfiguration(
                DomesticCommand.Commerce,
                true,
                3,
                4,
                false,
                SelectionPolicy.NativeBest,
                null);
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(flexible),
                Read(FakeMemory.Create(4, 200)));
            Assert(result.Cities[0].Tasks[0].SelectedOfficers.Count == 4
                    && result.Cities[0].Tasks[0].EstimatedCost == 200
                    && result.RemainingMoneyByCorps.Single().RemainingMoney == 0,
                "A general 3..4 officer task was not projected with four officers.");
        }

        private static void VerifyProjectorDuplicateCommand()
        {
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(
                    ExactTask(DomesticCommand.Commerce, null),
                    ExactTask(DomesticCommand.Commerce, null),
                    ExactTask(DomesticCommand.Cultivate, null)),
                Read(FakeMemory.Create(10, 500)));
            Assert(result.Cities[0].Tasks[1].SkipReason == UnverifiedPreviewSkipReason.DuplicateCommand
                    && result.Cities[0].Tasks[1].SelectedOfficers.Count == 0,
                "A repeated command was not prevented.");
            Assert(result.Cities[0].Tasks[2].SelectedOfficers.Count == 5
                    && result.SelectedOfficerCount == 10
                    && result.RemainingMoneyByCorps.Single().RemainingMoney == 0,
                "A repeated command consumed resources before the later unique task.");
        }

        private static void VerifyProjectorStructuralAbort()
        {
            San9Pk101AvailabilityReport report = Read(FakeMemory.Create(10, 500));
            CommandAvailabilityObservation[] original = report.Cities[0].Commands;
            report.Cities[0].Commands = new[]
            {
                original[0], original[1], original[1], original[3], original[4]
            };
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(
                    ExactTask(DomesticCommand.Patrol, null),
                    ExactTask(DomesticCommand.Commerce, null)),
                report);
            Assert(result.DiagnosticOnly
                    && result.Cities.Count == 0
                    && result.SelectedOfficerCount == 0
                    && result.RemainingMoneyByCorps.Count == 0,
                "A command-set contradiction leaked a partial preview.");
            Assert(result.Issues.Any(issue => issue.Code == "PREVIEW_COMMAND_SET_INVALID"),
                "The structural contradiction was not typed.");
        }

        private static void VerifyProjectorEvidenceAbort()
        {
            San9Pk101AvailabilityReport report = Read(FakeMemory.Create(10, 500));
            CommandAvailabilityObservation commerce = report.Cities[0].Commands
                .Single(command => command.Command == DomesticCommandKind.Commerce);
            AvailabilityCondition original = commerce.Conditions
                .Single(condition => condition.Code == "COMMAND_ORDER_BIT_CLEAR");
            commerce.Conditions = commerce.Conditions.Select(condition =>
                condition == original
                    ? new AvailabilityCondition(
                        original.Code,
                        original.Passed,
                        AvailabilityEvidence.ConfirmedStaticRule,
                        original.Detail)
                    : condition).ToArray();

            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(
                    ExactTask(DomesticCommand.Patrol, null),
                    ExactTask(DomesticCommand.Commerce, null)),
                report);
            Assert(result.DiagnosticOnly
                    && result.Cities.Count == 0
                    && result.SelectedOfficerCount == 0
                    && result.RemainingMoneyByCorps.Count == 0,
                "Contradictory evidence leaked a partial preview.");
            Assert(result.Issues.Any(issue =>
                    issue.Code == "PREVIEW_CONDITION_EVIDENCE_MISMATCH"),
                "The evidence contradiction was not typed.");
            AssertNonActionable(result);
        }

        private static San9Pk101UnverifiedPreviewResult Project(
            ExecutionPlan plan,
            San9Pk101AvailabilityReport report)
        {
            return new San9Pk101UnverifiedPreviewProjector().Project(plan, report);
        }

        private static void VerifyProjectorScoreContradiction()
        {
            San9Pk101AvailabilityReport report = Read(FakeMemory.Create(5, 250));
            CommandAvailabilityObservation commerce = report.Cities.Single().Commands
                .Single(command => command.Command == DomesticCommandKind.Commerce);
            commerce.RankedCandidates[0].Score = checked(commerce.RankedCandidates[0].Score + 1);

            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(ExactTask(DomesticCommand.Commerce, null)),
                report);
            Assert(result.DiagnosticOnly && result.Cities.Count == 0,
                "A ranked/source score contradiction leaked a partial projection.");
            Assert(result.Issues.Any(issue => issue.Code == "PREVIEW_RANKED_CANDIDATES_INVALID"),
                "The ranked/source score contradiction was not typed.");
            AssertNonActionable(result);
        }

        private static void VerifyProjectorNativeBestProvenance()
        {
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(ExactTask(DomesticCommand.Commerce, null)),
                Read(FakeMemory.Create(5, 250)));
            San9Pk101UnverifiedTaskPreview task = result.Cities.Single().Tasks.Single();

            Assert(task.RequestedSelectionPolicy == SelectionPolicy.NativeBest,
                "The requested native_best policy was not preserved.");
            Assert(task.AppliedRankingSource == UnverifiedPreviewRankingSource.StaticStatProxy,
                "An unverified native result was presented as the applied ranking source.");
            Assert(result.Issues.Any(issue => issue.Code == "PREVIEW_NATIVE_BEST_STATIC_PROXY"),
                "The native_best proxy substitution was not exposed as typed provenance.");
            AssertNonActionable(result);
        }

        private static void VerifyProjectorFallbackProvenance()
        {
            TaskConfiguration fallback = new TaskConfiguration(
                DomesticCommand.Patrol,
                true,
                5,
                5,
                true,
                SelectionPolicy.VerifiedStatFallback,
                null);
            San9Pk101UnverifiedPreviewResult result = Project(
                CompilePlan(fallback),
                Read(FakeMemory.Create(5, 0)));
            San9Pk101UnverifiedTaskPreview task = result.Cities.Single().Tasks.Single();

            Assert(task.RequestedSelectionPolicy == SelectionPolicy.VerifiedStatFallback,
                "The requested verified_stat_fallback policy was not preserved.");
            Assert(task.AppliedRankingSource == UnverifiedPreviewRankingSource.StaticStatProxy,
                "The fallback did not report the static-stat ranking source.");
            Assert(!result.Issues.Any(issue => issue.Code == "PREVIEW_NATIVE_BEST_STATIC_PROXY"),
                "Fallback was mislabeled as a native_best substitution.");
            AssertNonActionable(result);
        }

        private static ExecutionPlan CompilePlan(params TaskConfiguration[] tasks)
        {
            DomesticConfiguration configuration = new DomesticConfiguration(
                1,
                new[]
                {
                    new ProfileConfiguration("synthetic", "Synthetic", true, tasks)
                },
                CityScope.DirectCities,
                CityOrder.GameIdAscending,
                0,
                true);
            return new ExecutionPlanCompiler().Compile(configuration).GetRequired("synthetic");
        }

        private static TaskConfiguration ExactTask(DomesticCommand command, int? reserve)
        {
            return new TaskConfiguration(
                command,
                true,
                5,
                5,
                true,
                SelectionPolicy.NativeBest,
                reserve);
        }

        private static CityAvailabilityObservation CloneCity(
            CityAvailabilityObservation source,
            int cityId,
            int personIdOffset)
        {
            return new CityAvailabilityObservation
            {
                CityId = cityId,
                CityName = "C" + cityId,
                CorpsId = source.CorpsId,
                CorpsMoney = source.CorpsMoney,
                CorpsFlags = source.CorpsFlags,
                IsDirectlyControlled = true,
                Commands = source.Commands.Select(command =>
                    CloneCommand(command, personIdOffset)).ToArray()
            };
        }

        private static CommandAvailabilityObservation CloneCommand(
            CommandAvailabilityObservation source,
            int personIdOffset)
        {
            return new CommandAvailabilityObservation
            {
                Command = source.Command,
                ReadyCandidateCount = source.ReadyCandidateCount,
                ReadyCandidatesInSourceOrder = source.ReadyCandidatesInSourceOrder
                    .Select(item => CloneOfficer(item, personIdOffset)).ToArray(),
                RankedCandidates = source.RankedCandidates
                    .Select(item => CloneOfficer(item, personIdOffset)).ToArray(),
                ProvisionalCostPerOfficer = source.ProvisionalCostPerOfficer,
                NativeOneOfficerCostCandidate = source.NativeOneOfficerCostCandidate,
                KnownStaticSubsetWouldPass = source.KnownStaticSubsetWouldPass,
                Conditions = source.Conditions
            };
        }

        private static OfficerRankingPreview CloneOfficer(
            OfficerRankingPreview source,
            int personIdOffset)
        {
            return new OfficerRankingPreview
            {
                PersonId = source.PersonId + personIdOffset,
                Name = source.Name + "B",
                SourceListIndex = source.SourceListIndex,
                Score = source.Score
            };
        }

        private static void AssertSequence(
            IEnumerable<int> actual,
            IEnumerable<int> expected,
            string message)
        {
            Assert(actual.SequenceEqual(expected), message);
        }

        private static void AssertNonActionable(San9Pk101UnverifiedPreviewResult result)
        {
            Assert(!result.IsActionable && !result.CommitAuthorized, "Result exposed authorization.");
            foreach (San9Pk101UnverifiedPreviewIssue issue in result.Issues)
                Assert(!issue.IsActionable && !issue.CommitAuthorized, "Issue exposed authorization.");
            foreach (San9Pk101UnverifiedCityPreview city in result.Cities)
            {
                Assert(!city.IsActionable && !city.CommitAuthorized, "City exposed authorization.");
                foreach (San9Pk101UnverifiedTaskPreview task in city.Tasks)
                {
                    Assert(!task.IsActionable && !task.CommitAuthorized, "Task exposed authorization.");
                    foreach (San9Pk101UnverifiedOfficerPreview officer in task.SelectedOfficers)
                        Assert(!officer.IsActionable && !officer.CommitAuthorized,
                            "Officer exposed authorization.");
                }
            }
            foreach (San9Pk101UnverifiedCorpsMoneyPreview money in result.RemainingMoneyByCorps)
                Assert(!money.IsActionable && !money.CommitAuthorized, "Money DTO exposed authorization.");
        }

        private static San9Pk101AvailabilityReport Read(FakeMemory fake)
        {
            San9Pk101DiagnosticReport baseline = new San9Pk101DiagnosticReport
            {
                FileValidation = new FileValidationDiagnostic { IsValid = true },
                ProcessDiscovery = new ProcessDiscoveryDiagnostic
                {
                    Status = ProcessDiscoveryStatus.Unique,
                    SelectedProcessId = fake.ProcessId
                },
                ReadOnlyConnection = new ReadOnlyConnectionDiagnostic
                {
                    Attempted = true,
                    Connected = true,
                    ProcessId = fake.ProcessId
                },
                ConflictScan = new ConflictScanDiagnostic
                {
                    ProcessScanSucceeded = true,
                    ModuleScanAttempted = true,
                    ModuleScanSucceeded = true
                },
                ExecutionAllowed = true
            };
            return new San9Pk101AvailabilityReader().Read(fake, baseline,
                San9Pk101Target.ExpectedExecutablePath);
        }

        private static void VerifyUiSelectorObservation()
        {
            UiFakeMemory fake = UiFakeMemory.Create(false);
            San9Pk101UiObservationReader reader = new San9Pk101UiObservationReader(
                delegate(int processId) { return new StaticUiCandidateScanner(fake.Scan); },
                delegate(int milliseconds) { },
                0,
                delegate(int processId) { return 0x9001; });
            San9Pk101UiObservationReport report = reader.Read(fake, CreateUiBaseline(fake.ProcessId));
            Assert(report.ReadSucceeded && report.StableAbc, JoinUi(report));
            Assert(report.Layer == UiLayerKind.OfficerSelector, "Selector layer was not identified.");
            AssertSequence(report.CandidateOfficerIds, new[] { 0, 1, 2, 3, 4 }, "Candidate source order changed.");
            AssertSequence(report.SelectedOfficerIds, new[] { 0, 2 }, "Exact selector flag state changed.");
            UiObservationField current = report.Fields.Single(item => item.Name == "VerifiedCurrentCityId");
            Assert(current.State == UiObservationState.Verified && current.Value == "0",
                "Two independent city paths did not verify city zero.");
            Assert(!report.PlanningReady && !report.VerifiedNativeCapability
                    && !report.IsActionable && !report.CommitAuthorized,
                "UI observation exposed an execution capability.");
        }

        private static void VerifyUiCityContradiction()
        {
            UiFakeMemory fake = UiFakeMemory.Create(true);
            San9Pk101UiObservationReader reader = new San9Pk101UiObservationReader(
                delegate(int processId) { return new StaticUiCandidateScanner(fake.Scan); },
                delegate(int milliseconds) { },
                0,
                delegate(int processId) { return 0x9001; });
            San9Pk101UiObservationReport report = reader.Read(fake, CreateUiBaseline(fake.ProcessId));
            Assert(report.ReadSucceeded, JoinUi(report));
            UiObservationField current = report.Fields.Single(item => item.Name == "VerifiedCurrentCityId");
            Assert(current.State == UiObservationState.Contradictory,
                "A controller/selector city disagreement was not contradictory.");
            Assert(report.Issues.Any(item => item.Code == "UI_CURRENT_CITY_CONTRADICTORY"),
                "Contradictory city did not produce a blocking diagnostic.");
        }

        private static void VerifyEasyCompatibilityProfile()
        {
            EasyRuntimeBinding binding = CreateEasyBinding(0x10000000u, 6001);
            ConflictScanDiagnostic scan = CreateExactEasyScan(binding);
            Assert(San9Pk101ConflictDetector.ContainsOnlyExactCompatibleEasy(scan),
                "The exact paired Easy profile was rejected.");
            scan.Conflicts[1].Path = @"C:\unexpected\Easy.dll";
            Assert(!San9Pk101ConflictDetector.ContainsOnlyExactCompatibleEasy(scan),
                "An Easy module path drift was allowlisted.");
            Assert(!San9Pk101ConflictDetector.ContainsOnlyExactCompatibleEasy(
                new ConflictScanDiagnostic { Conflicts = new ConflictDiagnostic[0] }),
                "An absent Easy pair was allowlisted.");
        }

        private static void VerifyEasyManifestOracle()
        {
            const string independentExpectedDescriptorSha256 =
                "FD4EEE83D262719288B86C7A1BD2CD6E68EB738C00D32526C2F02333F701FF40";
            const string independentlyHashedJsonManifest =
                "72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE";
            StringBuilder canonical = new StringBuilder();
            EasyRedirectDefinition[] definitions = EasyCompatibilityManifest.Redirects;
            Assert(definitions.Length == 32, "The frozen redirect count is not 32.");
            foreach (EasyRedirectDefinition definition in definitions)
            {
                canonical.AppendFormat(
                    "{0}|{1:X8}|{2}|{3}|{4:X2}|{5:X8}|{6};",
                    definition.Id,
                    definition.Address,
                    definition.Length,
                    Hex(definition.Original),
                    definition.Opcode,
                    definition.TargetRva,
                    Hex(definition.Trailing));
            }
            canonical.AppendFormat(
                "AUX|{0:X8}|{1:X8}|{2:X8}|{3:X8}|{4:X8}|{5:X8}|TRAIN_ORIGINAL={6:X2}|TRAIN_ALLOWED={6:X2}/{6:X2},{7:X2}/{7:X2}|FOOD_ORIGINAL={8}|FOOD_INSTALLED={9}|AI_ORIGINAL={10:X2}|AI_INSTALLED={11:X2}|HWND_ORIGINAL={12:X8}|HWND_INSTALLED=EXACT_BOUND_GAME_HWND;",
                EasyCompatibilityManifest.ChildTrainingAAddress,
                EasyCompatibilityManifest.ChildTrainingBAddress,
                EasyCompatibilityManifest.MaxCorpsFoodAAddress,
                EasyCompatibilityManifest.MaxCorpsFoodBAddress,
                EasyCompatibilityManifest.AiCounterBarbariansAddress,
                EasyCompatibilityManifest.HwndSlotRva,
                EasyCompatibilityManifest.ChildTrainingOriginalValue,
                EasyCompatibilityManifest.ChildTrainingAlternateValue,
                Hex(EasyCompatibilityManifest.MaxCorpsFoodOriginal),
                Hex(EasyCompatibilityManifest.MaxCorpsFoodInstalled),
                EasyCompatibilityManifest.AiCounterBarbariansOriginalValue,
                EasyCompatibilityManifest.AiCounterBarbariansInstalledValue,
                EasyCompatibilityManifest.HwndOriginalValue);
            canonical.AppendFormat("PAGE|{0:X8}|{1:X8}|ORIGINAL={2:X8}|INSTALLED={3:X8};",
                EasyCompatibilityManifest.ProtectedPageAddress,
                EasyCompatibilityManifest.ProtectedPageLength,
                EasyCompatibilityManifest.OriginalPageProtection,
                EasyCompatibilityManifest.InstalledPageProtection);
            canonical.AppendFormat("IDLE|SLOT={0:X8}|VALUE={1:X8}|ORIGINAL={1:X8}|PREFIX={2}|CALLER_BASE={3:X8}|CALLER_RETURN={4:X8}|CALLER={5};",
                EasyCompatibilityManifest.IdleSlotAddress,
                EasyCompatibilityManifest.OriginalIdleAddress,
                Hex(EasyCompatibilityManifest.OriginalIdlePrefix),
                EasyCompatibilityManifest.IdleCallerAnchorAddress,
                EasyCompatibilityManifest.IdleCallerReturnAddress,
                Hex(EasyCompatibilityManifest.IdleCallerAnchor));
            Assert(string.Equals(Sha256(Encoding.ASCII.GetBytes(canonical.ToString())),
                    independentExpectedDescriptorSha256, StringComparison.Ordinal),
                "A compiled Easy descriptor field drifted from the independent manifest oracle.");
            Assert(string.Equals(EasyCompatibilityManifest.ManifestSha256,
                    independentlyHashedJsonManifest, StringComparison.Ordinal),
                "The compiled manifest identity drifted from the independently hashed JSON.");

            // A caller-controlled JSON string has no input path into the compiled descriptor.
            string callerTamperedJson = "{\"redirect_writes\":[],\"sha256\":\"attacker-controlled\"}";
            Assert(callerTamperedJson.Length != 0
                    && EasyCompatibilityManifest.Redirects.Length == 32
                    && EasyCompatibilityManifest.Redirects[0].Address == 0x004E6270u,
                "Caller data altered the frozen production descriptor.");
        }

        private static void VerifyEasyRuntimePointMatrix()
        {
            EasyRuntimeBinding binding = CreateEasyBinding(0x10000000u, 6101);
            EasyRuntimeSnapshot installed = CreateInstalledEasySnapshot(binding);
            Assert(Hex(installed.RedirectBytes[30]) == "E85E3CBC0F90"
                    && Hex(installed.RedirectBytes[31]) == "E82A39BA0F90",
                "CALL+NOP preferred-base rel32 bytes differ from the independent manifest oracle.");
            EasyRuntimeCompatibilityReport good = EasyRuntimeSnapshotVerifier.Verify(
                binding, CloneEasySnapshot(installed), CloneEasySnapshot(installed));
            Assert(good.State == EasyRuntimeCompatibilityState.Installed
                    && good.CompatibleForFutureBridge && good.StableSnapshot
                    && good.Ticket != null && !good.Ticket.ExecutionAuthorized
                    && !good.Ticket.IsSecurityCapability
                    && !good.ExecutionAuthorized,
                "The exact installed snapshot did not yield a compatibility-only ticket.");

            for (int index = 0; index < EasyCompatibilityManifest.Redirects.Length; index++)
            {
                EasyRuntimeSnapshot mutated = CloneEasySnapshot(installed);
                mutated.RedirectBytes[index][mutated.RedirectBytes[index].Length - 1] ^= 0x01;
                EasyRuntimeCompatibilityReport report = EasyRuntimeSnapshotVerifier.Verify(
                    binding, CloneEasySnapshot(mutated), mutated);
                Assert(!report.CompatibleForFutureBridge && report.UnknownRedirectCount == 1,
                    "Redirect mutation was accepted at index " + index + ".");
            }
            EasyRuntimeSnapshot wrongOpcode = CloneEasySnapshot(installed);
            wrongOpcode.RedirectBytes[5][0] ^= 0x01;
            Assert(!EasyRuntimeSnapshotVerifier.Verify(binding, wrongOpcode, CloneEasySnapshot(wrongOpcode))
                    .CompatibleForFutureBridge,
                "A wrong redirect opcode was accepted.");

            EasyRuntimeSnapshot trainingZero = CloneEasySnapshot(installed);
            trainingZero.ChildTrainingA = EasyCompatibilityManifest.ChildTrainingAlternateValue;
            trainingZero.ChildTrainingB = EasyCompatibilityManifest.ChildTrainingAlternateValue;
            Assert(EasyRuntimeSnapshotVerifier.Verify(binding, trainingZero, CloneEasySnapshot(trainingZero))
                    .State == EasyRuntimeCompatibilityState.Installed,
                "The installed 00/00 child-training option was rejected.");
            EasyRuntimeSnapshot trainingMixed = CloneEasySnapshot(installed);
            trainingMixed.ChildTrainingA = EasyCompatibilityManifest.ChildTrainingAlternateValue;
            Assert(!EasyRuntimeSnapshotVerifier.Verify(binding, trainingMixed, CloneEasySnapshot(trainingMixed))
                    .CompatibleForFutureBridge,
                "A mixed 00/32 child-training pair was accepted.");

            EasyRuntimeSnapshot[] auxiliaryMutations =
            {
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.MaxCorpsFoodA[0] ^= 1; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.MaxCorpsFoodB[0] ^= 1; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.AiCounterBarbarians = EasyCompatibilityManifest.AiCounterBarbariansOriginalValue; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.BoundHwnd++; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.ProtectedPageProtection = 0x02; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.ProtectedPageState = 0; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.ProtectedRegionSize = 0x800; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.IdleSlotValue++; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.OriginalIdlePrefix[0] ^= 1; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.IdleCallerAnchor[0] ^= 1; })
            };
            for (int index = 0; index < auxiliaryMutations.Length; index++)
            {
                EasyRuntimeSnapshot mutated = auxiliaryMutations[index];
                Assert(!EasyRuntimeSnapshotVerifier.Verify(binding, mutated, CloneEasySnapshot(mutated))
                        .CompatibleForFutureBridge,
                    "Auxiliary/protection/idle mutation was accepted at index " + index + ".");
            }

            EasyRuntimeSnapshot original = CreateOriginalEasySnapshot(binding);
            Assert(EasyRuntimeSnapshotVerifier.Verify(binding, original, CloneEasySnapshot(original)).State
                    == EasyRuntimeCompatibilityState.Original,
                "The exact original snapshot was not classified as non-production cleanup evidence.");
            EasyRuntimeSnapshot originalUncommitted = CloneEasySnapshot(original);
            originalUncommitted.ProtectedPageState = 0;
            Assert(EasyRuntimeSnapshotVerifier.Verify(
                    binding, originalUncommitted, CloneEasySnapshot(originalUncommitted)).State
                    == EasyRuntimeCompatibilityState.PartialOrUnknown,
                "Original classification ignored MEM_COMMIT.");
            EasyRuntimeSnapshot originalShortRegion = CloneEasySnapshot(original);
            originalShortRegion.ProtectedRegionSize = EasyCompatibilityManifest.ProtectedPageLength - 1;
            Assert(EasyRuntimeSnapshotVerifier.Verify(
                    binding, originalShortRegion, CloneEasySnapshot(originalShortRegion)).State
                    == EasyRuntimeCompatibilityState.PartialOrUnknown,
                "Original classification ignored full-page coverage.");
            original.ChildTrainingA = EasyCompatibilityManifest.ChildTrainingAlternateValue;
            original.ChildTrainingB = EasyCompatibilityManifest.ChildTrainingAlternateValue;
            Assert(EasyRuntimeSnapshotVerifier.Verify(binding, original, CloneEasySnapshot(original)).State
                    == EasyRuntimeCompatibilityState.PartialOrUnknown,
                "Original-mode 00/00 child-training bytes were incorrectly accepted as original.");
        }

        private static void VerifyEasyRuntimeBases()
        {
            EasyRuntimeBinding relocated = CreateEasyBinding(0x90000000u, 6201);
            EasyRuntimeSnapshot installed = CreateInstalledEasySnapshot(relocated);
            Assert(Hex(installed.RedirectBytes[0]) == "E99BAEB18F"
                    && Hex(installed.RedirectBytes[30]) == "E85E3CBC8F90"
                    && Hex(installed.RedirectBytes[31]) == "E82A39BA8F90",
                "The high-base rel32 encoder did not match the independent modulo-2^32 oracle.");
            Assert(EasyRuntimeSnapshotVerifier.Verify(relocated, installed, CloneEasySnapshot(installed)).State
                    == EasyRuntimeCompatibilityState.Installed,
                "A high non-default Easy base failed modulo-2^32 rel32 validation.");

            EasyRuntimeBinding wrongBase = CreateEasyBinding(0x91000000u, 6202);
            Assert(!EasyRuntimeSnapshotVerifier.Verify(wrongBase, installed, CloneEasySnapshot(installed))
                    .CompatibleForFutureBridge,
                "Redirects bound to a different Easy base were accepted.");

            EasyRuntimeBinding overflow = CreateEasyBinding(0xFFFFA000u, 6203);
            string error;
            Assert(!EasyRuntimeSnapshotVerifier.IsExactBinding(overflow, out error),
                "An Easy image crossing 0xFFFFFFFF was accepted.");
        }

        private static void VerifyEasyRuntimeStabilityAndIdentity()
        {
            EasyRuntimeBinding binding = CreateEasyBinding(0x10000000u, 6301);
            EasyRuntimeSnapshot first = CreateInstalledEasySnapshot(binding);
            EasyRuntimeSnapshot second = CloneEasySnapshot(first);
            second.RedirectBytes[7][0] ^= 1;
            Assert(EasyRuntimeSnapshotVerifier.Verify(binding, first, second).State
                    == EasyRuntimeCompatibilityState.SnapshotUnstable,
                "An A/B redirect change was not rejected as unstable.");
            second = CloneEasySnapshot(first);
            second.ChildTrainingA = EasyCompatibilityManifest.ChildTrainingAlternateValue;
            second.ChildTrainingB = EasyCompatibilityManifest.ChildTrainingAlternateValue;
            Assert(EasyRuntimeSnapshotVerifier.Verify(binding, first, second).State
                    == EasyRuntimeCompatibilityState.SnapshotUnstable,
                "An A/B checkbox transition was not rejected as unstable.");

            EasyRuntimeBinding wrongHash = CreateEasyBinding(0x10000000u, 6302);
            wrongHash.EasyModuleFileSha256 = new string('0', 64);
            string error;
            Assert(!EasyRuntimeSnapshotVerifier.IsExactBinding(wrongHash, out error),
                "A wrong Easy.dll hash was accepted.");

            ConflictScanDiagnostic exact = CreateExactEasyScan(binding);
            Assert(San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "The exact fresh identity scan was rejected.");
            exact.ProcessScanSucceeded = false;
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "A failed fresh process scan was accepted.");
            exact = CreateExactEasyScan(binding);
            exact.ModuleScanAttempted = false;
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "An unattempted fresh module scan was accepted.");
            exact = CreateExactEasyScan(binding);
            exact.ModuleScanSucceeded = false;
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "A failed fresh module scan was accepted.");
            exact = CreateExactEasyScan(binding);
            exact.Conflicts[0].ProcessCreationFileTimeUtc++;
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "A changed loader generation was accepted.");

            exact = CreateExactEasyScan(binding);
            exact.Conflicts[0].FileSha256 = new string('1', 64);
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "A loader file-hash mutation was accepted by the fresh scan gate.");
            exact = CreateExactEasyScan(binding);
            exact.Conflicts[1].FileSize++;
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "An Easy.dll file-size mutation was accepted by the fresh scan gate.");
            exact = CreateExactEasyScan(binding);
            exact.Conflicts[1].FileSha256 = new string('2', 64);
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "An Easy.dll file-hash mutation was accepted by the fresh scan gate.");
            exact = CreateExactEasyScan(binding);
            exact.Conflicts[1].ModuleBaseAddress++;
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "A loaded Easy.dll base mutation was accepted by the fresh scan gate.");

            exact = CreateExactEasyScan(binding);
            exact.Conflicts = exact.Conflicts.Concat(new[]
            {
                new ConflictDiagnostic
                {
                    Kind = ConflictKind.KnownModule,
                    Code = "KNOWN_CONFLICT_MODULE",
                    Name = "SanIXSpy.dll",
                    IsBlocking = true
                }
            }).ToArray();
            exact.HasBlockingConflicts = true;
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "A newly introduced conflicting module was ignored.");

            exact = CreateExactEasyScan(binding);
            exact.Conflicts = exact.Conflicts.Concat(new[] { CloneConflict(exact.Conflicts[0]) }).ToArray();
            Assert(!San9Pk101EasyRuntimeInspector.IsFreshScanExact(binding, exact),
                "A second exact loader was ignored.");
        }

        private static void VerifyEasyHookEpoch()
        {
            EasyRuntimeBinding binding = CreateEasyBinding(0x10000000u, 6401);
            EasyRuntimeSnapshot installed = CreateInstalledEasySnapshot(binding);
            EasyRuntimeCompatibilityReport installedReport = EasyRuntimeSnapshotVerifier.Verify(
                binding, installed, CloneEasySnapshot(installed));
            EasyRuntimeCompatibilityReport partial = EasyRuntimeSnapshotVerifier.Verify(
                binding,
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.RedirectBytes[0][0] ^= 1; }),
                Mutate(installed, delegate(EasyRuntimeSnapshot value) { value.RedirectBytes[0][0] ^= 1; }));

            EasyHookEpochTracker preArm = new EasyHookEpochTracker();
            Assert(!preArm.Observe(binding, partial).RestartRequired,
                "A pre-arm partial observation permanently latched restart.");
            EasyRuntimeCompatibilityReport preArmRestored = preArm.Observe(binding,
                EasyRuntimeSnapshotVerifier.Verify(binding, installed, CloneEasySnapshot(installed)));
            Assert(preArmRestored.State == EasyRuntimeCompatibilityState.Installed,
                "A pre-arm partial-to-stable transition did not recover normally.");
            Assert(!preArm.Observe(binding, partial).RestartRequired,
                "Passive Diagnose/Observe armed the epoch without an explicit bridge notification.");
            Assert(preArm.Observe(binding, EasyRuntimeSnapshotVerifier.Verify(
                    binding, installed, CloneEasySnapshot(installed))).State
                    == EasyRuntimeCompatibilityState.Installed,
                "A passive observation incorrectly latched a later recovery.");

            EasyCompatibilityTicket staleTicket = preArmRestored.Ticket;
            preArm.Observe(binding, partial);
            Assert(!preArm.NotifyBridgeInstalled(staleTicket),
                "A stale ticket armed after a newer non-exact observation.");

            EasyHookEpochTracker postArm = new EasyHookEpochTracker();
            postArm.Observe(binding, installedReport);
            Assert(postArm.NotifyBridgeInstalled(installedReport.Ticket),
                "The latest exact ticket did not explicitly arm the bridge epoch.");
            San9Pk101DiagnosticReport missingBaseline = new San9Pk101DiagnosticReport
            {
                ReadOnlyConnection = new ReadOnlyConnectionDiagnostic
                {
                    Connected = true,
                    ProcessId = binding.GameProcessId,
                    ProcessCreationFileTimeUtc = binding.GameCreationFileTimeUtc,
                    MainModuleBaseAddress = binding.GameImageBase,
                    MainModuleSize = binding.GameImageSize
                },
                ConflictScan = new ConflictScanDiagnostic { Conflicts = new ConflictDiagnostic[0] }
            };
            EasyRuntimeBinding missingGenerationBinding;
            EasyRuntimeCompatibilityReport missing;
            Assert(!San9Pk101EasyRuntimeInspector.TryCreateBinding(
                    missingBaseline, out missingGenerationBinding, out missing)
                    && missingGenerationBinding != null
                    && missingGenerationBinding.GenerationKey == binding.GenerationKey
                    && missing.State == EasyRuntimeCompatibilityState.Missing,
                "Missing Easy did not preserve the bound game generation for epoch latching.");
            Assert(postArm.Observe(missingGenerationBinding, missing).RestartRequired,
                "A post-arm missing observation did not latch restart.");
            EasyRuntimeCompatibilityReport restored = postArm.Observe(binding,
                EasyRuntimeSnapshotVerifier.Verify(binding, installed, CloneEasySnapshot(installed)));
            Assert(restored.State == EasyRuntimeCompatibilityState.RestartRequired
                    && restored.Ticket == null,
                "A restored hook escaped the post-arm restart latch.");

            EasyHookEpochTracker sharedA = EasyHookEpochTracker.Shared;
            EasyHookEpochTracker sharedB = EasyHookEpochTracker.Shared;
            EasyRuntimeBinding sharedBinding = CreateEasyBinding(0x10000000u, 987654321);
            EasyRuntimeSnapshot sharedSnapshot = CreateInstalledEasySnapshot(sharedBinding);
            EasyRuntimeCompatibilityReport sharedInstalled = sharedA.Observe(sharedBinding,
                EasyRuntimeSnapshotVerifier.Verify(
                    sharedBinding, sharedSnapshot, CloneEasySnapshot(sharedSnapshot)));
            Assert(sharedA.NotifyBridgeInstalled(sharedInstalled.Ticket),
                "The process-wide tracker did not accept an exact current ticket.");
            sharedA.Observe(sharedBinding, new EasyRuntimeCompatibilityReport
            {
                State = EasyRuntimeCompatibilityState.Ambiguous,
                Issues = new[] { new DiagnosticIssue("EASY_EXACT_PAIR_AMBIGUOUS", DiagnosticSeverity.Blocking, "synthetic") }
            });
            Assert(sharedB.Observe(sharedBinding, EasyRuntimeSnapshotVerifier.Verify(
                    sharedBinding, sharedSnapshot, CloneEasySnapshot(sharedSnapshot))).RestartRequired,
                "A second adapter-facing reference cleared the process-wide restart latch.");

            San9Pk101DiagnosticReport ambiguousBaseline = new San9Pk101DiagnosticReport
            {
                ReadOnlyConnection = new ReadOnlyConnectionDiagnostic
                {
                    Connected = true,
                    ProcessId = binding.GameProcessId,
                    ProcessCreationFileTimeUtc = binding.GameCreationFileTimeUtc,
                    MainModuleBaseAddress = binding.GameImageBase,
                    MainModuleSize = binding.GameImageSize
                },
                ConflictScan = CreateExactEasyScan(binding)
            };
            ambiguousBaseline.ConflictScan.Conflicts = ambiguousBaseline.ConflictScan.Conflicts
                .Concat(new[] { CloneConflict(ambiguousBaseline.ConflictScan.Conflicts[0]) }).ToArray();
            EasyRuntimeBinding ambiguousGenerationBinding;
            EasyRuntimeCompatibilityReport ambiguousFailure;
            Assert(!San9Pk101EasyRuntimeInspector.TryCreateBinding(
                    ambiguousBaseline, out ambiguousGenerationBinding, out ambiguousFailure)
                    && ambiguousGenerationBinding.GenerationKey == binding.GenerationKey
                    && ambiguousFailure.State == EasyRuntimeCompatibilityState.Ambiguous,
                "Ambiguous Easy did not preserve the bound game generation for epoch latching.");
            EasyHookEpochTracker ambiguousTracker = new EasyHookEpochTracker();
            EasyRuntimeCompatibilityReport ambiguousInstalled = ambiguousTracker.Observe(binding,
                EasyRuntimeSnapshotVerifier.Verify(binding, installed, CloneEasySnapshot(installed)));
            Assert(ambiguousTracker.NotifyBridgeInstalled(ambiguousInstalled.Ticket),
                "The ambiguous-loss fixture did not explicitly arm.");
            ambiguousTracker.Observe(ambiguousGenerationBinding, ambiguousFailure);
            Assert(ambiguousTracker.Observe(binding, EasyRuntimeSnapshotVerifier.Verify(
                    binding, installed, CloneEasySnapshot(installed))).RestartRequired,
                "A post-arm duplicate/ambiguous Easy observation did not survive restoration.");

            EasyRuntimeBinding newGame = CreateEasyBinding(0x10000000u, 987654322);
            EasyRuntimeSnapshot newGameSnapshot = CreateInstalledEasySnapshot(newGame);
            Assert(sharedB.Observe(newGame, EasyRuntimeSnapshotVerifier.Verify(
                    newGame, newGameSnapshot, CloneEasySnapshot(newGameSnapshot))).State
                    == EasyRuntimeCompatibilityState.Installed,
                "A new game creation generation inherited the old generation's latch.");

            EasyHookEpochTracker noBindingTracker = new EasyHookEpochTracker();
            EasyRuntimeCompatibilityReport noBindingInstalled = noBindingTracker.Observe(binding,
                EasyRuntimeSnapshotVerifier.Verify(binding, installed, CloneEasySnapshot(installed)));
            Assert(noBindingTracker.NotifyBridgeInstalled(noBindingInstalled.Ticket),
                "The no-binding fixture did not explicitly arm.");
            EasyRuntimeCompatibilityReport unavailable = noBindingTracker.Observe(
                null,
                new EasyRuntimeCompatibilityReport
                {
                    State = EasyRuntimeCompatibilityState.InspectionFailed,
                    Issues = new[] { new DiagnosticIssue(
                        "EASY_RUNTIME_NOT_READY", DiagnosticSeverity.Blocking, "synthetic") }
                });
            Assert(unavailable.RestartRequired,
                "A connection/scan failure with no recoverable binding did not poison the active armed generation.");
            Assert(noBindingTracker.Observe(binding, EasyRuntimeSnapshotVerifier.Verify(
                    binding, installed, CloneEasySnapshot(installed))).RestartRequired,
                "The active armed generation recovered after a binding-null failure.");

            EasyHookEpochTracker staleAfterGap = new EasyHookEpochTracker();
            EasyRuntimeCompatibilityReport exactBeforeGap = staleAfterGap.Observe(binding,
                EasyRuntimeSnapshotVerifier.Verify(binding, installed, CloneEasySnapshot(installed)));
            staleAfterGap.Observe(
                null,
                new EasyRuntimeCompatibilityReport
                {
                    State = EasyRuntimeCompatibilityState.InspectionFailed,
                    Issues = new[] { new DiagnosticIssue(
                        "EASY_RUNTIME_NOT_READY", DiagnosticSeverity.Blocking, "synthetic") }
                });
            Assert(!staleAfterGap.NotifyBridgeInstalled(exactBeforeGap.Ticket),
                "An exact ticket remained armable after an unbound inspection gap.");

            EasyHookEpochTracker bounded = new EasyHookEpochTracker();
            for (int index = 0; index < 80; index++)
            {
                EasyRuntimeBinding waiting = CreateEasyBinding(0x10000000u, 700000 + index);
                bounded.Observe(waiting, new EasyRuntimeCompatibilityReport
                {
                    State = EasyRuntimeCompatibilityState.Missing,
                    Issues = new DiagnosticIssue[0]
                });
            }
            Assert(bounded.TrackedGenerationCount <= 64,
                "The process-wide generation tracker grew beyond its hard bound.");

            EasyHookEpochTracker armedCapacity = new EasyHookEpochTracker();
            for (int index = 0; index < 64; index++)
            {
                EasyRuntimeBinding armedBinding = CreateEasyBinding(0x10000000u, 800000 + index);
                EasyRuntimeSnapshot armedSnapshot = CreateInstalledEasySnapshot(armedBinding);
                EasyRuntimeCompatibilityReport observed = armedCapacity.Observe(armedBinding,
                    EasyRuntimeSnapshotVerifier.Verify(
                        armedBinding, armedSnapshot, CloneEasySnapshot(armedSnapshot)));
                Assert(armedCapacity.NotifyBridgeInstalled(observed.Ticket),
                    "Failed to arm bounded generation " + index + ".");
            }
            EasyRuntimeBinding overflowBinding = CreateEasyBinding(0x10000000u, 900000);
            EasyRuntimeSnapshot overflowSnapshot = CreateInstalledEasySnapshot(overflowBinding);
            EasyRuntimeCompatibilityReport capacityBlocked = armedCapacity.Observe(overflowBinding,
                EasyRuntimeSnapshotVerifier.Verify(
                    overflowBinding, overflowSnapshot, CloneEasySnapshot(overflowSnapshot)));
            Assert(armedCapacity.TrackedGenerationCount == 64
                    && capacityBlocked.RestartRequired
                    && capacityBlocked.Issues.Any(item => item.Code == "EASY_EPOCH_TRACKER_CAPACITY"),
                "A 65th all-armed generation did not fail closed at the documented hard bound.");
        }

        private static EasyRuntimeBinding CreateEasyBinding(uint easyBase, long gameCreation)
        {
            return new EasyRuntimeBinding
            {
                GameProcessId = 4242,
                GameCreationFileTimeUtc = gameCreation,
                GameImageBase = San9Pk101Target.ExpectedImageBase,
                GameImageSize = San9Pk101Target.ExpectedSizeOfImage,
                GameWindowHandle = 0x00009001,
                LoaderProcessId = 4342,
                LoaderCreationFileTimeUtc = gameCreation + 10,
                LoaderPath = San9Pk101ConflictDetector.CompatibleEasyProcessPath,
                LoaderFileSize = San9Pk101ConflictDetector.CompatibleEasyProcessSize,
                LoaderFileSha256 = San9Pk101ConflictDetector.CompatibleEasyProcessSha256,
                EasyModuleBase = easyBase,
                EasyModuleImageSize = San9Pk101ConflictDetector.CompatibleEasyModuleImageSize,
                EasyModulePath = San9Pk101ConflictDetector.CompatibleEasyModulePath,
                EasyModuleFileSize = San9Pk101ConflictDetector.CompatibleEasyModuleFileSize,
                EasyModuleFileSha256 = San9Pk101ConflictDetector.CompatibleEasyModuleSha256
            };
        }

        private static EasyRuntimeSnapshot CreateInstalledEasySnapshot(EasyRuntimeBinding binding)
        {
            return new EasyRuntimeSnapshot
            {
                RedirectBytes = EasyCompatibilityManifest.Redirects
                    .Select(item => item.InstalledBytes(binding.EasyModuleBase)).ToArray(),
                ChildTrainingA = EasyCompatibilityManifest.ChildTrainingOriginalValue,
                ChildTrainingB = EasyCompatibilityManifest.ChildTrainingOriginalValue,
                MaxCorpsFoodA = EasyCompatibilityManifest.MaxCorpsFoodInstalled,
                MaxCorpsFoodB = EasyCompatibilityManifest.MaxCorpsFoodInstalled,
                AiCounterBarbarians = EasyCompatibilityManifest.AiCounterBarbariansInstalledValue,
                BoundHwnd = binding.GameWindowHandle,
                ProtectedPageBase = EasyCompatibilityManifest.ProtectedPageAddress,
                ProtectedRegionSize = EasyCompatibilityManifest.ProtectedPageLength,
                ProtectedPageState = 0x1000,
                ProtectedPageProtection = EasyCompatibilityManifest.InstalledPageProtection,
                IdleSlotValue = EasyCompatibilityManifest.OriginalIdleAddress,
                OriginalIdlePrefix = (byte[])EasyCompatibilityManifest.OriginalIdlePrefix.Clone(),
                IdleCallerAnchor = (byte[])EasyCompatibilityManifest.IdleCallerAnchor.Clone()
            };
        }

        private static EasyRuntimeSnapshot CreateOriginalEasySnapshot(EasyRuntimeBinding binding)
        {
            EasyRuntimeSnapshot result = CreateInstalledEasySnapshot(binding);
            result.RedirectBytes = EasyCompatibilityManifest.Redirects
                .Select(item => (byte[])item.Original.Clone()).ToArray();
            result.MaxCorpsFoodA = EasyCompatibilityManifest.MaxCorpsFoodOriginal;
            result.MaxCorpsFoodB = EasyCompatibilityManifest.MaxCorpsFoodOriginal;
            result.AiCounterBarbarians = EasyCompatibilityManifest.AiCounterBarbariansOriginalValue;
            result.BoundHwnd = EasyCompatibilityManifest.HwndOriginalValue;
            result.ProtectedPageProtection = EasyCompatibilityManifest.OriginalPageProtection;
            return result;
        }

        private static EasyRuntimeSnapshot CloneEasySnapshot(EasyRuntimeSnapshot value)
        {
            return new EasyRuntimeSnapshot
            {
                RedirectBytes = value.RedirectBytes.Select(item => (byte[])item.Clone()).ToArray(),
                ChildTrainingA = value.ChildTrainingA,
                ChildTrainingB = value.ChildTrainingB,
                MaxCorpsFoodA = (byte[])value.MaxCorpsFoodA.Clone(),
                MaxCorpsFoodB = (byte[])value.MaxCorpsFoodB.Clone(),
                AiCounterBarbarians = value.AiCounterBarbarians,
                BoundHwnd = value.BoundHwnd,
                ProtectedPageBase = value.ProtectedPageBase,
                ProtectedRegionSize = value.ProtectedRegionSize,
                ProtectedPageState = value.ProtectedPageState,
                ProtectedPageProtection = value.ProtectedPageProtection,
                IdleSlotValue = value.IdleSlotValue,
                OriginalIdlePrefix = (byte[])value.OriginalIdlePrefix.Clone(),
                IdleCallerAnchor = (byte[])value.IdleCallerAnchor.Clone()
            };
        }

        private static EasyRuntimeSnapshot Mutate(EasyRuntimeSnapshot source, Action<EasyRuntimeSnapshot> mutation)
        {
            EasyRuntimeSnapshot result = CloneEasySnapshot(source);
            mutation(result);
            return result;
        }

        private static ConflictScanDiagnostic CreateExactEasyScan(EasyRuntimeBinding binding)
        {
            return new ConflictScanDiagnostic
            {
                ProcessScanSucceeded = true,
                ModuleScanAttempted = true,
                ModuleScanSucceeded = true,
                Conflicts = new[]
                {
                    new ConflictDiagnostic
                    {
                        Kind = ConflictKind.KnownProcess,
                        Code = "COMPATIBLE_EASY_PROCESS",
                        Name = "San9PKEasy.exe",
                        ProcessId = binding.LoaderProcessId,
                        ProcessCreationFileTimeUtc = binding.LoaderCreationFileTimeUtc,
                        Path = binding.LoaderPath,
                        FileSize = binding.LoaderFileSize,
                        FileSha256 = binding.LoaderFileSha256,
                        IsBlocking = false
                    },
                    new ConflictDiagnostic
                    {
                        Kind = ConflictKind.KnownModule,
                        Code = "COMPATIBLE_EASY_MODULE",
                        Name = "Easy.dll",
                        ProcessId = binding.GameProcessId,
                        Path = binding.EasyModulePath,
                        FileSize = binding.EasyModuleFileSize,
                        FileSha256 = binding.EasyModuleFileSha256,
                        ModuleBaseAddress = binding.EasyModuleBase,
                        ModuleImageSize = binding.EasyModuleImageSize,
                        IsBlocking = false
                    }
                }
            };
        }

        private static ConflictDiagnostic CloneConflict(ConflictDiagnostic value)
        {
            return new ConflictDiagnostic
            {
                Kind = value.Kind,
                Code = value.Code,
                Name = value.Name,
                ProcessId = value.ProcessId,
                ProcessCreationFileTimeUtc = value.ProcessCreationFileTimeUtc,
                Path = value.Path,
                FileSize = value.FileSize,
                FileSha256 = value.FileSha256,
                ModuleBaseAddress = value.ModuleBaseAddress,
                ModuleImageSize = value.ModuleImageSize,
                Reason = value.Reason,
                IsBlocking = value.IsBlocking
            };
        }

        private static string Hex(byte[] value)
        {
            return BitConverter.ToString(value).Replace("-", string.Empty);
        }

        private static string Sha256(byte[] value)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(value)).Replace("-", string.Empty);
        }

        private static San9Pk101DiagnosticReport CreateUiBaseline(int processId)
        {
            return new San9Pk101DiagnosticReport
            {
                FileValidation = new FileValidationDiagnostic { IsValid = true },
                ProcessDiscovery = new ProcessDiscoveryDiagnostic
                {
                    Status = ProcessDiscoveryStatus.Unique,
                    SelectedProcessId = processId
                },
                ReadOnlyConnection = new ReadOnlyConnectionDiagnostic
                {
                    Attempted = true,
                    Connected = true,
                    ProcessId = processId
                },
                ConflictScan = new ConflictScanDiagnostic
                {
                    ProcessScanSucceeded = true,
                    ModuleScanAttempted = true,
                    ModuleScanSucceeded = true
                },
                ExecutionAllowed = true
            };
        }

        private static string JoinUi(San9Pk101UiObservationReport report)
        {
            return string.Join(" | ", report.Issues.Select(item => item.Code + ":" + item.Message).ToArray());
        }

        private static void Run(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { Failures.Add(name + " - " + exception.Message); }
        }

        private static void Assert(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static string Join(San9Pk101AvailabilityReport report)
        {
            return string.Join(" | ", report.Issues.Select(issue => issue.Code + ":" + issue.Message).ToArray());
        }

        private sealed class StaticUiCandidateScanner : IUiCandidateScanner
        {
            private readonly UiCandidateScan scan;

            internal StaticUiCandidateScanner(UiCandidateScan scan)
            {
                this.scan = scan;
            }

            public UiCandidateScan Scan(IReadOnlyProcessMemory memory, ProcessIdentitySnapshot expectedIdentity)
            {
                return scan;
            }

            public void Dispose()
            {
            }
        }

        private sealed class UiFakeMemory : IReadOnlyProcessMemory
        {
            private const uint HeapBase = 0x02000000;
            private const uint WindowPointer = 0x02001000;
            private const uint OwnerPointer = 0x02002000;
            private const uint ScenePointer = 0x02003000;
            private const uint RootPointer = 0x02004000;
            private const uint ControllerPointer = 0x02005000;
            private const uint OuterPointer = 0x02006000;
            private const uint SourceNodes = 0x02007000;
            private const uint WorkingNodes = 0x02007500;
            private const uint SelectorPointer = 0x02008000;
            private const uint SelectorRows = 0x02009000;
            private readonly SortedDictionary<uint, byte[]> regions = new SortedDictionary<uint, byte[]>();

            private UiFakeMemory()
            {
                InitialIdentity = new ProcessIdentitySnapshot
                {
                    ProcessId = 4343,
                    ImagePath = San9Pk101Target.ExpectedExecutablePath,
                    CreationFileTimeUtc = 123456789,
                    MainModuleBaseAddress = San9Pk101Target.ExpectedImageBase,
                    MainModuleSize = San9Pk101Target.ExpectedSizeOfImage
                };
                Scan = new UiCandidateScan();
            }

            public int ProcessId { get { return InitialIdentity.ProcessId; } }
            public string ImagePath { get { return InitialIdentity.ImagePath; } }
            public ProcessIdentitySnapshot InitialIdentity { get; private set; }
            internal UiCandidateScan Scan { get; private set; }

            internal static UiFakeMemory Create(bool contradictoryCity)
            {
                UiFakeMemory fake = new UiFakeMemory();
                fake.AddRegion(0x01228340, 0x100);
                fake.AddRegion(San9Pk101MemoryLayout.RawCurrentCityPointerAddress, 0x1000);
                fake.AddRegion(San9Pk101MemoryLayout.PersonBase,
                    San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount);
                fake.AddRegion(San9Pk101UiObservationReader.GlobalSelectedListAddress, 0x10);
                fake.AddRegion(San9Pk101UiObservationReader.TrackedModalCountAddress, 4);
                fake.AddRegion(San9Pk101UiObservationReader.TrackedModalArrayMinusOneAddress + 4, 4);
                fake.AddRegion(HeapBase, 0x0a100);
                fake.AddRegion(0x0060756c, 4);
                fake.AddRegion(0x00610bd4, 4);

                fake.SetUInt32(0x01228344, WindowPointer);
                fake.SetUInt32(WindowPointer + 0x1c, OwnerPointer);
                fake.SetUInt32(OwnerPointer + 0x18, ScenePointer);
                fake.SetUInt32(ScenePointer, 0x00410000);
                fake.SetUInt32(ScenePointer + 0x8c, RootPointer);
                fake.SetUInt32(RootPointer, 0x00607560);
                fake.SetUInt32(RootPointer + 0x0c, ControllerPointer);
                fake.SetUInt32(0x0060756c, 0x00420000);
                fake.SetUInt32(ControllerPointer, 0x00610bc8);
                fake.SetUInt32(ControllerPointer + 0x30, San9Pk101MemoryLayout.ForceBase);
                fake.SetUInt32(ControllerPointer + 0x34, 0x3e9);
                fake.SetUInt32(ControllerPointer + 0x38,
                    San9Pk101MemoryLayout.CityBase
                    + unchecked((uint)((contradictoryCity ? 1 : 0) * San9Pk101MemoryLayout.CityStride)));
                fake.SetUInt32(0x00610bd4, 0x00420010);

                fake.SetUInt32(San9Pk101MemoryLayout.RawCurrentCityPointerAddress, San9Pk101MemoryLayout.CityBase);
                fake.SetUInt32(San9Pk101MemoryLayout.RawContextValue2480Address, 0x13);
                fake.SetUInt32(San9Pk101MemoryLayout.RawPhaseCandidateAddress, 1);
                fake.SetUInt32(San9Pk101MemoryLayout.RawStrategicTimeCandidateAddress, 0x1234);
                fake.SetUInt32(San9Pk101UiObservationReader.TrackedModalCountAddress, 1);
                fake.SetUInt32(San9Pk101UiObservationReader.TrackedModalArrayMinusOneAddress + 4, 0x9002);

                for (int id = 0; id < 5; id++)
                {
                    uint person = San9Pk101MemoryLayout.PersonBase
                        + unchecked((uint)(id * San9Pk101MemoryLayout.PersonStride));
                    fake.SetUInt16(person + San9Pk101MemoryLayout.PersonIdOffset, unchecked((ushort)id));
                    fake.SetUInt32(person + San9Pk101MemoryLayout.PersonIdentityOffset, 3);
                    fake.SetUInt32(person + San9Pk101MemoryLayout.PersonReadyFlagsOffset, 0);
                    fake.SetUInt32(person + San9Pk101MemoryLayout.PersonResidencePointerOffset,
                        San9Pk101MemoryLayout.CityBase
                        + unchecked((uint)San9Pk101MemoryLayout.CityResidentUnitPointerOffset));
                    fake.SetUInt32(person + San9Pk101MemoryLayout.PersonEffectivePoliticsOffset,
                        unchecked((uint)(100 - id)));
                }

                fake.SetUInt32(OuterPointer, 0x0060ccb0);
                fake.SetPersonList(OuterPointer + 0x6cc, SourceNodes, new[] { 0, 1, 2, 3, 4 });
                fake.SetPersonList(OuterPointer + 0x6ec, WorkingNodes, new[] { 0, 2 });
                fake.SetPersonList(OuterPointer + 0x6ac, 0, new int[0]);
                fake.SetPersonList(San9Pk101UiObservationReader.GlobalSelectedListAddress, 0, new int[0]);

                fake.SetUInt32(SelectorPointer, San9Pk101UiObservationReader.SelectorVtable);
                fake.SetUInt32(SelectorPointer + 4, 0x9002);
                fake.SetUInt32(SelectorPointer + 0x158, SelectorRows);
                fake.SetUInt32(SelectorPointer + 0x15c, 6);
                fake.SetUInt32(SelectorPointer + 0x180, 5);
                for (int id = 0; id < 5; id++)
                {
                    uint person = San9Pk101MemoryLayout.PersonBase
                        + unchecked((uint)(id * San9Pk101MemoryLayout.PersonStride));
                    fake.SetUInt32(SelectorRows + unchecked((uint)(id * 8)), person);
                    fake.SetUInt32(SelectorRows + unchecked((uint)(id * 8 + 4)), id == 0 || id == 2 ? 2u : 0u);
                }

                fake.Scan.AddressesByVtable[0x0060ccb0] = new[] { OuterPointer };
                fake.Scan.AddressesByVtable[San9Pk101UiObservationReader.SelectorVtable] = new[] { SelectorPointer };
                return fake;
            }

            public byte[] ReadBytes(long address, int count)
            {
                uint start = checked((uint)address);
                ulong end = unchecked((ulong)start + (uint)count);
                foreach (KeyValuePair<uint, byte[]> pair in regions)
                {
                    ulong regionEnd = unchecked((ulong)pair.Key + (uint)pair.Value.Length);
                    if (start < pair.Key || end > regionEnd) continue;
                    byte[] result = new byte[count];
                    Buffer.BlockCopy(pair.Value, checked((int)(start - pair.Key)), result, 0, count);
                    return result;
                }
                throw new InvalidOperationException(string.Format("Unmapped UI fake read 0x{0:X8}+{1}.", start, count));
            }

            public bool TryCaptureIdentity(out ProcessIdentitySnapshot identity, out string error)
            {
                identity = new ProcessIdentitySnapshot
                {
                    ProcessId = InitialIdentity.ProcessId,
                    ImagePath = InitialIdentity.ImagePath,
                    CreationFileTimeUtc = InitialIdentity.CreationFileTimeUtc,
                    MainModuleBaseAddress = InitialIdentity.MainModuleBaseAddress,
                    MainModuleSize = InitialIdentity.MainModuleSize
                };
                error = null;
                return true;
            }

            private void SetPersonList(uint listAddress, uint nodeBase, int[] ids)
            {
                SetUInt32(listAddress, San9Pk101UiObservationReader.PersonListVtable);
                SetUInt32(listAddress + 4, ids.Length == 0 ? 0 : nodeBase);
                SetUInt32(listAddress + 8, ids.Length == 0 ? 0 : nodeBase + unchecked((uint)((ids.Length - 1) * 0x10)));
                SetUInt32(listAddress + 12, unchecked((uint)ids.Length));
                for (int index = 0; index < ids.Length; index++)
                {
                    uint node = nodeBase + unchecked((uint)(index * 0x10));
                    SetUInt32(node, index + 1 == ids.Length ? 0 : node + 0x10);
                    SetUInt32(node + 4, index == 0 ? 0 : node - 0x10);
                    SetUInt32(node + 8, San9Pk101MemoryLayout.PersonBase
                        + unchecked((uint)(ids[index] * San9Pk101MemoryLayout.PersonStride)));
                }
            }

            private void AddRegion(uint address, int count)
            {
                regions.Add(address, new byte[count]);
            }

            private void SetUInt16(uint address, ushort value)
            {
                SetBytes(address, BitConverter.GetBytes(value));
            }

            private void SetUInt32(uint address, uint value)
            {
                SetBytes(address, BitConverter.GetBytes(value));
            }

            private void SetBytes(uint address, byte[] value)
            {
                foreach (KeyValuePair<uint, byte[]> pair in regions)
                {
                    ulong end = unchecked((ulong)address + (uint)value.Length);
                    if (address < pair.Key || end > unchecked((ulong)pair.Key + (uint)pair.Value.Length)) continue;
                    Buffer.BlockCopy(value, 0, pair.Value, checked((int)(address - pair.Key)), value.Length);
                    return;
                }
                throw new InvalidOperationException(string.Format("Unmapped UI fake write 0x{0:X8}+{1}.", address, value.Length));
            }
        }

        private sealed class FakeMemory : IReadOnlyProcessMemory
        {
            private const uint RegionBase = San9Pk101MemoryLayout.RawCurrentCityPointerAddress;
            private readonly byte[] raw;
            private readonly Dictionary<uint, byte[]> nodes = new Dictionary<uint, byte[]>();
            private readonly Dictionary<uint, byte[]> code = new Dictionary<uint, byte[]>();
            private int rawContextReadCount;
            private int firstCodeReadCount;
            internal bool AlternateRawContext;
            internal bool AlternateFirstCodeAnchor;

            private FakeMemory()
            {
                uint end = checked(San9Pk101MemoryLayout.PersonBase
                    + unchecked((uint)(San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount)));
                raw = new byte[checked((int)(end - RegionBase))];
                InitialIdentity = new ProcessIdentitySnapshot
                {
                    ProcessId = 4242,
                    ImagePath = San9Pk101Target.ExpectedExecutablePath,
                    CreationFileTimeUtc = DateTime.UtcNow.ToFileTimeUtc(),
                    MainModuleBaseAddress = San9Pk101Target.ExpectedImageBase,
                    MainModuleSize = San9Pk101Target.ExpectedSizeOfImage
                };
                foreach (CodeAnchorDefinition definition in San9Pk101CodeAnchorValidator.Definitions)
                    code.Add(definition.Address, San9Pk101CodeAnchorValidator.ReadExpectedDiskBytes(definition));
            }

            public int ProcessId { get { return InitialIdentity.ProcessId; } }
            public string ImagePath { get { return InitialIdentity.ImagePath; } }
            public ProcessIdentitySnapshot InitialIdentity { get; private set; }

            internal static FakeMemory Create(int residentCount, int money)
            {
                FakeMemory fake = new FakeMemory();
                fake.SetUInt32(San9Pk101MemoryLayout.RawCurrentCityPointerAddress, San9Pk101MemoryLayout.CityBase);
                fake.SetUInt32(San9Pk101MemoryLayout.RawContextValue2480Address, 0x13);
                fake.SetUInt32(San9Pk101MemoryLayout.RawPhaseCandidateAddress, 1);
                fake.SetUInt32(San9Pk101MemoryLayout.RawStrategicTimeCandidateAddress, 0x1234);
                for (int cityId = 0; cityId < San9Pk101MemoryLayout.CityCount; cityId++)
                {
                    uint city = San9Pk101MemoryLayout.CityBase + unchecked((uint)(cityId * San9Pk101MemoryLayout.CityStride));
                    fake.SetUInt32(city, San9Pk101MemoryLayout.ExpectedCityVtable);
                    fake.SetByte(city + San9Pk101MemoryLayout.CityTypeOffset, San9Pk101MemoryLayout.CityTypeValue);
                    fake.SetAscii(city + San9Pk101MemoryLayout.CityNameOffset, "C" + cityId);
                    fake.SetUInt32(city + San9Pk101MemoryLayout.CitySelfPointerOffset, city);
                }
                for (int id = 0; id < San9Pk101MemoryLayout.PersonCount; id++)
                {
                    uint person = San9Pk101MemoryLayout.PersonBase + unchecked((uint)(id * San9Pk101MemoryLayout.PersonStride));
                    fake.SetUInt16(person + San9Pk101MemoryLayout.PersonIdOffset, unchecked((ushort)id));
                    fake.SetUInt32(person + San9Pk101MemoryLayout.PersonIdentityOffset, uint.MaxValue);
                }

                fake.SetUInt32(San9Pk101MemoryLayout.ForceBase + San9Pk101MemoryLayout.ForceMoneyOffset, unchecked((uint)money));
                fake.SetUInt32(San9Pk101MemoryLayout.ForceBase + San9Pk101MemoryLayout.ForceFlagsOffset, 3);
                fake.SetUInt32(San9Pk101MemoryLayout.ForceBase + San9Pk101MemoryLayout.ForceMainForcePointerOffset, San9Pk101MemoryLayout.ForceBase);
                fake.SetUInt32(San9Pk101MemoryLayout.ForceBase + San9Pk101MemoryLayout.ForceLeaderPointerOffset, San9Pk101MemoryLayout.PersonBase);

                uint city0 = San9Pk101MemoryLayout.CityBase;
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityCorpsPointerOffset, San9Pk101MemoryLayout.ForceBase);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityResidentUnitPointerOffset, San9Pk101MemoryLayout.ExpectedEmbeddedCityVtable);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityEmbeddedFlagsOffset, 1);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityPatrolCurrentOffset, 500);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityCommerceCurrentOffset, 200);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityCommerceMaximumOffset, 600);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityCultivateCurrentOffset, 200);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityCultivateMaximumOffset, 600);
                fake.SetUInt16(city0 + San9Pk101MemoryLayout.CityRepairCurrentOffset, 200);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityRepairMaximumOffset, 600);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityTroopsOffset, 10000);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityMoraleOffset, 80);

                const uint firstNode = 0x02000000;
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityResidentListFirstPointerOffset,
                    residentCount == 0 ? 0 : firstNode);
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityResidentListLastPointerOffset,
                    residentCount == 0 ? 0 : firstNode + unchecked((uint)((residentCount - 1) * 0x20)));
                fake.SetUInt32(city0 + San9Pk101MemoryLayout.CityValidResidentOfficerCountOffset, unchecked((uint)residentCount));
                for (int id = 0; id < residentCount; id++)
                {
                    uint person = San9Pk101MemoryLayout.PersonBase + unchecked((uint)(id * San9Pk101MemoryLayout.PersonStride));
                    uint nodeAddress = firstNode + unchecked((uint)(id * 0x20));
                    uint previous = id == 0 ? 0 : nodeAddress - 0x20;
                    uint next = id + 1 == residentCount ? 0 : nodeAddress + 0x20;
                    byte[] node = new byte[San9Pk101MemoryLayout.ResidentNodeSize];
                    WriteUInt32(node, 0, next); WriteUInt32(node, 4, previous); WriteUInt32(node, 8, person);
                    fake.nodes.Add(nodeAddress, node);
                    fake.SetAscii(person + San9Pk101MemoryLayout.PersonSurnameOffset, "P" + id);
                    fake.SetUInt32(person + San9Pk101MemoryLayout.PersonIdentityOffset, 3);
                    fake.SetUInt32(person + San9Pk101MemoryLayout.PersonResidencePointerOffset,
                        city0 + San9Pk101MemoryLayout.CityResidentUnitPointerOffset);
                    fake.SetPersonAbility(id, San9Pk101MemoryLayout.PersonEffectiveMightOffset, unchecked((uint)(50 + id)));
                    fake.SetPersonAbility(id, San9Pk101MemoryLayout.PersonEffectiveIntelligenceOffset, unchecked((uint)(80 - id)));
                    fake.SetPersonAbility(id, San9Pk101MemoryLayout.PersonEffectivePoliticsOffset, unchecked((uint)(60 + id)));
                    fake.SetPersonAbility(id, San9Pk101MemoryLayout.PersonEffectiveLeadershipOffset, unchecked((uint)(70 + id)));
                }
                return fake;
            }

            public byte[] ReadBytes(long address, int count)
            {
                uint start = checked((uint)address);
                uint rawEnd = unchecked(RegionBase + (uint)raw.Length);
                if (start >= RegionBase && unchecked((ulong)start + (uint)count) <= rawEnd)
                {
                    byte[] result = new byte[count];
                    Buffer.BlockCopy(raw, checked((int)(start - RegionBase)), result, 0, count);
                    if (start == RegionBase && count == raw.Length && AlternateRawContext)
                    {
                        if ((rawContextReadCount++ & 1) != 0) result[12] ^= 1;
                    }
                    return result;
                }
                byte[] value;
                if (nodes.TryGetValue(start, out value) && value.Length == count) return (byte[])value.Clone();
                if (code.TryGetValue(start, out value) && value.Length == count)
                {
                    byte[] result = (byte[])value.Clone();
                    if (AlternateFirstCodeAnchor && start == San9Pk101CodeAnchorValidator.Definitions[0].Address
                        && (firstCodeReadCount++ & 1) != 0) result[0] ^= 1;
                    return result;
                }
                throw new InvalidOperationException(string.Format("Unmapped fake read 0x{0:X8}+{1}.", start, count));
            }

            public bool TryCaptureIdentity(out ProcessIdentitySnapshot identity, out string error)
            {
                identity = new ProcessIdentitySnapshot
                {
                    ProcessId = InitialIdentity.ProcessId, ImagePath = InitialIdentity.ImagePath,
                    CreationFileTimeUtc = InitialIdentity.CreationFileTimeUtc,
                    MainModuleBaseAddress = InitialIdentity.MainModuleBaseAddress,
                    MainModuleSize = InitialIdentity.MainModuleSize
                };
                error = null; return true;
            }

            internal void SetPersonAbility(int id, int offset, uint value)
            {
                SetUInt32(San9Pk101MemoryLayout.PersonBase + unchecked((uint)(id * San9Pk101MemoryLayout.PersonStride + offset)), value);
            }
            internal void SetByte(uint address, byte value) { raw[checked((int)(address - RegionBase))] = value; }
            internal void SetUInt16(uint address, ushort value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, raw, checked((int)(address - RegionBase)), 2); }
            internal void SetUInt32(uint address, uint value) { WriteUInt32(raw, checked((int)(address - RegionBase)), value); }
            internal void SetAscii(uint address, string value) { byte[] b = Encoding.ASCII.GetBytes(value); Buffer.BlockCopy(b, 0, raw, checked((int)(address - RegionBase)), b.Length); }
            private static void WriteUInt32(byte[] bytes, int offset, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4); }
        }
    }
}
