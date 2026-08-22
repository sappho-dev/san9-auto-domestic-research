using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;
using San9AutoDomestic.Core.Execution;
using San9AutoDomestic.Core.Planning;

namespace San9AutoDomestic.Core.Tests
{
    internal static class CoreTests
    {
        public static IList<TestCase> All()
        {
            return new List<TestCase>
            {
                new TestCase("Default configuration compiles two ordered profiles", DefaultConfigurationCompiles),
                new TestCase("Unknown command is rejected with exact field path", UnknownCommandIsRejected),
                new TestCase("Strict loader rejects unknown fields and non-integers", StrictTypesAndFieldsAreRejected),
                new TestCase("Value ranges and duplicate profile ids are rejected", RangesAndDuplicatesAreRejected),
                new TestCase("Unknown schema version is rejected", UnknownSchemaIsRejected),
                new TestCase("Execution plan is frozen and resolves reserve override", ExecutionPlanIsFrozen),
                new TestCase("BASIC with ten officers uses two disjoint best-five groups", BasicTenOfficers),
                new TestCase("BASIC with seven officers skips cultivate only", BasicSevenOfficers),
                new TestCase("Grey commerce skips without consuming cultivate officers", GreyCommerceContinues),
                new TestCase("FULL with twenty officers plans first four and skips repair", FullTwentyOfficers),
                new TestCase("FULL continues after a grey middle task", FullContinuesAfterGreyTask),
                new TestCase("Two hundred money cannot commit exact five", TwoHundredMoneySkips),
                new TestCase("Reserve money prevents projected balance breach", ReserveMoneySkips),
                new TestCase("Zero-cost training does not require corps money", ZeroCostTrainingDoesNotRequireMoney),
                new TestCase("Delegated city is excluded from the queue", DelegatedCityIsExcluded),
                new TestCase("Gate and port are excluded from the queue", NonCitiesAreExcluded),
                new TestCase("Control change after queue capture skips all city tasks", ControlChangeSkipsCity),
                new TestCase("Already executed command skips while later task continues", AlreadyExecutedSupportsRestart),
                new TestCase("City id order deterministically allocates shared corps money", CityOrderControlsMoney),
                new TestCase("Verified stat fallback preserves source order on ties", VerifiedFallbackSorts),
                new TestCase("Fallback plans are preview-only and cannot enter submission mode", FallbackSubmissionIsFatal),
                new TestCase("Full-batch submission requires stepwise revalidation", BatchSubmissionIsFatal),
                new TestCase("Unknown planning mode aborts without producing work", UnknownPlanningModeIsFatal),
                new TestCase("Native-best aborts on unverified native ranking", NativeBestRequiresVerification),
                new TestCase("Non-exact task selects all available up to max", NonExactTaskUsesAvailableOfficers),
                new TestCase("Duplicate planned command is not submitted twice", DuplicateCommandIsNotPlannedTwice),
                new TestCase("Missing corps money marks planning snapshot incomplete", MissingCorpsMoneyIsFatal),
                new TestCase("Structure-only snapshot can be created but planning aborts", StructureOnlySnapshotIsFatalForPlanning),
                new TestCase("Missing command data aborts and stops later cities", MissingCommandSnapshotStopsBatch),
                new TestCase("Batch context binds process, game, profile, and fingerprint", BatchContextBindsIdentity),
                new TestCase("Changed batch identity fields abort planning", ChangedBatchContextIsFatal),
                new TestCase("Stable unverified game tokens remain previewable", StableUnverifiedTokensCanPreview),
                new TestCase("Native command and candidate invariants reject corrupt data", SnapshotInvariantsRejectCorruption),
                new TestCase("Unclassified native block reason aborts", OtherNativeBlockIsFatal),
                new TestCase("Exact-count configuration requires equal min and max", ExactCountRequiresEqualBounds),
                new TestCase("Disabled profiles and tasks do not enter frozen plans", DisabledConfigurationIsOmitted),
                new TestCase("Process-wide batch coordinator uses generation-owned leases", BatchRunGateUsesOwnedLeases),
                new TestCase("Planning result money map has no mutation interface", MoneyMapIsReadOnly),
                new TestCase("Public APIs cannot assert verified provenance", PublicVerificationCannotBeAsserted),
                new TestCase("Trusted command rankings bind to one observation", TrustedObservationBindingIsEnforced),
                new TestCase("Command reread challenge binds batch city command and generation", CommandObservationChallengeIsBound),
                new TestCase("Planning-ready cross-field contradictions abort", PlanningReadyContradictionsAbort),
                new TestCase("Queued facility identity changes abort", FacilityIdentityChangeIsFatal),
                new TestCase("Aborted batches discard earlier projections", AbortedBatchDiscardsProjections)
            };
        }

        private static void DefaultConfigurationCompiles()
        {
            string path = FindRepositoryFile(Path.Combine("config", "default.json"));
            DomesticConfiguration configuration = new ConfigurationLoader().LoadFile(path);
            ExecutionPlanCatalog catalog = new ExecutionPlanCompiler().Compile(configuration);

            AssertEx.Equal(2, catalog.Plans.Count, "Default enabled profile count is wrong.");
            AssertEx.SequenceEqual(
                new[] { DomesticCommand.Commerce, DomesticCommand.Cultivate },
                catalog.GetRequired("basic").Tasks.Select(item => item.Command),
                "BASIC task order is wrong.");
            AssertEx.SequenceEqual(
                new[]
                {
                    DomesticCommand.Patrol,
                    DomesticCommand.Commerce,
                    DomesticCommand.Cultivate,
                    DomesticCommand.Train,
                    DomesticCommand.Repair
                },
                catalog.GetRequired("wealthy").Tasks.Select(item => item.Command),
                "FULL task order is wrong.");
            AssertEx.True(configuration.DryRunByDefault, "Default configuration should start in dry-run mode.");
            AssertEx.True(catalog.GetRequired("basic").Tasks.All(item => item.MaxOfficers == 5), "Defaults must use five officers.");
        }

        private static void UnknownCommandIsRejected()
        {
            string json = MinimalJson("bogus", 5, 5, "native_best", 0, "basic");
            ConfigurationValidationException exception = AssertEx.Throws<ConfigurationValidationException>(
                delegate { new ConfigurationLoader().LoadJson(json); },
                "Unknown command must be rejected.");
            AssertIssue(exception, "$.profiles[0].tasks[0].command", "unknown command");
        }

        private static void StrictTypesAndFieldsAreRejected()
        {
            string json = @"{
              ""schemaVersion"": 1,
              ""profiles"": [{
                ""id"": ""basic"", ""displayName"": ""基础"", ""enabled"": true,
                ""unexpected"": 1,
                ""tasks"": [{
                  ""command"": ""commerce"", ""enabled"": true,
                  ""minOfficers"": 5.0, ""maxOfficers"": 5,
                  ""requireExactCount"": true, ""selectionPolicy"": ""native_best""
                }]
              }],
              ""cityScope"": ""direct_cities"", ""cityOrder"": ""game_id_asc"",
              ""reserveMoney"": 0, ""dryRunByDefault"": true
            }";
            ConfigurationValidationException exception = AssertEx.Throws<ConfigurationValidationException>(
                delegate { new ConfigurationLoader().LoadJson(json); },
                "Unknown fields and floating-point integer fields must fail.");
            AssertIssue(exception, "$.profiles[0].unexpected", "unknown field");
            AssertIssue(exception, "$.profiles[0].tasks[0].minOfficers", "must be an integer");
        }

        private static void RangesAndDuplicatesAreRejected()
        {
            string first = ProfileJson("same", "commerce", 0, 6, "native_best");
            string second = ProfileJson("same", "cultivate", 5, 4, "native_best");
            string json = "{\"schemaVersion\":1,\"profiles\":[" + first + "," + second
                + "],\"cityScope\":\"direct_cities\",\"cityOrder\":\"game_id_asc\","
                + "\"reserveMoney\":-1,\"dryRunByDefault\":true}";
            ConfigurationValidationException exception = AssertEx.Throws<ConfigurationValidationException>(
                delegate { new ConfigurationLoader().LoadJson(json); },
                "Invalid ranges and duplicates must fail.");
            AssertIssue(exception, "$.reserveMoney", "between 0");
            AssertIssue(exception, "$.profiles[0].tasks[0].minOfficers", "between 1 and 5");
            AssertIssue(exception, "$.profiles[0].tasks[0].maxOfficers", "between 1 and 5");
            AssertIssue(exception, "$.profiles[1].id", "duplicates");
            AssertIssue(exception, "$.profiles[1].tasks[0].maxOfficers", "less than minOfficers");
        }

        private static void UnknownSchemaIsRejected()
        {
            string json = MinimalJson("commerce", 5, 5, "native_best", 0, "basic")
                .Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
            ConfigurationValidationException exception = AssertEx.Throws<ConfigurationValidationException>(
                delegate { new ConfigurationLoader().LoadJson(json); },
                "Unknown schema must fail.");
            AssertIssue(exception, "$.schemaVersion", "unsupported schema version");
        }

        private static void ExecutionPlanIsFrozen()
        {
            List<TaskConfiguration> sourceTasks = new List<TaskConfiguration>
            {
                new TaskConfiguration(
                    DomesticCommand.Commerce,
                    true,
                    5,
                    5,
                    true,
                    SelectionPolicy.NativeBest,
                    123)
            };
            ProfileConfiguration profile = new ProfileConfiguration("frozen", "冻结", true, sourceTasks);
            List<ProfileConfiguration> sourceProfiles = new List<ProfileConfiguration> { profile };
            DomesticConfiguration configuration = new DomesticConfiguration(
                1,
                sourceProfiles,
                CityScope.DirectCities,
                CityOrder.GameIdAscending,
                999,
                true);
            ExecutionPlan plan = new ExecutionPlanCompiler().Compile(configuration).GetRequired("frozen");

            sourceTasks.Clear();
            sourceProfiles.Clear();
            AssertEx.Equal(1, plan.Tasks.Count, "Compiled plan changed after source list mutation.");
            AssertEx.Equal(123, plan.Tasks[0].ReserveMoney, "Task reserve override was not frozen.");
        }

        private static void BasicTenOfficers()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot city = CreateFacility(1, FacilityType.City, true, 1, 1, 10, null, true, null);
            PlanningResult result = Build(plan, 500, city);
            CityPlanningResult cityResult = result.Cities[0];

            AssertPlanned(cityResult.Tasks[0], new[] { 1, 2, 3, 4, 5 });
            AssertPlanned(cityResult.Tasks[1], new[] { 6, 7, 8, 9, 10 });
            AssertEx.Equal(0, result.RemainingMoneyByCorps[1], "Two five-person commands should cost 500.");
        }

        private static void BasicSevenOfficers()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot city = CreateFacility(1, FacilityType.City, true, 1, 1, 7, null, true, null);
            PlanningResult result = Build(plan, 1000, city);

            AssertPlanned(result.Cities[0].Tasks[0], new[] { 1, 2, 3, 4, 5 });
            AssertSkipped(result.Cities[0].Tasks[1], PlanningSkipReason.InsufficientOfficers);
            AssertEx.Equal(2, result.Cities[0].Tasks[1].CandidateCount, "Two officers should remain.");
        }

        private static void GreyCommerceContinues()
        {
            IDictionary<DomesticCommand, NativeCommandBlockReason> blocked =
                new Dictionary<DomesticCommand, NativeCommandBlockReason>
                {
                    { DomesticCommand.Commerce, NativeCommandBlockReason.GreyedOut }
                };
            PlanningResult result = Build(
                DefaultPlan("basic"),
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 5, blocked, true, null));

            AssertSkipped(result.Cities[0].Tasks[0], PlanningSkipReason.NativeGreyedOut);
            AssertPlanned(result.Cities[0].Tasks[1], new[] { 1, 2, 3, 4, 5 });
        }

        private static void FullTwentyOfficers()
        {
            PlanningResult result = Build(
                DefaultPlan("wealthy"),
                2000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 20, null, true, null));
            CityPlanningResult city = result.Cities[0];

            for (int index = 0; index < 4; index++)
            {
                AssertEx.Equal(PlanningDecision.Projected, city.Tasks[index].Decision, "One of the first four tasks was not projected.");
            }

            AssertSkipped(city.Tasks[4], PlanningSkipReason.InsufficientOfficers);
            AssertEx.Equal(1250, result.RemainingMoneyByCorps[1], "Three charged commands and zero-cost training should consume 750 money.");
            AssertEx.Equal(20, city.Tasks.Take(4).SelectMany(item => item.SelectedOfficerIds).Distinct().Count(), "Officers were reused.");
        }

        private static void FullContinuesAfterGreyTask()
        {
            IDictionary<DomesticCommand, NativeCommandBlockReason> blocked =
                new Dictionary<DomesticCommand, NativeCommandBlockReason>
                {
                    { DomesticCommand.Cultivate, NativeCommandBlockReason.GreyedOut }
                };
            PlanningResult result = Build(
                DefaultPlan("wealthy"),
                2000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 20, blocked, true, null));
            CityPlanningResult city = result.Cities[0];

            AssertSkipped(city.Tasks[2], PlanningSkipReason.NativeGreyedOut);
            AssertEx.Equal(PlanningDecision.Projected, city.Tasks[3].Decision, "Train should continue after grey cultivate.");
            AssertEx.Equal(PlanningDecision.Projected, city.Tasks[4].Decision, "Repair should use preserved officers.");
            AssertEx.Equal(4, city.Tasks.Count(item => item.Decision == PlanningDecision.Projected), "Exactly four tasks should be projected.");
        }

        private static void TwoHundredMoneySkips()
        {
            PlanningResult result = Build(
                DefaultPlan("basic"),
                200,
                CreateFacility(1, FacilityType.City, true, 1, 1, 5, null, true, null));

            AssertSkipped(result.Cities[0].Tasks[0], PlanningSkipReason.InsufficientMoney);
            AssertSkipped(result.Cities[0].Tasks[1], PlanningSkipReason.InsufficientMoney);
            AssertEx.Equal(5, result.Cities[0].Tasks[1].CandidateCount, "Money skip must not consume officers.");
            AssertEx.Equal(200, result.RemainingMoneyByCorps[1], "Skipped tasks must not consume money.");
        }

        private static void ReserveMoneySkips()
        {
            ExecutionPlan plan = CreatePlan(
                "reserve",
                new[] { DomesticCommand.Commerce },
                2000,
                SelectionPolicy.NativeBest,
                5,
                5,
                true,
                null);
            PlanningResult result = Build(
                plan,
                2200,
                CreateFacility(1, FacilityType.City, true, 1, 1, 5, null, true, null));

            AssertSkipped(result.Cities[0].Tasks[0], PlanningSkipReason.ReserveMoneyProtected);
            AssertEx.Equal(250, result.Cities[0].Tasks[0].EstimatedCost, "Projected cost should remain visible on reserve skip.");
            AssertEx.Equal(2200, result.RemainingMoneyByCorps[1], "Reserve skip must not consume money.");
        }

        private static void ZeroCostTrainingDoesNotRequireMoney()
        {
            ExecutionPlan plan = CreatePlan(
                "training-no-fee",
                new[] { DomesticCommand.Train },
                0,
                SelectionPolicy.NativeBest,
                5,
                5,
                true,
                null);
            PlanningResult result = Build(
                plan,
                0,
                CreateFacility(1, FacilityType.City, true, 1, 1, 5, null, true, null));

            AssertPlanned(result.Cities[0].Tasks[0], new[] { 1, 2, 3, 4, 5 });
            AssertEx.Equal(0, result.Cities[0].Tasks[0].EstimatedCost, "Training should have zero estimated cost on this exact target.");
            AssertEx.Equal(0, result.RemainingMoneyByCorps[1], "Zero-cost training must not alter corps money.");
        }

        private static void DelegatedCityIsExcluded()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot delegated = CreateFacility(1, FacilityType.City, false, 1, 1, 5, null, true, null);
            FacilitySnapshot direct = CreateFacility(2, FacilityType.City, true, 1, 100, 5, null, true, null);
            PlanningResult result = Build(plan, 1000, delegated, direct);

            AssertEx.Equal(1, result.Cities.Count, "Only the direct city should enter the queue.");
            AssertEx.Equal(2, result.Cities[0].CityId, "Wrong city entered the queue.");
            AssertEx.True(result.ScopeSkips.Any(item => item.FacilityId == 1 && item.Reason == ScopeSkipReason.DelegatedCity), "Delegated skip is missing.");
        }

        private static void NonCitiesAreExcluded()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot city = CreateFacility(3, FacilityType.City, true, 1, 200, 5, null, true, null);
            FacilitySnapshot gate = CreateFacility(1, FacilityType.Gate, true, 1, 1, 5, null, true, null);
            FacilitySnapshot port = CreateFacility(2, FacilityType.Port, true, 1, 100, 5, null, true, null);
            PlanningResult result = Build(plan, 1000, city, gate, port);

            AssertEx.SequenceEqual(new[] { 3 }, result.Cities.Select(item => item.CityId), "Non-cities entered the city plan.");
            AssertEx.Equal(2, result.ScopeSkips.Count(item => item.Reason == ScopeSkipReason.NonCityFacility), "Expected gate and port skips.");
        }

        private static void ControlChangeSkipsCity()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            GameSnapshot initial = Snapshot(1000, CreateFacility(1, FacilityType.City, true, 1, 1, 10, null, true, null));
            CityQueueSnapshot queue = CityQueueSnapshot.Capture(plan, initial);
            GameSnapshot changed = Snapshot(1000, CreateFacility(1, FacilityType.City, false, 1, 1, 10, null, true, null));
            PlanningResult result = new DomesticPlanner().Build(plan, queue, changed);

            AssertEx.Equal(2, result.Cities[0].Tasks.Count, "All frozen tasks should receive a control-change result.");
            AssertEx.True(result.Cities[0].Tasks.All(item => item.SkipReason == PlanningSkipReason.CityControlChanged), "Control change reason is not explicit.");
            AssertEx.Equal(1000, result.RemainingMoneyByCorps[1], "Changed city must not consume money.");
        }

        private static void AlreadyExecutedSupportsRestart()
        {
            IDictionary<DomesticCommand, NativeCommandBlockReason> blocked =
                new Dictionary<DomesticCommand, NativeCommandBlockReason>
                {
                    { DomesticCommand.Commerce, NativeCommandBlockReason.AlreadyExecuted }
                };
            PlanningResult result = Build(
                DefaultPlan("basic"),
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 5, blocked, true, null));

            AssertSkipped(result.Cities[0].Tasks[0], PlanningSkipReason.AlreadyExecuted);
            AssertEx.Equal(PlanningDecision.Projected, result.Cities[0].Tasks[1].Decision, "Unfinished task should still project on rerun.");
        }

        private static void CityOrderControlsMoney()
        {
            ExecutionPlan plan = CreatePlan(
                "one",
                new[] { DomesticCommand.Commerce },
                0,
                SelectionPolicy.NativeBest,
                5,
                5,
                true,
                null);
            FacilitySnapshot laterId = CreateFacility(20, FacilityType.City, true, 1, 100, 5, null, true, null);
            FacilitySnapshot earlierId = CreateFacility(10, FacilityType.City, true, 1, 1, 5, null, true, null);
            PlanningResult result = Build(plan, 250, laterId, earlierId);

            AssertEx.SequenceEqual(new[] { 10, 20 }, result.Cities.Select(item => item.CityId), "Cities are not sorted by game id.");
            AssertEx.Equal(PlanningDecision.Projected, result.Cities[0].Tasks[0].Decision, "Earlier city should receive shared money first.");
            AssertSkipped(result.Cities[1].Tasks[0], PlanningSkipReason.InsufficientMoney);
        }

        private static void VerifiedFallbackSorts()
        {
            ExecutionPlan plan = CreatePlan(
                "fallback",
                new[] { DomesticCommand.Patrol },
                0,
                SelectionPolicy.VerifiedStatFallback,
                5,
                5,
                true,
                null);
            List<OfficerSnapshot> officers = new List<OfficerSnapshot>
            {
                new OfficerSnapshot(11, "11", true, 1, 1, 20, 1),
                new OfficerSnapshot(12, "12", true, 1, 1, 90, 1),
                new OfficerSnapshot(13, "13", true, 1, 1, 90, 1),
                new OfficerSnapshot(14, "14", true, 1, 1, 80, 1),
                new OfficerSnapshot(15, "15", true, 1, 1, 70, 1),
                new OfficerSnapshot(16, "16", true, 1, 1, 60, 1)
            };
            IDictionary<DomesticCommand, IList<int>> rankings = new Dictionary<DomesticCommand, IList<int>>
            {
                { DomesticCommand.Patrol, new[] { 11, 16, 15, 14, 13, 12 } }
            };
            FacilitySnapshot city = CreateFacilityWithOfficers(1, true, 1, officers, null, false, rankings);
            PlanningResult result = Build(plan, 1000, city);

            AssertEx.True(plan.PreviewOnly, "Fallback plan must be marked preview-only.");
            AssertEx.False(plan.EligibleForStepwiseValidation, "Fallback plan must never enter stepwise validation.");
            AssertEx.False(result.CommitAuthorized, "Preview output must never authorize a commit.");
            AssertPlanned(result.Cities[0].Tasks[0], new[] { 13, 12, 14, 15, 16 });
        }

        private static void FallbackSubmissionIsFatal()
        {
            ExecutionPlan plan = CreatePlan(
                "fallback-submit",
                new[] { DomesticCommand.Patrol },
                0,
                SelectionPolicy.VerifiedStatFallback,
                5,
                5,
                true,
                null);
            GameSnapshot snapshot = Snapshot(
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 5, null, false, null));

            PlanningResult result = new DomesticPlanner().Build(plan, snapshot, PlanningMode.Submission);

            AssertFatal(result, PlanningFatalReason.PreviewOnlyPlan);
            AssertEx.False(result.CommitAuthorized, "Fatal fallback result cannot authorize a commit.");
        }

        private static void BatchSubmissionIsFatal()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            PlanningResult result = new DomesticPlanner().Build(
                plan,
                Snapshot(1000, CreateFacility(1, FacilityType.City, true, 1, 1, 10, null, true, null)),
                PlanningMode.Submission);

            AssertEx.True(plan.EligibleForStepwiseValidation, "Native-only plan should be eligible for future stepwise validation.");
            AssertFatal(result, PlanningFatalReason.StepwiseSubmissionRequired);
            AssertEx.Equal(0, result.ProjectedTaskCount, "Batch submission mode must not produce a commit schedule.");
        }

        private static void UnknownPlanningModeIsFatal()
        {
            PlanningResult result = new DomesticPlanner().Build(
                DefaultPlan("basic"),
                Snapshot(1000, CreateFacility(1, FacilityType.City, true, 1, 1, 10, null, true, null)),
                (PlanningMode)999);

            AssertFatal(result, PlanningFatalReason.InvalidPlanningMode);
            AssertEx.Equal(0, result.Cities.Count, "Invalid mode must not produce city work.");
        }

        private static void NativeBestRequiresVerification()
        {
            ExecutionPlan plan = CreatePlan(
                "native",
                new[] { DomesticCommand.Commerce },
                0,
                SelectionPolicy.NativeBest,
                5,
                5,
                true,
                null);
            PlanningResult result = Build(
                plan,
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 5, null, false, null));

            AssertFatal(result, PlanningFatalReason.NativeSelectionUnavailable);
        }

        private static void NonExactTaskUsesAvailableOfficers()
        {
            ExecutionPlan plan = CreatePlan(
                "flex",
                new[] { DomesticCommand.Commerce },
                0,
                SelectionPolicy.NativeBest,
                2,
                5,
                false,
                null);
            PlanningResult result = Build(
                plan,
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 3, null, true, null));

            AssertPlanned(result.Cities[0].Tasks[0], new[] { 1, 2, 3 });
            AssertEx.Equal(150, result.Cities[0].Tasks[0].EstimatedCost, "Flexible task cost is wrong.");
        }

        private static void DuplicateCommandIsNotPlannedTwice()
        {
            ExecutionPlan plan = CreatePlan(
                "duplicate",
                new[] { DomesticCommand.Commerce, DomesticCommand.Commerce },
                0,
                SelectionPolicy.NativeBest,
                5,
                5,
                true,
                null);
            PlanningResult result = Build(
                plan,
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 10, null, true, null));

            AssertEx.Equal(PlanningDecision.Projected, result.Cities[0].Tasks[0].Decision, "First duplicate should project.");
            AssertSkipped(result.Cities[0].Tasks[1], PlanningSkipReason.AlreadyPlannedInThisBatch);
        }

        private static void MissingCorpsMoneyIsFatal()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot city = CreateFacility(1, FacilityType.City, true, 9, 1, 5, null, true, null);
            GameSnapshot snapshot = new GameSnapshot(
                1,
                new[] { city },
                new CorpsMoneySnapshot[0],
                PlanningContext(1));
            PlanningResult result = new DomesticPlanner().Build(plan, snapshot);

            AssertFatal(result, PlanningFatalReason.SnapshotIncomplete);
            AssertEx.Equal(0, result.Cities.Count, "Snapshot completeness must fail before city planning starts.");
        }

        private static void StructureOnlySnapshotIsFatalForPlanning()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot structureCity = new FacilitySnapshot(
                1,
                "StructureOnly",
                FacilityType.City,
                1,
                1,
                true,
                new OfficerSnapshot[0],
                new NativeCommandSnapshot[0]);
            GameSnapshot snapshot = new GameSnapshot(
                1,
                new[] { structureCity },
                new CorpsMoneySnapshot[0]);

            AssertEx.Equal(
                SnapshotReadiness.StructureOnly,
                snapshot.Context.Readiness,
                "Legacy/read-only snapshot must be explicitly structure-only.");
            PlanningResult result = new DomesticPlanner().Build(plan, snapshot);
            AssertFatal(result, PlanningFatalReason.SnapshotIncomplete);
        }

        private static void MissingCommandSnapshotStopsBatch()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot incomplete = new FacilitySnapshot(
                1,
                "Incomplete",
                FacilityType.City,
                1,
                1,
                true,
                CreateOfficers(1, 10),
                new NativeCommandSnapshot[0]);
            FacilitySnapshot later = CreateFacility(
                2,
                FacilityType.City,
                true,
                2,
                100,
                10,
                null,
                true,
                null);

            PlanningResult result = Build(plan, 1000, incomplete, later);

            AssertFatal(result, PlanningFatalReason.MissingCommandSnapshot);
            AssertEx.Equal(1, result.Cities.Count, "Planner must stop before reaching a later city.");
            AssertEx.Equal(1, result.FatalIssue.CityId.Value, "Fatal city id is wrong.");
            AssertEx.Equal(DomesticCommand.Commerce, result.FatalIssue.Command.Value, "Fatal command is wrong.");
        }

        private static void BatchContextBindsIdentity()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            GameSnapshot snapshot = Snapshot(
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 10, null, true, null));
            CityQueueSnapshot queue = CityQueueSnapshot.Capture(plan, snapshot);

            AssertEx.True(!string.IsNullOrWhiteSpace(queue.Context.BatchId), "Batch id is missing.");
            AssertEx.Equal(plan.ProfileId, queue.Context.ProfileId, "Profile binding is wrong.");
            AssertEx.Equal(
                plan.ConfigurationFingerprint,
                queue.Context.ConfigurationFingerprint,
                "Configuration fingerprint binding is wrong.");
            AssertEx.Equal(64, plan.ConfigurationFingerprint.Length, "Fingerprint must be SHA-256 hex.");
            AssertEx.Equal(24680, queue.Context.ProcessId.Value, "PID binding is wrong.");
            AssertEx.Equal(638900000000000000L, queue.Context.ProcessStartUtcTicks.Value, "Start-time binding is wrong.");
            AssertEx.Equal(1, queue.Context.PlayerForceId, "Player binding is wrong.");
            AssertEx.Equal("scenario-1", queue.Context.ScenarioToken.Value, "Scenario binding is wrong.");
            AssertEx.Equal("turn-1", queue.Context.TurnToken.Value, "Turn binding is wrong.");
            AssertEx.Equal("strategy", queue.Context.PhaseToken.Value, "Phase binding is wrong.");
            AssertEx.Equal(1L, queue.Context.InitialSnapshotGeneration.Value, "Generation binding is wrong.");

            PlanningResult mixedPlan = new DomesticPlanner().Build(
                DefaultPlan("wealthy"),
                queue,
                snapshot);
            AssertFatal(mixedPlan, PlanningFatalReason.PlanContextMismatch);

            ExecutionPlan changedConfiguration = CreatePlan(
                "basic",
                new[] { DomesticCommand.Commerce, DomesticCommand.Cultivate },
                1,
                SelectionPolicy.NativeBest,
                5,
                5,
                true,
                null);
            AssertEx.True(
                !string.Equals(
                    plan.ConfigurationFingerprint,
                    changedConfiguration.ConfigurationFingerprint,
                    StringComparison.Ordinal),
                "Changed configuration must have a different fingerprint.");
            PlanningResult mixedConfiguration = new DomesticPlanner().Build(
                changedConfiguration,
                queue,
                snapshot);
            AssertFatal(mixedConfiguration, PlanningFatalReason.PlanContextMismatch);
        }

        private static void ChangedBatchContextIsFatal()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot city = CreateFacility(1, FacilityType.City, true, 1, 1, 10, null, true, null);
            GameSnapshot initial = SnapshotWithContext(1000, PlanningContext(2), city);
            CityQueueSnapshot queue = CityQueueSnapshot.Capture(plan, initial);

            GameSnapshotContext[] changedContexts =
            {
                PlanningContext(3, 24681, 638900000000000000L, 1, "scenario-1", "turn-1", "strategy", true),
                PlanningContext(3, 24680, 638900000000000001L, 1, "scenario-1", "turn-1", "strategy", true),
                PlanningContext(3, 24680, 638900000000000000L, 2, "scenario-1", "turn-1", "strategy", true),
                PlanningContext(3, 24680, 638900000000000000L, 1, "scenario-2", "turn-1", "strategy", true),
                PlanningContext(3, 24680, 638900000000000000L, 1, "scenario-1", "turn-2", "strategy", true),
                PlanningContext(3, 24680, 638900000000000000L, 1, "scenario-1", "turn-1", "dialog", true),
                PlanningContext(3, 24680, 638900000000000000L, 1, "scenario-1", "turn-1", "strategy", false),
                PlanningContext(1, 24680, 638900000000000000L, 1, "scenario-1", "turn-1", "strategy", true)
            };

            for (int index = 0; index < changedContexts.Length; index++)
            {
                GameSnapshot changed = SnapshotWithContext(1000, changedContexts[index], city);
                PlanningResult result = new DomesticPlanner().Build(plan, queue, changed);
                AssertFatal(result, PlanningFatalReason.BatchContextChanged);
            }
        }

        private static void StableUnverifiedTokensCanPreview()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot city = CreateFacility(1, FacilityType.City, true, 1, 1, 10, null, true, null);
            GameSnapshot initial = SnapshotWithContext(
                1000,
                PlanningContext(1, 24680, 638900000000000000L, 1, "unverified-scenario", "unverified-turn", "unverified-phase", false),
                city);
            CityQueueSnapshot queue = CityQueueSnapshot.Capture(plan, initial);
            GameSnapshot next = SnapshotWithContext(
                1000,
                PlanningContext(2, 24680, 638900000000000000L, 1, "unverified-scenario", "unverified-turn", "unverified-phase", false),
                city);

            PlanningResult result = new DomesticPlanner().Build(plan, queue, next, PlanningMode.Preview);

            AssertEx.False(result.IsAborted, "Stable unverified tokens should remain usable for read-only preview.");
            AssertEx.False(result.CommitAuthorized, "Unverified preview must not authorize a commit.");
        }

        private static void SnapshotInvariantsRejectCorruption()
        {
            AssertEx.Throws<ArgumentException>(
                delegate
                {
                    new NativeCommandSnapshot(
                        DomesticCommand.Commerce,
                        true,
                        NativeCommandBlockReason.GreyedOut,
                        new int[0],
                        50);
                },
                "Executable command with a block reason must fail.");

            AssertEx.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    new NativeCommandSnapshot(
                        DomesticCommand.Commerce,
                        true,
                        NativeCommandBlockReason.None,
                        new int[0],
                        -1);
                },
                "Negative command cost must fail; zero is valid for commands such as training.");

            AssertEx.Throws<ArgumentException>(
                delegate
                {
                    new NativeCommandSnapshot(
                        DomesticCommand.Commerce,
                        true,
                        NativeCommandBlockReason.None,
                        new[] { 1, 1 },
                        50);
                },
                "Duplicate candidate ids must fail.");

            AssertEx.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    new NativeCommandSnapshot(
                        (DomesticCommand)999,
                        true,
                        NativeCommandBlockReason.None,
                        new int[0],
                        50);
                },
                "Undefined command enum must fail.");

            AssertEx.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    new NativeCommandSnapshot(
                        DomesticCommand.Commerce,
                        false,
                        (NativeCommandBlockReason)999,
                        new int[0],
                        0);
                },
                "Undefined block-reason enum must fail.");

            NativeCommandSnapshot unknownCandidate = new NativeCommandSnapshot(
                DomesticCommand.Commerce,
                true,
                NativeCommandBlockReason.None,
                new[] { 99 },
                50);
            AssertEx.Throws<ArgumentException>(
                delegate
                {
                    new FacilitySnapshot(
                        1,
                        "BadCandidate",
                        FacilityType.City,
                        1,
                        1,
                        true,
                        CreateOfficers(1, 5),
                        new[] { unknownCandidate });
                },
                "Candidate outside its city must fail.");
        }

        private static void OtherNativeBlockIsFatal()
        {
            IDictionary<DomesticCommand, NativeCommandBlockReason> blocked =
                new Dictionary<DomesticCommand, NativeCommandBlockReason>
                {
                    { DomesticCommand.Commerce, NativeCommandBlockReason.Other }
                };
            PlanningResult result = Build(
                DefaultPlan("basic"),
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 10, blocked, true, null));

            AssertFatal(result, PlanningFatalReason.InvalidNativeCommandState);
            AssertEx.Equal(0, result.Cities[0].Tasks.Count, "Unclassified state must stop before later tasks.");
        }

        private static void ExactCountRequiresEqualBounds()
        {
            string json = MinimalJson("commerce", 2, 5, "native_best", 0, "exact");
            ConfigurationValidationException exception = AssertEx.Throws<ConfigurationValidationException>(
                delegate { new ConfigurationLoader().LoadJson(json); },
                "Exact-count bounds must match.");
            AssertIssue(
                exception,
                "$.profiles[0].tasks[0].requireExactCount",
                "minOfficers and maxOfficers to be equal");
        }

        private static void DisabledConfigurationIsOmitted()
        {
            List<TaskConfiguration> tasks = new List<TaskConfiguration>
            {
                new TaskConfiguration(DomesticCommand.Commerce, false, 5, 5, true, SelectionPolicy.NativeBest, null),
                new TaskConfiguration(DomesticCommand.Cultivate, true, 5, 5, true, SelectionPolicy.NativeBest, null)
            };
            DomesticConfiguration configuration = new DomesticConfiguration(
                1,
                new[]
                {
                    new ProfileConfiguration("enabled", "启用", true, tasks),
                    new ProfileConfiguration("disabled", "关闭", false, tasks)
                },
                CityScope.DirectCities,
                CityOrder.GameIdAscending,
                0,
                true);
            ExecutionPlanCatalog catalog = new ExecutionPlanCompiler().Compile(configuration);

            AssertEx.Equal(1, catalog.Plans.Count, "Disabled profile was compiled.");
            AssertEx.SequenceEqual(
                new[] { DomesticCommand.Cultivate },
                catalog.GetRequired("enabled").Tasks.Select(item => item.Command),
                "Disabled task was compiled.");
        }

        private static void BatchRunGateUsesOwnedLeases()
        {
            BatchRunCoordinator firstEntry = BatchRunCoordinator.ProcessWide;
            BatchRunCoordinator secondEntry = BatchRunCoordinator.ProcessWide;
            AssertEx.True(ReferenceEquals(firstEntry, secondEntry), "All entry points must share one coordinator.");

            BatchRunLease first = null;
            BatchRunLease second = null;
            try
            {
                AssertEx.True(firstEntry.TryBegin(out first), "First batch should acquire the coordinator.");
                AssertEx.True(secondEntry.IsRunning, "A second entry must see the active run.");
                BatchRunLease rejected;
                AssertEx.False(secondEntry.TryBegin(out rejected), "Second entry must be rejected.");
                AssertEx.Equal(null, rejected, "Rejected acquisition must not receive a lease.");
                first.Dispose();
                AssertEx.False(firstEntry.IsRunning, "Coordinator should be released.");
                AssertEx.True(secondEntry.TryBegin(out second), "A later batch should start after release.");
                AssertEx.True(second.Generation > first.Generation, "Lease generation must increase.");

                first.Dispose();
                AssertEx.True(firstEntry.IsRunning, "A stale/double release must not release the current generation.");
            }
            finally
            {
                if (second != null)
                {
                    second.Dispose();
                }

                if (first != null)
                {
                    first.Dispose();
                }
            }

            AssertEx.False(firstEntry.IsRunning, "Current owner should release the coordinator.");
        }

        private static void MoneyMapIsReadOnly()
        {
            PlanningResult result = Build(
                CreatePlan(
                    "money-map",
                    new[] { DomesticCommand.Commerce },
                    0,
                    SelectionPolicy.NativeBest,
                    5,
                    5,
                    true,
                    null),
                1000,
                CreateFacility(1, FacilityType.City, true, 1, 1, 5, null, true, null));

            object exposedMap = result.RemainingMoneyByCorps;
            AssertEx.False(
                exposedMap is IDictionary<int, int>,
                "Read-only money map must not expose an IDictionary mutation surface.");
            AssertEx.Equal(750, result.RemainingMoneyByCorps[1], "Read-only money snapshot value is wrong.");
        }

        private static void PublicVerificationCannotBeAsserted()
        {
            GameContextToken publicToken = new GameContextToken("scenario-public");
            AssertEx.False(publicToken.IsVerified, "A public token constructor must always be unverified.");

            NativeCommandSnapshot publicCommand = new NativeCommandSnapshot(
                DomesticCommand.Commerce,
                true,
                NativeCommandBlockReason.None,
                new int[0],
                GameRules.DefaultDomesticCostPerOfficer);
            AssertEx.False(
                publicCommand.HasVerifiedNativeRanking,
                "A public native-command constructor must not assert verified ranking.");

            GameSnapshotContext publicContext = new GameSnapshotContext(
                24680,
                638900000000000000L,
                1,
                new GameContextToken("scenario-public"),
                new GameContextToken("turn-public"),
                new GameContextToken("strategy-public"),
                1,
                SnapshotReadiness.PlanningReady,
                string.Empty);
            AssertEx.False(
                publicContext.HasTrustedObservation,
                "A public planning context must not acquire trusted provenance.");
            AssertEx.True(
                publicContext.ScenarioToken != null && !publicContext.ScenarioToken.IsVerified,
                "Public context tokens must remain unverified.");

            GameSnapshot publicSnapshot = new GameSnapshot(
                1,
                new[]
                {
                    new FacilitySnapshot(
                        1,
                        "PublicPreview",
                        FacilityType.City,
                        1,
                        1,
                        true,
                        new OfficerSnapshot[0],
                        new[] { publicCommand })
                },
                new[] { new CorpsMoneySnapshot(1, 1000) },
                publicContext);
            ExecutionPlan publicPlan = CreatePlan(
                "public-preview",
                new[] { DomesticCommand.Commerce },
                0,
                SelectionPolicy.NativeBest,
                5,
                5,
                true,
                null);
            CityQueueSnapshot publicQueue = CityQueueSnapshot.Capture(publicPlan, publicSnapshot);
            AssertEx.Throws<InvalidOperationException>(
                delegate
                {
                    publicQueue.CreateCommandObservationRequest(
                        publicPlan,
                        1,
                        DomesticCommand.Commerce);
                },
                "An untrusted public snapshot must not issue a command reread challenge.");
        }

        private static void TrustedObservationBindingIsEnforced()
        {
            TrustedObservationCapability contextCapability = TrustedSnapshotFactory.BeginPlanningObservation();
            TrustedObservationCapability foreignCapability = TrustedSnapshotFactory.BeginPlanningObservation();
            GameSnapshotContext context = TrustedSnapshotFactory.CreatePlanningContext(
                contextCapability,
                24680,
                638900000000000000L,
                1,
                "scenario-1",
                "turn-1",
                "strategy",
                true,
                1,
                string.Empty);
            AssertEx.Throws<InvalidOperationException>(
                delegate
                {
                    TrustedSnapshotFactory.CreatePlanningContext(
                        contextCapability,
                        24680,
                        638900000000000000L,
                        1,
                        "scenario-1",
                        "turn-1",
                        "strategy",
                        true,
                        2,
                        string.Empty);
                },
                "One observation capability must not be reused for a second context.");
            List<OfficerSnapshot> officers = CreateOfficers(1, 5);
            NativeCommandSnapshot foreignCommand = TrustedSnapshotFactory.CreateNativeCommand(
                foreignCapability,
                DomesticCommand.Commerce,
                true,
                NativeCommandBlockReason.None,
                officers.Select(item => item.Id),
                GameRules.DefaultDomesticCostPerOfficer);
            FacilitySnapshot mismatchedFacility = new FacilitySnapshot(
                1,
                "MismatchedObservation",
                FacilityType.City,
                1,
                1,
                true,
                officers,
                new[] { foreignCommand });
            GameSnapshot mismatched = new GameSnapshot(
                1,
                new[] { mismatchedFacility },
                new[] { new CorpsMoneySnapshot(1, 1000) },
                context);

            AssertEx.False(mismatched.IsPlanningComplete, "Foreign observation ranking must invalidate the snapshot.");
            PlanningResult fatal = new DomesticPlanner().Build(
                CreatePlan(
                    "observation-mismatch",
                    new[] { DomesticCommand.Commerce },
                    0,
                    SelectionPolicy.NativeBest,
                    5,
                    5,
                    true,
                    null),
                mismatched);
            AssertFatal(fatal, PlanningFatalReason.SnapshotIncomplete);

            NativeCommandSnapshot matchingCommand = TrustedSnapshotFactory.CreateNativeCommand(
                contextCapability,
                DomesticCommand.Commerce,
                true,
                NativeCommandBlockReason.None,
                officers.Select(item => item.Id),
                GameRules.DefaultDomesticCostPerOfficer);
            GameSnapshot matching = new GameSnapshot(
                1,
                new[]
                {
                    new FacilitySnapshot(
                        1,
                        "MatchingObservation",
                        FacilityType.City,
                        1,
                        1,
                        true,
                        officers,
                        new[] { matchingCommand })
                },
                new[] { new CorpsMoneySnapshot(1, 1000) },
                context);
            AssertEx.True(matching.IsPlanningComplete, "A shared trusted observation must pass provenance validation.");
        }

        private static void CommandObservationChallengeIsBound()
        {
            ExecutionPlan plan = CreatePlan(
                "command-observation",
                new[] { DomesticCommand.Commerce },
                0,
                SelectionPolicy.NativeBest,
                5,
                5,
                true,
                null);
            FacilitySnapshot initialCity = CreateFacility(
                1,
                FacilityType.City,
                true,
                1,
                1,
                5,
                null,
                true,
                null);
            GameSnapshot initial = SnapshotWithContext(1000, PlanningContext(1), initialCity);
            CityQueueSnapshot queue = CityQueueSnapshot.Capture(plan, initial);
            CommandObservationRequest request = queue.CreateCommandObservationRequest(
                plan,
                1,
                DomesticCommand.Commerce);
            AssertEx.Equal(queue.Context.BatchId, request.BatchId, "Command request batch binding is wrong.");
            AssertEx.Equal(1L, request.MinimumSnapshotGeneration, "Command request generation binding is wrong.");

            TrustedObservationCapability capability = TrustedSnapshotFactory.BeginCommandObservation(request);
            GameSnapshotContext context = TrustedSnapshotFactory.CreatePlanningContext(
                capability,
                24680,
                638900000000000000L,
                1,
                "scenario-1",
                "turn-1",
                "strategy",
                true,
                2,
                string.Empty);
            List<OfficerSnapshot> officers = CreateOfficers(1, 5);
            NativeCommandSnapshot command = TrustedSnapshotFactory.CreateNativeCommand(
                capability,
                DomesticCommand.Commerce,
                true,
                NativeCommandBlockReason.None,
                officers.Select(item => item.Id),
                GameRules.DefaultDomesticCostPerOfficer);
            GameSnapshot reread = new GameSnapshot(
                1,
                new[]
                {
                    new FacilitySnapshot(
                        1,
                        "CommandReread",
                        FacilityType.City,
                        1,
                        1,
                        true,
                        officers,
                        new[] { command })
                },
                new[] { new CorpsMoneySnapshot(1, 1000) },
                context);
            AssertEx.True(reread.IsPlanningComplete, "A correctly bound command reread must be complete.");
            AssertEx.Equal(
                request.RequestId,
                reread.Context.CommandObservationRequestId,
                "Command request id was not carried by the observation.");

            TrustedObservationCapability staleCapability = TrustedSnapshotFactory.BeginCommandObservation(request);
            GameSnapshotContext staleContext = TrustedSnapshotFactory.CreatePlanningContext(
                staleCapability,
                24680,
                638900000000000000L,
                1,
                "scenario-1",
                "turn-1",
                "strategy",
                true,
                1,
                string.Empty);
            NativeCommandSnapshot staleCommand = TrustedSnapshotFactory.CreateNativeCommand(
                staleCapability,
                DomesticCommand.Commerce,
                true,
                NativeCommandBlockReason.None,
                officers.Select(item => item.Id),
                GameRules.DefaultDomesticCostPerOfficer);
            GameSnapshot stale = new GameSnapshot(
                1,
                new[]
                {
                    new FacilitySnapshot(
                        1,
                        "StaleCommandReread",
                        FacilityType.City,
                        1,
                        1,
                        true,
                        officers,
                        new[] { staleCommand })
                },
                new[] { new CorpsMoneySnapshot(1, 1000) },
                staleContext);
            AssertEx.False(stale.IsPlanningComplete, "A command reread must advance snapshot generation.");

            NativeCommandSnapshot wrongCommand = TrustedSnapshotFactory.CreateNativeCommand(
                capability,
                DomesticCommand.Cultivate,
                true,
                NativeCommandBlockReason.None,
                officers.Select(item => item.Id),
                GameRules.DefaultDomesticCostPerOfficer);
            GameSnapshot wrongBinding = new GameSnapshot(
                1,
                new[]
                {
                    new FacilitySnapshot(
                        1,
                        "WrongCommand",
                        FacilityType.City,
                        1,
                        1,
                        true,
                        officers,
                        new[] { wrongCommand })
                },
                new[] { new CorpsMoneySnapshot(1, 1000) },
                context);
            AssertEx.False(wrongBinding.IsPlanningComplete, "A command request must not validate another command.");
        }

        private static void PlanningReadyContradictionsAbort()
        {
            GameSnapshotContext context = PlanningContext(1);
            FacilitySnapshot contradictory = new FacilitySnapshot(
                1,
                "ContradictoryDirectCity",
                FacilityType.City,
                2,
                1,
                true,
                new OfficerSnapshot[0],
                new NativeCommandSnapshot[0]);
            GameSnapshot snapshot = new GameSnapshot(
                1,
                new[] { contradictory },
                new[] { new CorpsMoneySnapshot(1, 1000) },
                context);
            AssertEx.False(snapshot.IsPlanningComplete, "Contradictory direct-control fields must invalidate planning.");
            AssertFatal(
                new DomesticPlanner().Build(DefaultPlan("basic"), snapshot),
                PlanningFatalReason.SnapshotIncomplete);

            FacilitySnapshot missingCorps = new FacilitySnapshot(
                1,
                "MissingCorps",
                FacilityType.City,
                1,
                -1,
                true,
                new OfficerSnapshot[0],
                new NativeCommandSnapshot[0]);
            GameSnapshot noCorps = new GameSnapshot(
                1,
                new[] { missingCorps },
                new CorpsMoneySnapshot[0],
                PlanningContext(1));
            AssertEx.False(noCorps.IsPlanningComplete, "A direct city requires a verified corps id.");
        }

        private static void FacilityIdentityChangeIsFatal()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot initialCity = CreateFacility(
                1,
                FacilityType.City,
                true,
                1,
                1,
                10,
                null,
                true,
                null);
            GameSnapshot initial = SnapshotWithContext(1000, PlanningContext(1), initialCity);
            CityQueueSnapshot queue = CityQueueSnapshot.Capture(plan, initial);
            FacilitySnapshot changedToGate = new FacilitySnapshot(
                1,
                "NowGate",
                FacilityType.Gate,
                1,
                1,
                false,
                new OfficerSnapshot[0],
                new NativeCommandSnapshot[0]);
            GameSnapshot changed = SnapshotWithContext(1000, PlanningContext(2), changedToGate);

            PlanningResult result = new DomesticPlanner().Build(plan, queue, changed);
            AssertFatal(result, PlanningFatalReason.FacilityIdentityChanged);
        }

        private static void AbortedBatchDiscardsProjections()
        {
            ExecutionPlan plan = DefaultPlan("basic");
            FacilitySnapshot first = CreateFacility(
                1,
                FacilityType.City,
                true,
                1,
                1,
                10,
                null,
                true,
                null);
            FacilitySnapshot incomplete = new FacilitySnapshot(
                2,
                "IncompleteLaterCity",
                FacilityType.City,
                1,
                2,
                true,
                CreateOfficers(100, 5),
                new NativeCommandSnapshot[0]);
            PlanningResult result = new DomesticPlanner().Build(
                plan,
                SnapshotWithContext(1000, PlanningContext(1), first, incomplete));

            AssertFatal(result, PlanningFatalReason.MissingCommandSnapshot);
            AssertEx.Equal(0, result.ProjectedTaskCount, "An aborted batch must expose no usable projections.");
            AssertEx.Equal(2, result.DiscardedTaskCount, "Earlier city projections should be explicitly discarded.");
            AssertEx.Equal(0, result.RemainingMoneyByCorps.Count, "Aborted projected balances must not be exposed.");
            AssertEx.True(
                result.Cities[0].Tasks.All(task => task.Decision == PlanningDecision.Discarded),
                "Every earlier projection must be marked discarded.");
            AssertEx.True(
                result.Cities[0].Tasks.All(task => task.SelectedOfficerIds.Count == 0),
                "Discarded projections must not retain selectable officer ids.");
        }

        private static ExecutionPlan DefaultPlan(string profileId)
        {
            // Stable synthetic fixtures keep planner safety tests independent of
            // edits to the shipped default.json. DefaultConfigurationCompiles is
            // the single golden test for the two shipped example profiles.
            if (string.Equals(profileId, "basic", StringComparison.Ordinal))
            {
                return CreatePlan(
                    profileId,
                    new[] { DomesticCommand.Commerce, DomesticCommand.Cultivate },
                    0,
                    SelectionPolicy.NativeBest,
                    5,
                    5,
                    true,
                    null);
            }
            if (string.Equals(profileId, "wealthy", StringComparison.Ordinal))
            {
                return CreatePlan(
                    profileId,
                    new[]
                    {
                        DomesticCommand.Patrol,
                        DomesticCommand.Commerce,
                        DomesticCommand.Cultivate,
                        DomesticCommand.Train,
                        DomesticCommand.Repair
                    },
                    0,
                    SelectionPolicy.NativeBest,
                    5,
                    5,
                    true,
                    null);
            }

            throw new InvalidOperationException("Unknown synthetic planner fixture: " + profileId + ".");
        }

        private static ExecutionPlan CreatePlan(
            string id,
            IEnumerable<DomesticCommand> commands,
            int reserveMoney,
            SelectionPolicy policy,
            int minOfficers,
            int maxOfficers,
            bool exact,
            int? reserveOverride)
        {
            List<TaskConfiguration> tasks = commands.Select(command => new TaskConfiguration(
                command,
                true,
                minOfficers,
                maxOfficers,
                exact,
                policy,
                reserveOverride)).ToList();
            DomesticConfiguration configuration = new DomesticConfiguration(
                1,
                new[] { new ProfileConfiguration(id, id, true, tasks) },
                CityScope.DirectCities,
                CityOrder.GameIdAscending,
                reserveMoney,
                true);
            return new ExecutionPlanCompiler().Compile(configuration).GetRequired(id);
        }

        private static PlanningResult Build(ExecutionPlan plan, int money, params FacilitySnapshot[] facilities)
        {
            return new DomesticPlanner().Build(plan, Snapshot(money, facilities));
        }

        private static GameSnapshot Snapshot(int money, params FacilitySnapshot[] facilities)
        {
            return SnapshotWithContext(money, PlanningContext(1), facilities);
        }

        private static GameSnapshot SnapshotWithContext(
            int money,
            GameSnapshotContext context,
            params FacilitySnapshot[] facilities)
        {
            FacilitySnapshot[] boundFacilities = facilities
                .Select(item => BindFacilityToObservation(item, context.ObservationCapability))
                .ToArray();
            HashSet<int> corpsIds = new HashSet<int>(
                boundFacilities.Select(item => item.CorpsId).Where(id => id >= 0));
            List<CorpsMoneySnapshot> funds = corpsIds.Select(id => new CorpsMoneySnapshot(id, money)).ToList();
            return new GameSnapshot(context.PlayerForceId.Value, boundFacilities, funds, context);
        }

        private static GameSnapshotContext PlanningContext(long generation)
        {
            return PlanningContext(
                generation,
                24680,
                638900000000000000L,
                1,
                "scenario-1",
                "turn-1",
                "strategy",
                true);
        }

        private static GameSnapshotContext PlanningContext(
            long generation,
            int processId,
            long processStartUtcTicks,
            int playerForceId,
            string scenario,
            string turn,
            string phase,
            bool verified)
        {
            TrustedObservationCapability capability = TrustedSnapshotFactory.BeginPlanningObservation();
            return TrustedSnapshotFactory.CreatePlanningContext(
                capability,
                processId,
                processStartUtcTicks,
                playerForceId,
                scenario,
                turn,
                phase,
                verified,
                generation,
                string.Empty);
        }

        private static FacilitySnapshot BindFacilityToObservation(
            FacilitySnapshot facility,
            TrustedObservationCapability capability)
        {
            if (capability == null
                || !facility.CommandSnapshots.Any(command => command.HasVerifiedNativeRanking))
            {
                return facility;
            }

            List<NativeCommandSnapshot> commands = facility.CommandSnapshots
                .Select(command => command.HasVerifiedNativeRanking
                    ? TrustedSnapshotFactory.ReissueNativeCommand(capability, command)
                    : command)
                .ToList();
            return new FacilitySnapshot(
                facility.Id,
                facility.Name,
                facility.FacilityType,
                facility.OwnerForceId,
                facility.CorpsId,
                facility.IsDirectlyControlled,
                facility.Officers,
                commands);
        }

        private static FacilitySnapshot CreateFacility(
            int facilityId,
            FacilityType type,
            bool direct,
            int corpsId,
            int firstOfficerId,
            int officerCount,
            IDictionary<DomesticCommand, NativeCommandBlockReason> blocked,
            bool verifiedNativeRanking,
            IDictionary<DomesticCommand, IList<int>> customRankings)
        {
            List<OfficerSnapshot> officers = CreateOfficers(firstOfficerId, officerCount);

            return CreateFacilityWithOfficers(
                facilityId,
                direct,
                corpsId,
                officers,
                blocked,
                verifiedNativeRanking,
                customRankings,
                type);
        }

        private static List<OfficerSnapshot> CreateOfficers(int firstOfficerId, int officerCount)
        {
            List<OfficerSnapshot> officers = new List<OfficerSnapshot>();
            for (int index = 0; index < officerCount; index++)
            {
                int id = firstOfficerId + index;
                int ability = 1000 - index;
                officers.Add(new OfficerSnapshot(
                    id,
                    "Officer" + id.ToString(CultureInfo.InvariantCulture),
                    true,
                    ability,
                    ability,
                    ability,
                    ability));
            }

            return officers;
        }

        private static FacilitySnapshot CreateFacilityWithOfficers(
            int facilityId,
            bool direct,
            int corpsId,
            IList<OfficerSnapshot> officers,
            IDictionary<DomesticCommand, NativeCommandBlockReason> blocked,
            bool verifiedNativeRanking,
            IDictionary<DomesticCommand, IList<int>> customRankings)
        {
            return CreateFacilityWithOfficers(
                facilityId,
                direct,
                corpsId,
                officers,
                blocked,
                verifiedNativeRanking,
                customRankings,
                FacilityType.City);
        }

        private static FacilitySnapshot CreateFacilityWithOfficers(
            int facilityId,
            bool direct,
            int corpsId,
            IList<OfficerSnapshot> officers,
            IDictionary<DomesticCommand, NativeCommandBlockReason> blocked,
            bool verifiedNativeRanking,
            IDictionary<DomesticCommand, IList<int>> customRankings,
            FacilityType type)
        {
            List<NativeCommandSnapshot> commands = new List<NativeCommandSnapshot>();
            TrustedObservationCapability placeholderCapability = verifiedNativeRanking
                ? TrustedSnapshotFactory.BeginPlanningObservation()
                : null;
            foreach (DomesticCommand command in new[]
            {
                DomesticCommand.Patrol,
                DomesticCommand.Commerce,
                DomesticCommand.Cultivate,
                DomesticCommand.Train,
                DomesticCommand.Repair
            })
            {
                NativeCommandBlockReason blockReason = NativeCommandBlockReason.None;
                if (blocked != null && blocked.ContainsKey(command))
                {
                    blockReason = blocked[command];
                }

                IList<int> ranking = officers.Select(item => item.Id).ToList();
                if (customRankings != null && customRankings.ContainsKey(command))
                {
                    ranking = customRankings[command];
                }

                NativeCommandSnapshot commandSnapshot = verifiedNativeRanking
                    ? TrustedSnapshotFactory.CreateNativeCommand(
                        placeholderCapability,
                        command,
                        blockReason == NativeCommandBlockReason.None,
                        blockReason,
                        ranking,
                        command == DomesticCommand.Train ? 0 : GameRules.DefaultDomesticCostPerOfficer)
                    : new NativeCommandSnapshot(
                        command,
                        blockReason == NativeCommandBlockReason.None,
                        blockReason,
                        ranking,
                        command == DomesticCommand.Train ? 0 : GameRules.DefaultDomesticCostPerOfficer);
                commands.Add(commandSnapshot);
            }

            return new FacilitySnapshot(
                facilityId,
                "Facility" + facilityId.ToString(CultureInfo.InvariantCulture),
                type,
                1,
                corpsId,
                direct,
                officers,
                commands);
        }

        private static void AssertPlanned(TaskPlanningResult task, IEnumerable<int> expectedOfficerIds)
        {
            AssertEx.Equal(PlanningDecision.Projected, task.Decision, "Task should be projected: " + task.Detail);
            AssertEx.Equal(PlanningSkipReason.None, task.SkipReason, "Planned task has a skip reason.");
            AssertEx.SequenceEqual(expectedOfficerIds, task.SelectedOfficerIds, "Selected officers are wrong.");
        }

        private static void AssertSkipped(TaskPlanningResult task, PlanningSkipReason reason)
        {
            AssertEx.Equal(PlanningDecision.Skipped, task.Decision, "Task should be skipped.");
            AssertEx.Equal(reason, task.SkipReason, "Skip reason is wrong. Detail: " + task.Detail);
            AssertEx.True(!string.IsNullOrWhiteSpace(task.Detail), "Skipped task needs an explanatory detail.");
        }

        private static void AssertFatal(PlanningResult result, PlanningFatalReason reason)
        {
            AssertEx.True(result.IsAborted, "Planning should have aborted.");
            AssertEx.Equal(PlanningOutcome.Aborted, result.Outcome, "Planning outcome is wrong.");
            AssertEx.True(result.FatalIssue != null, "Aborted planning needs a fatal issue.");
            AssertEx.Equal(reason, result.FatalIssue.Reason, "Fatal reason is wrong: " + result.FatalIssue.Detail);
            AssertEx.True(!string.IsNullOrWhiteSpace(result.FatalIssue.Detail), "Fatal issue needs an explanatory detail.");
            AssertEx.False(result.CommitAuthorized, "Aborted planning cannot authorize a commit.");
        }

        private static void AssertIssue(
            ConfigurationValidationException exception,
            string path,
            string messageFragment)
        {
            bool found = exception.Issues.Any(issue =>
                string.Equals(issue.Path, path, StringComparison.Ordinal)
                && issue.Message.IndexOf(messageFragment, StringComparison.OrdinalIgnoreCase) >= 0);
            AssertEx.True(found, "Expected configuration issue at " + path + " containing '" + messageFragment + "'. Actual: " + exception.Message);
        }

        private static string FindRepositoryFile(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory })
            {
                DirectoryInfo directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            throw new FileNotFoundException("Could not locate repository file: " + relativePath);
        }

        private static string MinimalJson(
            string command,
            int min,
            int max,
            string selectionPolicy,
            int reserveMoney,
            string id)
        {
            return "{\"schemaVersion\":1,\"profiles\":[" + ProfileJson(id, command, min, max, selectionPolicy)
                + "],\"cityScope\":\"direct_cities\",\"cityOrder\":\"game_id_asc\","
                + "\"reserveMoney\":" + reserveMoney.ToString(CultureInfo.InvariantCulture)
                + ",\"dryRunByDefault\":true}";
        }

        private static string ProfileJson(string id, string command, int min, int max, string selectionPolicy)
        {
            return "{\"id\":\"" + id + "\",\"displayName\":\"Profile\",\"enabled\":true,\"tasks\":[{"
                + "\"command\":\"" + command + "\",\"enabled\":true,"
                + "\"minOfficers\":" + min.ToString(CultureInfo.InvariantCulture) + ","
                + "\"maxOfficers\":" + max.ToString(CultureInfo.InvariantCulture) + ","
                + "\"requireExactCount\":true,\"selectionPolicy\":\"" + selectionPolicy + "\"}]}";
        }
    }
}
