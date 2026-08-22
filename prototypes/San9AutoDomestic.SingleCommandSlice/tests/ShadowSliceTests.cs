using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;
using San9AutoDomestic.V8Transaction;

namespace San9AutoDomestic.SingleCommandSlice.SelfTest
{
    internal static class ShadowSliceTests
    {
        private sealed class TestCase
        {
            public TestCase(string name, Action body)
            {
                Name = name;
                Body = body;
            }

            public string Name { get; private set; }

            public Action Body { get; private set; }
        }

        private sealed class FakeShadowObservationProvider : IShadowObservationProvider
        {
            private readonly object _sync = new object();
            private readonly Dictionary<string, Func<ShadowCommandObservationRequest, ShadowCommandObservation>> _rules;
            private readonly List<ShadowCommandObservationRequest> _requests;

            public FakeShadowObservationProvider(ShadowBatchObservation batch)
            {
                Batch = batch;
                _rules = new Dictionary<string, Func<ShadowCommandObservationRequest, ShadowCommandObservation>>(
                    StringComparer.Ordinal);
                _requests = new List<ShadowCommandObservationRequest>();
            }

            public ShadowBatchObservation Batch { get; private set; }

            public bool ThrowOnObserve { get; set; }

            public string CapturedProfileId { get; private set; }

            public string CapturedFingerprint { get; private set; }

            public int CaptureCount { get; private set; }

            public int ObserveCount
            {
                get
                {
                    lock (_sync)
                    {
                        return _requests.Count;
                    }
                }
            }

            public IList<ShadowCommandObservationRequest> Requests
            {
                get
                {
                    lock (_sync)
                    {
                        return new List<ShadowCommandObservationRequest>(_requests);
                    }
                }
            }

            public ShadowBatchObservation CaptureBatch(
                string profileId,
                string configurationFingerprint)
            {
                lock (_sync)
                {
                    CaptureCount++;
                    CapturedProfileId = profileId;
                    CapturedFingerprint = configurationFingerprint;
                    return Batch;
                }
            }

            public ShadowCommandObservation ObserveCommand(
                ShadowCommandObservationRequest request)
            {
                lock (_sync)
                {
                    _requests.Add(request);
                }

                if (ThrowOnObserve)
                {
                    throw new InvalidOperationException("synthetic failure");
                }

                Func<ShadowCommandObservationRequest, ShadowCommandObservation> rule;
                if (_rules.TryGetValue(Key(request.CityId, request.Command), out rule))
                {
                    return rule(request);
                }

                return CreateObservation(request);
            }

            public void SetRule(
                int cityId,
                DomesticCommand command,
                Func<ShadowCommandObservationRequest, ShadowCommandObservation> rule)
            {
                _rules[Key(cityId, command)] = rule;
            }

            public ShadowCommandObservation CreateObservation(
                ShadowCommandObservationRequest request,
                FixedDigest generationDigest = null,
                int? cityId = null,
                int? ownerForceId = null,
                int? corpsId = null,
                bool? isDirectlyControlled = null,
                DomesticCommand? command = null,
                bool canExecute = true,
                NativeCommandBlockReason blockReason = NativeCommandBlockReason.None,
                IEnumerable<int> rankedCandidateOfficerIds = null)
            {
                ShadowCitySeed city = Batch.Cities.Single(item => item.CityId == request.CityId);
                return new ShadowCommandObservation(
                    generationDigest ?? Batch.GenerationDigest,
                    cityId ?? city.CityId,
                    ownerForceId ?? city.OwnerForceId,
                    corpsId ?? city.CorpsId,
                    isDirectlyControlled ?? city.IsDirectlyControlled,
                    command ?? request.Command,
                    canExecute,
                    blockReason,
                    rankedCandidateOfficerIds ?? city.AvailableOfficerIds);
            }

            private static string Key(int cityId, DomesticCommand command)
            {
                return cityId + ":" + (int)command;
            }
        }

        public static int RunAll()
        {
            List<TestCase> tests = new List<TestCase>
            {
                new TestCase("permanent shadow build contract", PermanentShadowBuildContract),
                new TestCase("basic profile maps commerce then cultivate", BasicProfileOrderAndMapping),
                new TestCase("wealthy profile maps all five commands", WealthyFiveCommandMapping),
                new TestCase("multiple cities are stable id ascending", MultipleCitiesAscending),
                new TestCase("task order follows enabled configuration order", ConfiguredTaskOrder),
                new TestCase("disabled tasks disappear without ordinal holes", DisabledTaskExcluded),
                new TestCase("grey task skips and preserves officers and funds", GreySkipContinuesWithoutConsumption),
                new TestCase("delegated city skips without provider read", DelegatedCitySkips),
                new TestCase("foreign city skips without provider read", ForeignCitySkips),
                new TestCase("under five skips and later task can use five", UnderFiveSkipContinues),
                new TestCase("committed officers make later task skip", CommitDepletionMakesLaterTaskSkip),
                new TestCase("insufficient funds skip preserves officers", InsufficientFundsPreservesOfficers),
                new TestCase("reserve boundary skip preserves officers", ReserveBoundaryPreservesOfficers),
                new TestCase("shared corps funds are consumed across cities", SharedCorpsFunds),
                new TestCase("validation does not consume before commit", CommitIsOnlyResourceConsumptionBoundary),
                new TestCase("ticket evaluates exactly once", DuplicateTicketHalts),
                new TestCase("higher priority ordinal cannot be skipped", OutOfOrderTicketHalts),
                new TestCase("wrong ordinal halts even with outstanding work", OutstandingWrongOrdinalHalts),
                new TestCase("same ordinal reports outstanding work", SameOrdinalReportsOutstandingWork),
                new TestCase("ticket from another run is rejected", CrossRunTicketHalts),
                new TestCase("generation switch halts", GenerationSwitchHalts),
                new TestCase("observation identity mismatch halts", ObservationMismatchHalts),
                new TestCase("stop before ticket releases slot", StopBeforeTicket),
                new TestCase("stop consumes outstanding ticket without resources", StopWithOutstandingTicket),
                new TestCase("stop after validation does not commit", StopAfterValidation),
                new TestCase("single flight rejects a second run", SingleFlightRejectsSecondRun),
                new TestCase("concurrent starts have one winner", ConcurrentSingleFlight),
                new TestCase("completion releases single flight", CompletionReleasesSingleFlight),
                new TestCase("profile and fingerprint reach fake provider", ProfileFingerprintBinding),
                new TestCase("non exact-five profile is rejected", NonExactFiveRejected),
                new TestCase("candidate outside frozen city halts", ForeignCandidateHalts),
                new TestCase("officer cannot be seeded in two cities", CrossCityOfficerSeedRejected),
                new TestCase("non-grey native block skips and continues", NonGreyBlockSkips),
                new TestCase("zero-cost training ignores money reserve", ZeroCostTrainingAllowed),
                new TestCase("sequences retain stable ordinal across skips", StableSequenceAcrossSkips),
                new TestCase("provider exception fail-closes", ProviderExceptionHalts),
                new TestCase("callback stop cannot resurrect and second run survives", CallbackStopCannotResurrect),
                new TestCase("callback dispose cannot resurrect", CallbackDisposeCannotResurrect),
                new TestCase("recursive evaluate cannot resurrect", RecursiveEvaluateCannotResurrect),
                new TestCase("callback commit cannot resurrect", CallbackCommitCannotResurrect),
                new TestCase("nontrivial ranking takes exact first five", NontrivialRankingTakesFirstFive),
                new TestCase("consumed officers filter then preserve fresh ranking", ConsumedOfficersFilterRanking),
                new TestCase("duplicate candidate observation is rejected", DuplicateCandidateRejected),
                new TestCase("production surface has no public capability constructors", NoPublicCapabilityConstructors),
                new TestCase("slice assembly has no pinvoke or live references", NoPInvokeOrLiveReferences),
                new TestCase("V8 request remains non-authorizing", V8RequestRemainsNonAuthorizing)
            };

            int passed = 0;
            foreach (TestCase test in tests)
            {
                test.Body();
                passed++;
                Console.WriteLine("PASS {0:D2}: {1}", passed, test.Name);
            }

            return passed;
        }

        private static void PermanentShadowBuildContract()
        {
            AssertEx.True(ShadowBuildContract.ShadowOnly, "Build must remain shadow-only.");
            AssertEx.False(ShadowBuildContract.LiveAuthorized, "Build must never authorize live work.");
            AssertEx.False(ShadowBuildContract.SupportsProcessAccess, "Process access must be absent.");
            AssertEx.False(ShadowBuildContract.SupportsIpc, "IPC must be absent.");
            AssertEx.False(ShadowBuildContract.SupportsNativeCallbacks, "Native callbacks must be absent.");
            FakeShadowObservationProvider provider = SimpleProvider();
            AssertEx.True(provider.Batch.ShadowOnly, "Batch must be shadow-only.");
            AssertEx.False(provider.Batch.LiveAuthorized, "Batch must not authorize live work.");
            AssertEx.True(provider.Batch.Cities[0].ShadowOnly, "City seed must be shadow-only.");
            AssertEx.False(provider.Batch.Cities[0].LiveAuthorized, "City seed claims live authority.");
            AssertEx.True(provider.Batch.Corps[0].ShadowOnly, "Corps seed must be shadow-only.");
            AssertEx.False(provider.Batch.Corps[0].LiveAuthorized, "Corps seed claims live authority.");
        }

        private static void BasicProfileOrderAndMapping()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(8, 1, 1, true, Officers(10, 10)) },
                new[] { Corps(1, 500) });
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                List<ValidatedSingleCommand> commands = RunToCompletion(run);
                AssertEx.SequenceEqual(
                    new[] { DomesticCommand.Commerce, DomesticCommand.Cultivate },
                    commands.Select(item => item.Command),
                    "Basic command order is wrong.");
                AssertEx.SequenceEqual(new ulong[] { 1, 2 }, commands.Select(item => item.Sequence),
                    "Basic sequences are wrong.");
                AssertEx.Equal(0, run.GetRemainingOfficerIds(8).Count,
                    "Two committed commands must consume ten officers.");
                AssertEx.Equal(0, run.GetRemainingMoney(1),
                    "Two paid commands must consume 500 money.");
            }
        }

        private static void WealthyFiveCommandMapping()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(3, 1, 2, true, Officers(100, 25)) },
                new[] { Corps(2, 1000) });
            using (SingleCommandShadowRun run = Start(Config("wealthy", WealthyCommands(), 0), "wealthy", provider))
            {
                List<ValidatedSingleCommand> commands = RunToCompletion(run);
                AssertEx.SequenceEqual(WealthyCommands(), commands.Select(item => item.Command),
                    "Wealthy command order is wrong.");
                AssertEx.SequenceEqual(
                    new[] { 0, 1, 2, 5, 3 },
                    commands.Select(item => item.V8Request.Descriptor.NativeCommandId),
                    "Frozen V8 native ids are wrong.");
                AssertEx.SequenceEqual(
                    new[] { 250, 250, 250, 0, 250 },
                    commands.Select(item => item.ExpectedCost),
                    "Frozen V8 costs are wrong.");
                AssertEx.True(commands.All(item => item.OfficerIds.Count == 5),
                    "Every V8 mapping must bind exactly five officers.");
                AssertEx.Equal(25, commands.SelectMany(item => item.OfficerIds).Distinct().Count(),
                    "Committed commands must consume disjoint officer sets.");
                AssertEx.Equal(ShadowSliceState.Completed, run.State, "Wealthy run did not complete.");
            }
        }

        private static void MultipleCitiesAscending()
        {
            FakeShadowObservationProvider provider = Provider(
                new[]
                {
                    City(7, 1, 7, true, Officers(200, 10)),
                    City(2, 1, 2, true, Officers(20, 10))
                },
                new[] { Corps(7, 500), Corps(2, 500) });
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                List<ValidatedSingleCommand> commands = RunToCompletion(run);
                AssertEx.SequenceEqual(new[] { 2, 2, 7, 7 }, commands.Select(item => item.CityId),
                    "City traversal is not stable ascending.");
                AssertEx.SequenceEqual(new[] { 0, 0, 1, 1 }, commands.Select(item => item.CityOrdinal),
                    "City ordinals are wrong.");
                AssertEx.SequenceEqual(new[] { 0, 1, 0, 1 }, commands.Select(item => item.TaskOrdinal),
                    "Task ordinals are wrong.");
            }
        }

        private static void ConfiguredTaskOrder()
        {
            DomesticCommand[] order =
            {
                DomesticCommand.Repair,
                DomesticCommand.Patrol,
                DomesticCommand.Train
            };
            FakeShadowObservationProvider provider = Provider(
                new[] { City(4, 1, 4, true, Officers(40, 15)) },
                new[] { Corps(4, 500) });
            using (SingleCommandShadowRun run = Start(Config("custom", order, 0), "custom", provider))
            {
                AssertEx.SequenceEqual(order, RunToCompletion(run).Select(item => item.Command),
                    "Configured task order was not preserved.");
            }
        }

        private static void DisabledTaskExcluded()
        {
            DomesticConfiguration configuration = ConfigWithTasks(
                "custom",
                0,
                Task(DomesticCommand.Patrol, false, 0),
                Task(DomesticCommand.Commerce, true, 0),
                Task(DomesticCommand.Cultivate, true, 0));
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 10)) },
                new[] { Corps(1, 500) });
            using (SingleCommandShadowRun run = Start(configuration, "custom", provider))
            {
                AssertEx.Equal(2, run.TaskCount, "Disabled task must not retain an ordinal.");
                AssertEx.SequenceEqual(BasicCommands(), RunToCompletion(run).Select(item => item.Command),
                    "Enabled task order is wrong.");
            }
        }

        private static void GreySkipContinuesWithoutConsumption()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(10, 5)) },
                new[] { Corps(1, 250) });
            provider.SetRule(1, DomesticCommand.Commerce, request => provider.CreateObservation(
                request,
                canExecute: false,
                blockReason: NativeCommandBlockReason.GreyedOut));
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                ShadowStepResult skipped = EvaluateCurrent(run);
                AssertEx.Equal(ShadowSkipReason.GreyedOut, skipped.SkipReason, "Grey reason is wrong.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count, "Grey skip consumed officers.");
                AssertEx.Equal(250, run.GetRemainingMoney(1), "Grey skip consumed money.");

                ShadowStepResult next = EvaluateCurrent(run);
                AssertEx.True(next.IsValidated, "Cultivate must continue after grey commerce.");
                AssertEx.SequenceEqual(Officers(10, 5), next.ValidatedCommand.OfficerIds,
                    "Skip did not preserve ranked officers.");
                Commit(run, next.ValidatedCommand);
            }
        }

        private static void DelegatedCitySkips()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, false, Officers(0, 5)) },
                new[] { Corps(1, 500) });
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                AssertEx.Equal(ShadowSkipReason.DelegatedCity, EvaluateCurrent(run).SkipReason,
                    "First delegated skip is wrong.");
                AssertEx.Equal(ShadowSkipReason.DelegatedCity, EvaluateCurrent(run).SkipReason,
                    "Second delegated skip is wrong.");
                AssertEx.Equal(0, provider.ObserveCount, "Delegated city must not request command reads.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count, "Delegated skip consumed officers.");
                AssertEx.Equal(500, run.GetRemainingMoney(1), "Delegated skip consumed money.");
            }
        }

        private static void ForeignCitySkips()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 2, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 500) },
                1);
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                AssertEx.Equal(ShadowSkipReason.NotOwnedByPlayer, EvaluateCurrent(run).SkipReason,
                    "Foreign city skip is wrong.");
                AssertEx.Equal(ShadowSkipReason.NotOwnedByPlayer, EvaluateCurrent(run).SkipReason,
                    "Foreign city second skip is wrong.");
                AssertEx.Equal(0, provider.ObserveCount, "Foreign city must not request command reads.");
            }
        }

        private static void UnderFiveSkipContinues()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 250) });
            provider.SetRule(1, DomesticCommand.Commerce, request => provider.CreateObservation(
                request,
                rankedCandidateOfficerIds: Officers(0, 4)));
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                AssertEx.Equal(ShadowSkipReason.InsufficientOfficers, EvaluateCurrent(run).SkipReason,
                    "Under-five skip is wrong.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count, "Under-five skip consumed officers.");
                ShadowStepResult next = EvaluateCurrent(run);
                AssertEx.True(next.IsValidated, "Later task must still see five officers.");
                Commit(run, next.ValidatedCommand);
            }
        }

        private static void InsufficientFundsPreservesOfficers()
        {
            DomesticCommand[] tasks = { DomesticCommand.Commerce, DomesticCommand.Train };
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 200) });
            using (SingleCommandShadowRun run = Start(Config("custom", tasks, 0), "custom", provider))
            {
                AssertEx.Equal(ShadowSkipReason.InsufficientFunds, EvaluateCurrent(run).SkipReason,
                    "Insufficient-funds reason is wrong.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count, "Funds skip consumed officers.");
                ShadowStepResult train = EvaluateCurrent(run);
                AssertEx.True(train.IsValidated, "Zero-cost task must continue after funds skip.");
                AssertEx.SequenceEqual(Officers(0, 5), train.ValidatedCommand.OfficerIds,
                    "Funds skip did not preserve officers.");
                Commit(run, train.ValidatedCommand);
                AssertEx.Equal(200, run.GetRemainingMoney(1), "Zero-cost commit changed money.");
            }
        }

        private static void CommitDepletionMakesLaterTaskSkip()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 500) });
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                ShadowStepResult commerce = EvaluateCurrent(run);
                AssertEx.True(commerce.IsValidated, "First task should validate with five officers.");
                Commit(run, commerce.ValidatedCommand);
                AssertEx.Equal(0, run.GetRemainingOfficerIds(1).Count,
                    "Commit did not consume the five officers.");

                ShadowStepResult cultivate = EvaluateCurrent(run);
                AssertEx.Equal(ShadowSkipReason.InsufficientOfficers, cultivate.SkipReason,
                    "Later task must skip after prior commit exhausts officers.");
                AssertEx.Equal(250, run.GetRemainingMoney(1),
                    "Officer-shortage skip consumed the later command cost.");
                AssertEx.Equal(ShadowSliceState.Completed, run.State,
                    "Run did not advance after the later skip.");
            }
        }

        private static void ReserveBoundaryPreservesOfficers()
        {
            DomesticCommand[] tasks = { DomesticCommand.Commerce, DomesticCommand.Train };
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 400) });
            using (SingleCommandShadowRun run = Start(Config("custom", tasks, 200), "custom", provider))
            {
                AssertEx.Equal(ShadowSkipReason.ReserveMoneyProtected, EvaluateCurrent(run).SkipReason,
                    "Reserve skip reason is wrong.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count, "Reserve skip consumed officers.");
                ShadowStepResult train = EvaluateCurrent(run);
                AssertEx.True(train.IsValidated, "Train must continue after reserve skip.");
                Commit(run, train.ValidatedCommand);
                AssertEx.Equal(400, run.GetRemainingMoney(1), "Train changed shared money.");
            }
        }

        private static void SharedCorpsFunds()
        {
            DomesticCommand[] tasks = { DomesticCommand.Commerce };
            FakeShadowObservationProvider provider = Provider(
                new[]
                {
                    City(7, 1, 1, true, Officers(20, 5)),
                    City(2, 1, 1, true, Officers(0, 5))
                },
                new[] { Corps(1, 250) });
            using (SingleCommandShadowRun run = Start(Config("custom", tasks, 0), "custom", provider))
            {
                ShadowStepResult first = EvaluateCurrent(run);
                AssertEx.True(first.IsValidated, "First city should validate.");
                Commit(run, first.ValidatedCommand);
                AssertEx.Equal(0, run.GetRemainingMoney(1), "First city did not consume shared money.");

                ShadowStepResult second = EvaluateCurrent(run);
                AssertEx.Equal(ShadowSkipReason.InsufficientFunds, second.SkipReason,
                    "Second city must see depleted shared corps funds.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(7).Count,
                    "Shared-funds skip consumed second-city officers.");
            }
        }

        private static void CommitIsOnlyResourceConsumptionBoundary()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 250) });
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                ShadowStepResult result = EvaluateCurrent(run);
                AssertEx.True(result.IsValidated, "Command did not validate.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count,
                    "Validation consumed officers before commit.");
                AssertEx.Equal(250, run.GetRemainingMoney(1),
                    "Validation consumed money before commit.");
                ShadowCommitReceipt receipt = Commit(run, result.ValidatedCommand);
                AssertEx.Equal(0, receipt.RemainingCityOfficerCount, "Commit did not consume five officers.");
                AssertEx.Equal(0, receipt.RemainingCorpsMoney, "Commit did not consume command cost.");
                AssertEx.False(receipt.NativeSubmissionPerformed, "Shadow commit must not submit natively.");
            }
        }

        private static void DuplicateTicketHalts()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 250) });
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                SingleCommandSliceTicket ticket = Issue(run);
                AssertEx.True(run.Evaluate(ticket).IsValidated, "First ticket use should validate.");
                ShadowStepResult duplicate = run.Evaluate(ticket);
                AssertEx.True(duplicate.IsHalted, "Duplicate ticket must halt.");
                AssertEx.Equal(ShadowHaltReason.TicketAlreadyConsumed, duplicate.HaltReason,
                    "Duplicate-ticket halt reason is wrong.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count,
                    "Duplicate handling consumed officers.");
                AssertEx.Equal(250, run.GetRemainingMoney(1), "Duplicate handling consumed funds.");
            }
        }

        private static void OutOfOrderTicketHalts()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 10)) },
                new[] { Corps(1, 500) });
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                SingleCommandSliceTicket ticket;
                ShadowTicketIssueRejection rejection;
                AssertEx.False(run.TryIssueTicketFor(0, 1, out ticket, out rejection),
                    "Later task ordinal must not issue.");
                AssertEx.Equal(ShadowTicketIssueRejection.OutOfOrder, rejection,
                    "Out-of-order rejection is wrong.");
                AssertEx.Equal(ShadowSliceState.Halted, run.State, "Out-of-order request must fail-close.");
                AssertEx.Equal(0, provider.ObserveCount, "Out-of-order request must not read a command.");
            }
        }

        private static void CrossRunTicketHalts()
        {
            DomesticConfiguration configuration = Config(
                "custom", new[] { DomesticCommand.Commerce }, 0);
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 250) });
            SingleCommandSliceTicket oldTicket;
            using (SingleCommandShadowRun first = Start(configuration, "custom", provider))
            {
                oldTicket = Issue(first);
                first.Stop();
            }

            using (SingleCommandShadowRun second = Start(configuration, "custom", provider))
            {
                Issue(second);
                ShadowStepResult result = second.Evaluate(oldTicket);
                AssertEx.True(result.IsHalted, "Cross-run ticket must halt.");
                AssertEx.Equal(ShadowHaltReason.TicketMismatch, result.HaltReason,
                    "Cross-run ticket reason is wrong.");
            }
        }

        private static void OutstandingWrongOrdinalHalts()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 10)) },
                new[] { Corps(1, 500) });
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                Issue(run);
                SingleCommandSliceTicket later;
                ShadowTicketIssueRejection rejection;
                AssertEx.False(run.TryIssueTicketFor(0, 1, out later, out rejection),
                    "Wrong ordinal issued while work was outstanding.");
                AssertEx.Equal(ShadowTicketIssueRejection.OutOfOrder, rejection,
                    "Wrong ordinal must win over generic outstanding-work rejection.");
                AssertEx.Equal(ShadowSliceState.Halted, run.State,
                    "Wrong ordinal with outstanding work did not fail-close.");
            }
        }

        private static void SameOrdinalReportsOutstandingWork()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 10)) },
                new[] { Corps(1, 500) });
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                Issue(run);
                SingleCommandSliceTicket duplicate;
                ShadowTicketIssueRejection rejection;
                AssertEx.False(run.TryIssueTicketFor(0, 0, out duplicate, out rejection),
                    "Second ticket issued for the same outstanding ordinal.");
                AssertEx.Equal(ShadowTicketIssueRejection.OutstandingWork, rejection,
                    "Same ordinal should report outstanding work.");
                AssertEx.Equal(ShadowSliceState.TicketOutstanding, run.State,
                    "Same-ordinal query should not alter the outstanding operation.");
            }
        }

        private static void GenerationSwitchHalts()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 250) });
            provider.SetRule(1, DomesticCommand.Commerce, request => provider.CreateObservation(
                request,
                generationDigest: Digest(99)));
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                ShadowStepResult result = EvaluateCurrent(run);
                AssertEx.Equal(ShadowHaltReason.GenerationChanged, result.HaltReason,
                    "Generation change did not halt correctly.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count, "Generation halt consumed officers.");
            }
        }

        private static void ObservationMismatchHalts()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 250) });
            provider.SetRule(1, DomesticCommand.Commerce, request => provider.CreateObservation(
                request,
                cityId: 2));
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                AssertEx.Equal(ShadowHaltReason.ObservationMismatch, EvaluateCurrent(run).HaltReason,
                    "Wrong city identity did not halt.");
            }
        }

        private static void StopBeforeTicket()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                AssertEx.True(run.Stop(), "Stop should transition an active run.");
                AssertEx.Equal(ShadowSliceState.Stopped, run.State, "Stop state is wrong.");
                AssertEx.False(ShadowSingleCommandSlice.IsRunActive, "Stop did not release single-flight.");
            }
        }

        private static void StopWithOutstandingTicket()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                Issue(run);
                AssertEx.True(run.Stop(), "Stop with ticket should succeed.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count, "Stop consumed ticket officers.");
                AssertEx.Equal(250, run.GetRemainingMoney(1), "Stop consumed ticket funds.");
                AssertEx.Equal(0, provider.ObserveCount, "Stop unexpectedly evaluated ticket.");
            }
        }

        private static void StopAfterValidation()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                AssertEx.True(EvaluateCurrent(run).IsValidated, "Command did not validate.");
                AssertEx.True(run.Stop(), "Stop after validation should succeed.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count,
                    "Stop after validation committed officers.");
                AssertEx.Equal(250, run.GetRemainingMoney(1),
                    "Stop after validation committed funds.");
            }
        }

        private static void SingleFlightRejectsSecondRun()
        {
            DomesticConfiguration configuration = Config(
                "custom", new[] { DomesticCommand.Commerce }, 0);
            FakeShadowObservationProvider provider = SimpleProvider();
            using (SingleCommandShadowRun first = Start(configuration, "custom", provider))
            {
                SingleCommandShadowRun second;
                string detail;
                AssertEx.False(ShadowSingleCommandSlice.TryStart(
                    configuration, "custom", provider, out second, out detail),
                    "Second active run must be rejected.");
                AssertEx.True(second == null, "Rejected run must be null.");
                first.Stop();
                using (SingleCommandShadowRun third = Start(configuration, "custom", provider))
                {
                    AssertEx.True(third.ShadowOnly, "Restarted run lost shadow flag.");
                }
            }
        }

        private static void ConcurrentSingleFlight()
        {
            DomesticConfiguration configuration = Config(
                "custom", new[] { DomesticCommand.Commerce }, 0);
            FakeShadowObservationProvider provider = SimpleProvider();
            ConcurrentBag<SingleCommandShadowRun> winners = new ConcurrentBag<SingleCommandShadowRun>();
            int successCount = 0;
            Parallel.For(0, 16, delegate(int ignored)
            {
                SingleCommandShadowRun run;
                string detail;
                if (ShadowSingleCommandSlice.TryStart(
                    configuration, "custom", provider, out run, out detail))
                {
                    Interlocked.Increment(ref successCount);
                    winners.Add(run);
                }
            });

            AssertEx.Equal(1, successCount, "Concurrent starts must have exactly one winner.");
            foreach (SingleCommandShadowRun winner in winners)
            {
                winner.Dispose();
            }

            AssertEx.False(ShadowSingleCommandSlice.IsRunActive,
                "Concurrent winner did not release single-flight.");
        }

        private static void CompletionReleasesSingleFlight()
        {
            DomesticConfiguration configuration = Config(
                "custom", new[] { DomesticCommand.Train }, 0);
            FakeShadowObservationProvider provider = SimpleProvider(0);
            using (SingleCommandShadowRun first = Start(configuration, "custom", provider))
            {
                ShadowStepResult result = EvaluateCurrent(first);
                Commit(first, result.ValidatedCommand);
                AssertEx.Equal(ShadowSliceState.Completed, first.State, "Run did not complete.");
                AssertEx.False(ShadowSingleCommandSlice.IsRunActive,
                    "Completion did not release single-flight.");
                using (SingleCommandShadowRun second = Start(configuration, "custom", provider))
                {
                    AssertEx.True(second != null, "Second run did not start after completion.");
                }
            }
        }

        private static void ProfileFingerprintBinding()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            using (SingleCommandShadowRun run = Start(
                Config("named-profile", new[] { DomesticCommand.Commerce }, 0),
                "named-profile",
                provider))
            {
                AssertEx.Equal("named-profile", provider.CapturedProfileId,
                    "Provider did not receive profile id.");
                AssertEx.Equal(run.ConfigurationFingerprint, provider.CapturedFingerprint,
                    "Provider did not receive frozen fingerprint.");
                AssertEx.True(!string.IsNullOrWhiteSpace(provider.CapturedFingerprint),
                    "Frozen fingerprint is absent.");
            }
        }

        private static void NonExactFiveRejected()
        {
            DomesticConfiguration configuration = new DomesticConfiguration(
                1,
                new[]
                {
                    new ProfileConfiguration(
                        "bad",
                        "bad",
                        true,
                        new[]
                        {
                            new TaskConfiguration(
                                DomesticCommand.Commerce,
                                true,
                                4,
                                5,
                                false,
                                SelectionPolicy.NativeBest,
                                null)
                        })
                },
                CityScope.DirectCities,
                CityOrder.GameIdAscending,
                0,
                false);
            FakeShadowObservationProvider provider = SimpleProvider();
            AssertEx.Throws<NotSupportedException>(delegate
            {
                SingleCommandShadowRun run;
                string detail;
                ShadowSingleCommandSlice.TryStart(configuration, "bad", provider, out run, out detail);
            }, "Non-exact-five profile must be rejected.");
            AssertEx.False(ShadowSingleCommandSlice.IsRunActive,
                "Rejected profile acquired single-flight.");
            AssertEx.Equal(0, provider.CaptureCount, "Rejected profile reached fake provider.");
        }

        private static void ForeignCandidateHalts()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            provider.SetRule(1, DomesticCommand.Commerce, request => provider.CreateObservation(
                request,
                rankedCandidateOfficerIds: new[] { 0, 1, 2, 3, 99 }));
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                AssertEx.Equal(ShadowHaltReason.ObservationMismatch, EvaluateCurrent(run).HaltReason,
                    "Foreign candidate did not halt.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count,
                    "Foreign candidate halt consumed officers.");
            }
        }

        private static void NonGreyBlockSkips()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 250) });
            provider.SetRule(1, DomesticCommand.Commerce, request => provider.CreateObservation(
                request,
                canExecute: false,
                blockReason: NativeCommandBlockReason.CityInCombat));
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                AssertEx.Equal(ShadowSkipReason.CommandBlocked, EvaluateCurrent(run).SkipReason,
                    "Non-grey block reason is wrong.");
                ShadowStepResult next = EvaluateCurrent(run);
                AssertEx.True(next.IsValidated, "Later task did not continue after block.");
                Commit(run, next.ValidatedCommand);
            }
        }

        private static void CrossCityOfficerSeedRejected()
        {
            AssertEx.Throws<ArgumentException>(delegate
            {
                new ShadowBatchObservation(
                    1,
                    Digest(11),
                    Digest(22),
                    new[] { Corps(1, 500), Corps(2, 500) },
                    new[]
                    {
                        City(1, 1, 1, true, new[] { 1, 2, 3, 4, 5 }),
                        City(2, 1, 2, true, new[] { 5, 6, 7, 8, 9 })
                    });
            }, "Cross-city duplicate officer seed must be rejected.");
        }

        private static void ZeroCostTrainingAllowed()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 0) });
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Train }, 999), "custom", provider))
            {
                ShadowStepResult train = EvaluateCurrent(run);
                AssertEx.True(train.IsValidated, "Zero-cost train was blocked by reserve.");
                AssertEx.Equal(0, train.ValidatedCommand.ExpectedCost, "Train cost is not zero.");
                Commit(run, train.ValidatedCommand);
                AssertEx.Equal(0, run.GetRemainingMoney(1), "Train changed money.");
            }
        }

        private static void StableSequenceAcrossSkips()
        {
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, 250) });
            provider.SetRule(1, DomesticCommand.Commerce, request => provider.CreateObservation(
                request,
                canExecute: false,
                blockReason: NativeCommandBlockReason.GreyedOut));
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                ShadowStepResult first = EvaluateCurrent(run);
                AssertEx.Equal((ulong)1, first.Ticket.Sequence, "First ordinal sequence is wrong.");
                ShadowStepResult second = EvaluateCurrent(run);
                AssertEx.Equal((ulong)2, second.ValidatedCommand.V8Request.Sequence,
                    "Skip incorrectly compressed the later stable sequence.");
                Commit(run, second.ValidatedCommand);
            }
        }

        private static void ProviderExceptionHalts()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            provider.ThrowOnObserve = true;
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                AssertEx.Equal(ShadowHaltReason.ObservationFailure, EvaluateCurrent(run).HaltReason,
                    "Provider exception did not fail-close.");
                AssertEx.False(ShadowSingleCommandSlice.IsRunActive,
                    "Provider failure did not release single-flight.");
            }
        }

        private static void NoPublicCapabilityConstructors()
        {
            AssertEx.Equal(0, typeof(SingleCommandSliceTicket).GetConstructors().Length,
                "Ticket has a public constructor.");
            AssertEx.Equal(0, typeof(ValidatedSingleCommand).GetConstructors().Length,
                "Validated command has a public constructor.");
            AssertEx.Equal(0, typeof(ShadowCommitReceipt).GetConstructors().Length,
                "Receipt has a public constructor.");
            AssertEx.True(typeof(IShadowObservationProvider).IsInterface,
                "Synthetic provider contract must remain an interface.");
        }

        private static void CallbackStopCannotResurrect()
        {
            DomesticConfiguration configuration = Config(
                "custom", new[] { DomesticCommand.Commerce }, 0);
            FakeShadowObservationProvider firstProvider = SimpleProvider();
            FakeShadowObservationProvider secondProvider = SimpleProvider();
            SingleCommandShadowRun first = null;
            SingleCommandShadowRun second = null;
            bool callbackSawEvaluating = false;
            bool stopCompletedOutsideLock = false;
            bool secondStarted = false;
            firstProvider.SetRule(1, DomesticCommand.Commerce, delegate(ShadowCommandObservationRequest request)
            {
                callbackSawEvaluating = first.State == ShadowSliceState.Evaluating;
                System.Threading.Tasks.Task<bool> stopTask =
                    System.Threading.Tasks.Task.Factory.StartNew(delegate { return first.Stop(); });
                stopCompletedOutsideLock = stopTask.Wait(2000) && stopTask.Result;
                string detail;
                secondStarted = ShadowSingleCommandSlice.TryStart(
                    configuration,
                    "custom",
                    secondProvider,
                    out second,
                    out detail);
                return firstProvider.CreateObservation(request);
            });

            try
            {
                first = Start(configuration, "custom", firstProvider);
                ShadowStepResult outer = EvaluateCurrent(first);
                AssertEx.True(callbackSawEvaluating, "Callback did not observe explicit Evaluating state.");
                AssertEx.True(stopCompletedOutsideLock,
                    "Stop on another thread could not complete while provider callback was running.");
                AssertEx.True(secondStarted, "Second run did not start after callback stopped first run.");
                AssertEx.Equal(ShadowSliceState.Stopped, first.State,
                    "Outer callback return resurrected stopped run.");
                AssertEx.Equal(ShadowHaltReason.EvaluationInvalidated, outer.HaltReason,
                    "Outer evaluation did not report invalidated operation.");
                AssertEx.Equal(ShadowSliceState.Ready, second.State,
                    "Outer first-run return corrupted the coexisting second run.");
                AssertEx.True(ShadowSingleCommandSlice.IsRunActive,
                    "Coexisting second run lost the global slot.");
                AssertEx.Equal(5, first.GetRemainingOfficerIds(1).Count,
                    "Invalidated callback consumed first-run officers.");
                AssertEx.Equal(250, first.GetRemainingMoney(1),
                    "Invalidated callback consumed first-run money.");
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
        }

        private static void CallbackDisposeCannotResurrect()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            SingleCommandShadowRun run = null;
            provider.SetRule(1, DomesticCommand.Commerce, delegate(ShadowCommandObservationRequest request)
            {
                run.Dispose();
                return provider.CreateObservation(request);
            });
            try
            {
                run = Start(Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider);
                ShadowStepResult result = EvaluateCurrent(run);
                AssertEx.Equal(ShadowSliceState.Stopped, run.State,
                    "Dispose inside callback was resurrected.");
                AssertEx.Equal(ShadowHaltReason.EvaluationInvalidated, result.HaltReason,
                    "Disposed callback did not invalidate outer evaluation.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count,
                    "Disposed evaluation consumed officers.");
            }
            finally
            {
                if (run != null)
                {
                    run.Dispose();
                }
            }
        }

        private static void RecursiveEvaluateCannotResurrect()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            SingleCommandShadowRun run = null;
            SingleCommandSliceTicket ticket = null;
            ShadowStepResult nested = null;
            provider.SetRule(1, DomesticCommand.Commerce, delegate(ShadowCommandObservationRequest request)
            {
                nested = run.Evaluate(ticket);
                return provider.CreateObservation(request);
            });
            try
            {
                run = Start(Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider);
                ticket = Issue(run);
                ShadowStepResult outer = run.Evaluate(ticket);
                AssertEx.Equal(ShadowHaltReason.TicketAlreadyConsumed, nested.HaltReason,
                    "Recursive evaluation did not reject consumed ticket.");
                AssertEx.Equal(ShadowSliceState.Halted, run.State,
                    "Outer return resurrected recursively halted run.");
                AssertEx.Equal(ShadowHaltReason.TicketAlreadyConsumed, run.HaltReason,
                    "Run lost the original recursive halt reason.");
                AssertEx.Equal(ShadowHaltReason.EvaluationInvalidated, outer.HaltReason,
                    "Outer evaluation did not report invalidated operation.");
                AssertEx.Equal(250, run.GetRemainingMoney(1),
                    "Recursive evaluation consumed money.");
            }
            finally
            {
                if (run != null)
                {
                    run.Dispose();
                }
            }
        }

        private static void CallbackCommitCannotResurrect()
        {
            DomesticConfiguration configuration = Config(
                "custom", new[] { DomesticCommand.Commerce }, 0);
            ValidatedSingleCommand staleCommand;
            using (SingleCommandShadowRun preparation = Start(configuration, "custom", SimpleProvider()))
            {
                staleCommand = EvaluateCurrent(preparation).ValidatedCommand;
                preparation.Stop();
            }

            FakeShadowObservationProvider provider = SimpleProvider();
            SingleCommandShadowRun run = null;
            bool nestedCommitAccepted = true;
            provider.SetRule(1, DomesticCommand.Commerce, delegate(ShadowCommandObservationRequest request)
            {
                ShadowCommitReceipt ignored;
                nestedCommitAccepted = run.TryCommit(staleCommand, out ignored);
                return provider.CreateObservation(request);
            });
            try
            {
                run = Start(configuration, "custom", provider);
                ShadowStepResult outer = EvaluateCurrent(run);
                AssertEx.False(nestedCommitAccepted, "Foreign callback commit was accepted.");
                AssertEx.Equal(ShadowSliceState.Halted, run.State,
                    "Outer return resurrected commit-mismatch halt.");
                AssertEx.Equal(ShadowHaltReason.CommitMismatch, run.HaltReason,
                    "Run lost callback commit-mismatch reason.");
                AssertEx.Equal(ShadowHaltReason.EvaluationInvalidated, outer.HaltReason,
                    "Outer evaluation did not report invalidated operation.");
                AssertEx.Equal(5, run.GetRemainingOfficerIds(1).Count,
                    "Callback commit mismatch consumed officers.");
            }
            finally
            {
                if (run != null)
                {
                    run.Dispose();
                }
            }
        }

        private static void NontrivialRankingTakesFirstFive()
        {
            int[] ranking = { 7, 2, 9, 1, 8, 0, 3, 4, 5, 6 };
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 10)) },
                new[] { Corps(1, 250) });
            provider.SetRule(1, DomesticCommand.Commerce, request => provider.CreateObservation(
                request,
                rankedCandidateOfficerIds: ranking));
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                ShadowStepResult result = EvaluateCurrent(run);
                AssertEx.SequenceEqual(new[] { 7, 2, 9, 1, 8 }, result.ValidatedCommand.OfficerIds,
                    "Validator did not preserve the observation's nontrivial top-five order.");
            }
        }

        private static void ConsumedOfficersFilterRanking()
        {
            int[] secondRanking = { 4, 9, 3, 8, 2, 7, 1, 6, 0, 5 };
            FakeShadowObservationProvider provider = Provider(
                new[] { City(1, 1, 1, true, Officers(0, 10)) },
                new[] { Corps(1, 500) });
            provider.SetRule(1, DomesticCommand.Cultivate, request => provider.CreateObservation(
                request,
                rankedCandidateOfficerIds: secondRanking));
            using (SingleCommandShadowRun run = Start(Config("basic", BasicCommands(), 0), "basic", provider))
            {
                ShadowStepResult first = EvaluateCurrent(run);
                AssertEx.SequenceEqual(new[] { 0, 1, 2, 3, 4 }, first.ValidatedCommand.OfficerIds,
                    "First command ranking is wrong.");
                Commit(run, first.ValidatedCommand);

                ShadowStepResult second = EvaluateCurrent(run);
                AssertEx.SequenceEqual(new[] { 9, 8, 7, 6, 5 }, second.ValidatedCommand.OfficerIds,
                    "Consumed officers were not filtered while preserving fresh ranking.");
            }
        }

        private static void DuplicateCandidateRejected()
        {
            AssertEx.Throws<ArgumentException>(delegate
            {
                new ShadowCommandObservation(
                    Digest(22),
                    1,
                    1,
                    1,
                    true,
                    DomesticCommand.Commerce,
                    true,
                    NativeCommandBlockReason.None,
                    new[] { 1, 2, 3, 3, 4 });
            }, "Duplicate ranked candidate ids must be rejected at the observation boundary.");
        }

        private static void NoPInvokeOrLiveReferences()
        {
            Assembly assembly = typeof(ShadowSingleCommandSlice).Assembly;
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                {
                    AssertEx.False(method.IsDefined(typeof(DllImportAttribute), false),
                        "P/Invoke found: " + type.FullName + "." + method.Name);
                }
            }

            string[] references = assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();
            AssertEx.False(references.Any(item => item.IndexOf("Adapter", StringComparison.OrdinalIgnoreCase) >= 0),
                "Slice unexpectedly references a game adapter.");
            AssertEx.False(references.Any(item => item.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) >= 0),
                "Slice unexpectedly references a bridge/IPC assembly.");
            AssertEx.SequenceEqual(
                new[]
                {
                    "San9AutoDomestic.Core",
                    "San9AutoDomestic.V8Transaction",
                    "mscorlib",
                    "System.Core"
                }.OrderBy(item => item),
                references.OrderBy(item => item),
                "Unexpected assembly reference expands the shadow boundary.");
        }

        private static void V8RequestRemainsNonAuthorizing()
        {
            FakeShadowObservationProvider provider = SimpleProvider();
            using (SingleCommandShadowRun run = Start(
                Config("custom", new[] { DomesticCommand.Commerce }, 0), "custom", provider))
            {
                ShadowStepResult result = EvaluateCurrent(run);
                AssertEx.True(result.ValidatedCommand.ShadowOnly, "Validated command lost shadow marking.");
                AssertEx.False(result.ValidatedCommand.LiveAuthorized,
                    "Validated command claims live authorization.");
                AssertEx.False(result.ValidatedCommand.V8Request.AuthorizesLiveMutation,
                    "V8 request claims mutation authority.");
                AssertEx.Equal(result.Ticket.TicketId, result.ValidatedCommand.V8Request.RequestId,
                    "V8 request did not bind the one-use ticket id.");
                AssertEx.True(result.ValidatedCommand.V8Request.ContextDigest.Equals(provider.Batch.ContextDigest),
                    "V8 request context digest is wrong.");
                AssertEx.True(result.ValidatedCommand.V8Request.GenerationDigest.Equals(provider.Batch.GenerationDigest),
                    "V8 request generation digest is wrong.");
            }
        }

        private static SingleCommandShadowRun Start(
            DomesticConfiguration configuration,
            string profileId,
            FakeShadowObservationProvider provider)
        {
            SingleCommandShadowRun run;
            string detail;
            AssertEx.True(ShadowSingleCommandSlice.TryStart(
                configuration, profileId, provider, out run, out detail),
                "Shadow run did not start: " + detail);
            AssertEx.True(run.ShadowOnly, "Run must be shadow-only.");
            AssertEx.False(run.LiveAuthorized, "Run must not authorize live execution.");
            return run;
        }

        private static SingleCommandSliceTicket Issue(SingleCommandShadowRun run)
        {
            SingleCommandSliceTicket ticket;
            ShadowTicketIssueRejection rejection;
            AssertEx.True(run.TryIssueCurrentTicket(out ticket, out rejection),
                "Current ticket did not issue: " + rejection);
            AssertEx.True(ticket.ShadowOnly, "Ticket lost shadow marking.");
            AssertEx.False(ticket.LiveAuthorized, "Ticket claims live authority.");
            return ticket;
        }

        private static ShadowStepResult EvaluateCurrent(SingleCommandShadowRun run)
        {
            return run.Evaluate(Issue(run));
        }

        private static ShadowCommitReceipt Commit(
            SingleCommandShadowRun run,
            ValidatedSingleCommand command)
        {
            ShadowCommitReceipt receipt;
            AssertEx.True(run.TryCommit(command, out receipt), "Shadow commit was rejected.");
            AssertEx.True(receipt.ShadowOnly, "Receipt lost shadow marking.");
            AssertEx.False(receipt.LiveAuthorized, "Receipt claims live authority.");
            return receipt;
        }

        private static List<ValidatedSingleCommand> RunToCompletion(SingleCommandShadowRun run)
        {
            List<ValidatedSingleCommand> commands = new List<ValidatedSingleCommand>();
            while (run.State == ShadowSliceState.Ready)
            {
                ShadowStepResult result = EvaluateCurrent(run);
                if (result.IsValidated)
                {
                    commands.Add(result.ValidatedCommand);
                    Commit(run, result.ValidatedCommand);
                }
                else
                {
                    AssertEx.True(result.IsSkipped, "Run halted unexpectedly: " + result.Detail);
                }
            }

            AssertEx.Equal(ShadowSliceState.Completed, run.State, "Run did not reach completion.");
            return commands;
        }

        private static DomesticConfiguration Config(
            string profileId,
            IEnumerable<DomesticCommand> commands,
            int reserveMoney)
        {
            return ConfigWithTasks(
                profileId,
                reserveMoney,
                commands.Select(command => Task(command, true, null)).ToArray());
        }

        private static DomesticConfiguration ConfigWithTasks(
            string profileId,
            int reserveMoney,
            params TaskConfiguration[] tasks)
        {
            return new DomesticConfiguration(
                1,
                new[]
                {
                    new ProfileConfiguration(profileId, profileId, true, tasks)
                },
                CityScope.DirectCities,
                CityOrder.GameIdAscending,
                reserveMoney,
                false);
        }

        private static TaskConfiguration Task(
            DomesticCommand command,
            bool enabled,
            int? reserveOverride)
        {
            return new TaskConfiguration(
                command,
                enabled,
                5,
                5,
                true,
                SelectionPolicy.NativeBest,
                reserveOverride);
        }

        private static DomesticCommand[] BasicCommands()
        {
            return new[] { DomesticCommand.Commerce, DomesticCommand.Cultivate };
        }

        private static DomesticCommand[] WealthyCommands()
        {
            return new[]
            {
                DomesticCommand.Patrol,
                DomesticCommand.Commerce,
                DomesticCommand.Cultivate,
                DomesticCommand.Train,
                DomesticCommand.Repair
            };
        }

        private static FakeShadowObservationProvider SimpleProvider(int money = 250)
        {
            return Provider(
                new[] { City(1, 1, 1, true, Officers(0, 5)) },
                new[] { Corps(1, money) });
        }

        private static FakeShadowObservationProvider Provider(
            IEnumerable<ShadowCitySeed> cities,
            IEnumerable<ShadowCorpsSeed> corps,
            int playerForceId = 1)
        {
            return new FakeShadowObservationProvider(new ShadowBatchObservation(
                playerForceId,
                Digest(11),
                Digest(22),
                corps,
                cities));
        }

        private static ShadowCitySeed City(
            int cityId,
            int ownerForceId,
            int corpsId,
            bool directlyControlled,
            IEnumerable<int> officers)
        {
            return new ShadowCitySeed(
                cityId,
                ownerForceId,
                corpsId,
                directlyControlled,
                officers);
        }

        private static ShadowCorpsSeed Corps(int corpsId, int money)
        {
            return new ShadowCorpsSeed(corpsId, money);
        }

        private static int[] Officers(int first, int count)
        {
            return Enumerable.Range(first, count).ToArray();
        }

        private static FixedDigest Digest(byte value)
        {
            byte[] bytes = new byte[FixedDigest.ByteCount];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = value;
            }

            return FixedDigest.Create(bytes);
        }
    }
}
