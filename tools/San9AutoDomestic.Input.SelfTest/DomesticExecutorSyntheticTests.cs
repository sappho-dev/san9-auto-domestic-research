using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Input.Win32;

namespace San9AutoDomestic.Input.SelfTest
{
    internal static class DomesticExecutorSyntheticTests
    {
        private const int ProcessId = 37692;
        private const long ProcessCreationFileTimeUtc = 639218891755456732;
        private const long MainWindowHandle = 0x00120ED8;
        private const int CityId = 46;

        internal static void Register(Action<string, Action> run)
        {
            run("domestic executor commits exact five for exact 250 cost", ExactFiveAndExactCostComplete);
            run("domestic executor commits Train for zero cost", TrainZeroCostCompletes);
            run("domestic executor skips grey command without input", GreyCommandSendsNoInput);
            run("domestic executor skips fewer than five without input", FewerThanFiveSendsNoInput);
            run("domestic executor skips insufficient money without input", InsufficientMoneySendsNoInput);
            run("domestic executor rejects wrong surface root before command click", WrongSurfaceRootRejectsBeforeCommandClick);
            run("domestic executor rejects unverified surface hover before command click", UnverifiedSurfaceHoverRejectsBeforeCommandClick);
            run("domestic executor aborts on wrong selector list", WrongSelectorListAborts);
            run("domestic executor aborts on wrong post-commit money", WrongPostCommitMoneyAborts);
            run("domestic executor aborts on wrong post-commit ready count", WrongPostCommitReadyCountAborts);
            run("domestic executor aborts when post-commit order stays clear", WrongPostCommitOrderAborts);
            run("domestic executor polls unchanged post-commit state to success", OldStateThenCommittedStateCompletes);
            run("domestic executor rejects technically invalid post availability", InvalidPostAvailabilityAborts);
            run("domestic executor times out unchanged without retrying commit", UnchangedPostCommitStateTimesOutWithoutRetry);
            run("domestic executor does not retry attempted hover failure", AttemptedHoverFailureIsNotRetried);
            run("domestic executor does not retry attempted input failure", InputFailureIsNotRetried);
            run("domestic executor stops before input", StopBeforeInputSendsNoInput);
        }

        private static void ExactFiveAndExactCostComplete()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal("SINGLE_COMMAND_COMPLETED", result.Code);
                Equal(CityId, result.CityId.Value);
                Equal(1000, result.MoneyBefore.Value);
                Equal(750, result.MoneyAfter.Value);
                Equal(8, result.ReadyBefore.Value);
                Equal(3, result.ReadyAfter.Value);
                SequenceEqual(new[] { 108, 103, 106, 101, 105 }, result.SelectedOfficerIds);
                True(result.CommitClickAttempted);
                Equal(7, result.InputActionCount);
                Equal(7, fixture.Transport.InputCallCount);
                Equal(1, fixture.Transport.HoverCallCount);
                Equal(5, fixture.Transport.ClickCallCount);
                Equal(1, fixture.Transport.ReturnCallCount);
                Equal(7, result.Evidence.Count);
            }
        }

        private static void TrainZeroCostCompletes()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Train, 0))
            {
                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal(DomesticCommandKind.Train, result.Command);
                Equal(1000, result.MoneyBefore.Value);
                Equal(1000, result.MoneyAfter.Value);
                SequenceEqual(new[] { 108, 103, 106, 101, 105 }, result.SelectedOfficerIds);
                Equal(7, fixture.Transport.InputCallCount);
                Equal(1, fixture.Transport.HoverCallCount);
                DomesticExecutionPoint commerce = DomesticExecutionCoordinates.ForCommand(DomesticCommandKind.Commerce);
                Equal(commerce.X, fixture.Transport.LastHoverX);
                Equal(commerce.Y, fixture.Transport.LastHoverY);
            }
        }

        private static void GreyCommandSendsNoInput()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.Before.KnownStaticSubsetWouldPass = false;

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.SkippedUnavailable, result.Status);
                Equal("COMMAND_UNAVAILABLE", result.Code);
                Equal(0, result.InputActionCount);
                Equal(0, fixture.Transport.InputCallCount);
                Equal(0, fixture.Transport.HoverCallCount);
                Equal(0, fixture.Transport.ActivationCount);
                Equal(0, fixture.Transport.ScreenshotCount);
                Equal(0, result.Evidence.Count);
                True(!result.CommitClickAttempted);
            }
        }

        private static void FewerThanFiveSendsNoInput()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.SetReadyBefore(
                    new[] { 101, 102, 103, 104 },
                    new[] { 104, 102, 101, 103 });

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.SkippedFewerThanFive, result.Status);
                Equal("FEWER_THAN_FIVE_READY", result.Code);
                Equal(0, result.InputActionCount);
                Equal(0, fixture.Transport.InputCallCount);
                Equal(0, fixture.Transport.HoverCallCount);
                Equal(0, fixture.Transport.ActivationCount);
                Equal(0, fixture.Transport.ScreenshotCount);
                Equal(0, result.Evidence.Count);
                True(!result.CommitClickAttempted);
            }
        }

        private static void InsufficientMoneySendsNoInput()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.Before.CorpsMoney = 249;

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.SkippedUnavailable, result.Status);
                Equal("INSUFFICIENT_MONEY_FOR_FIVE", result.Code);
                Equal(0, result.InputActionCount);
                Equal(0, fixture.Transport.InputCallCount);
                Equal(0, fixture.Transport.HoverCallCount);
                Equal(0, fixture.Transport.ClickCallCount);
                Equal(0, fixture.Transport.ActivationCount);
                Equal(0, fixture.Transport.ScreenshotCount);
                Equal(1, fixture.Source.UiCaptureCount);
                Equal(1, fixture.Source.AvailabilityCaptureCount);
                Equal(0, result.Evidence.Count);
                True(!result.CommitClickAttempted);
            }
        }

        private static void WrongSurfaceRootRejectsBeforeCommandClick()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.Surface.Layer = UiLayerKind.StrategicMapCandidate;
                AssertSurfaceProbeReject(fixture);
            }
        }

        private static void UnverifiedSurfaceHoverRejectsBeforeCommandClick()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.Surface.HoveredCommandState = UiObservationState.Unknown;
                fixture.Surface.HoveredCommand = string.Empty;
                AssertSurfaceProbeReject(fixture);
            }
        }

        private static void AssertSurfaceProbeReject(Fixture fixture)
        {
            SingleCityDomesticExecutionResult result = fixture.Execute();

            Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
            Equal("SURFACE_PROBE_REJECTED", result.Code);
            Equal(1, result.InputActionCount);
            Equal(1, fixture.Transport.InputCallCount);
            Equal(1, fixture.Transport.HoverCallCount);
            Equal(0, fixture.Transport.ClickCallCount);
            Equal(0, fixture.Transport.ReturnCallCount);
            Equal(1, fixture.Transport.ActivationCount);
            Equal(2, fixture.Transport.ScreenshotCount);
            Equal(2, fixture.Source.UiCaptureCount);
            Equal(2, fixture.Source.AvailabilityCaptureCount);
            Equal(1, result.Evidence.Count);
            True(!result.CommitClickAttempted);
        }

        private static void WrongSelectorListAborts()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.Selected.SelectedOfficerIds = new[] { 108, 103, 106, 101, 102 };

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("EXACT_FIVE_NOT_SELECTED", result.Code);
                Equal(4, result.InputActionCount);
                Equal(4, fixture.Transport.InputCallCount);
                Equal(1, fixture.Transport.HoverCallCount);
                Equal(3, fixture.Transport.ClickCallCount);
                True(!result.CommitClickAttempted);
            }
        }

        private static void WrongPostCommitMoneyAborts()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.After.CorpsMoney = 751;
                AssertCommitPostconditionAbort(fixture);
            }
        }

        private static void WrongPostCommitReadyCountAborts()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.After.ReadyCandidatesInSourceOrder = new[] { 102, 104, 107, 109 };
                fixture.After.RankedCandidates = new[] { 109, 107, 104, 102 };
                AssertCommitPostconditionAbort(fixture);
            }
        }

        private static void WrongPostCommitOrderAborts()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.After.OrderBitClearPassed = true;
                AssertCommitPostconditionAbort(fixture);
            }
        }

        private static void AssertCommitPostconditionAbort(Fixture fixture)
        {
            SingleCityDomesticExecutionResult result = fixture.Execute();

            Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
            Equal("COMMIT_POSTCONDITION_MISMATCH", result.Code);
            True(result.CommitClickAttempted);
            Equal(6, result.InputActionCount);
            Equal(6, fixture.Transport.InputCallCount);
            Equal(1, fixture.Transport.HoverCallCount);
            Equal(5, fixture.Transport.ClickCallCount);
            Equal(3, fixture.Source.AvailabilityCaptureCount);
            Equal(0, fixture.DelayCalls.Count(value => value == 250));
        }

        private static void OldStateThenCommittedStateCompletes()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.SetPostAvailabilitySequence(fixture.Before, fixture.After);

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal("SINGLE_COMMAND_COMPLETED", result.Code);
                Equal(4, fixture.Source.AvailabilityCaptureCount);
                Equal(1, fixture.Transport.HoverCallCount);
                Equal(5, fixture.Transport.ClickCallCount);
                Equal(1, fixture.Transport.ReturnCallCount);
                Equal(7, fixture.Transport.InputCallCount);
                Equal(7, result.InputActionCount);
                Equal(1, fixture.DelayCalls.Count(value => value == 500));
                Equal(1, fixture.DelayCalls.Count(value => value == 250));
                Equal("UAAUUUUUUAAU", string.Join("", fixture.Source.Events.ToArray()));
            }
        }

        private static void InvalidPostAvailabilityAborts()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.After.IsValid = false;
                fixture.After.Error = "Synthetic transition-invalid availability.";

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("POST_AVAILABILITY_REJECTED", result.Code);
                True(result.CommitClickAttempted);
                Equal(6, result.InputActionCount);
                Equal(1, fixture.Transport.HoverCallCount);
                Equal(5, fixture.Transport.ClickCallCount);
                Equal(0, fixture.Transport.ReturnCallCount);
                Equal(6, fixture.Transport.InputCallCount);
                Equal(3, fixture.Source.AvailabilityCaptureCount);
                Equal(7, fixture.Source.UiCaptureCount);
                Equal("UAAUUUUUUA", string.Join("", fixture.Source.Events.ToArray()));
            }
        }

        private static void UnchangedPostCommitStateTimesOutWithoutRetry()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.SetPostAvailabilitySequence(
                    Enumerable.Repeat(fixture.Before, 19).ToArray());

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("COMMIT_POSTCONDITION_TIMEOUT", result.Code);
                True(result.CommitClickAttempted);
                Equal(6, result.InputActionCount);
                Equal(1, fixture.Transport.HoverCallCount);
                Equal(5, fixture.Transport.ClickCallCount);
                Equal(0, fixture.Transport.ReturnCallCount);
                Equal(6, fixture.Transport.InputCallCount);
                Equal(21, fixture.Source.AvailabilityCaptureCount);
                Equal(7, fixture.Source.UiCaptureCount);
                Equal(1, fixture.DelayCalls.Count(value => value == 500));
                Equal(18, fixture.DelayCalls.Count(value => value == 250));
            }
        }

        private static void AttemptedHoverFailureIsNotRetried()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.Transport.FailInputOrdinal = 1;
                fixture.Transport.AttemptFailedInput = true;

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("SURFACE_PROBE_HOVER_FAILED", result.Code);
                Equal(1, result.InputActionCount);
                Equal(1, fixture.Transport.InputCallCount);
                Equal(1, fixture.Transport.HoverCallCount);
                Equal(0, fixture.Transport.ClickCallCount);
                Equal(0, fixture.Transport.ReturnCallCount);
                Equal(1, fixture.Transport.ActivationCount);
                Equal(2, fixture.Transport.ScreenshotCount);
                Equal(1, fixture.Source.UiCaptureCount);
                Equal(2, fixture.Source.AvailabilityCaptureCount);
                Equal(1, result.Evidence.Count);
                True(!result.CommitClickAttempted);
            }
        }

        private static void InputFailureIsNotRetried()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                fixture.Transport.FailInputOrdinal = 2;
                fixture.Transport.AttemptFailedInput = true;

                SingleCityDomesticExecutionResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("OPEN_COMMAND_FAILED", result.Code);
                Equal(2, result.InputActionCount);
                Equal(2, fixture.Transport.InputCallCount);
                Equal(1, fixture.Transport.HoverCallCount);
                Equal(1, fixture.Transport.ClickCallCount);
                Equal(2, fixture.Transport.ActivationCount);
                Equal(2, fixture.Source.UiCaptureCount);
                Equal(2, fixture.Source.AvailabilityCaptureCount);
                Equal(2, result.Evidence.Count);
                True(!result.CommitClickAttempted);
            }
        }

        private static void StopBeforeInputSendsNoInput()
        {
            using (Fixture fixture = Fixture.Valid(DomesticCommandKind.Patrol, 50))
            {
                DomesticExecutionStopSignal stop = new DomesticExecutionStopSignal();
                stop.RequestStop();

                SingleCityDomesticExecutionResult result = fixture.Execute(stop);

                Equal(DomesticExecutionStatus.StoppedBeforeCommit, result.Status);
                Equal("STOPPED_BEFORE_INPUT", result.Code);
                Equal(0, result.InputActionCount);
                Equal(0, fixture.Transport.InputCallCount);
                Equal(0, fixture.Transport.ActivationCount);
                Equal(0, fixture.Transport.ScreenshotCount);
                Equal(1, fixture.Source.UiCaptureCount);
                Equal(1, fixture.Source.AvailabilityCaptureCount);
                Equal(0, result.Evidence.Count);
                True(!result.CommitClickAttempted);
            }
        }

        private static void True(bool value)
        {
            if (!value) throw new InvalidOperationException("Expected true.");
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(string.Format("Expected {0}, got {1}.", expected, actual));
        }

        private static void SequenceEqual(IEnumerable<int> expected, IEnumerable<int> actual)
        {
            int[] expectedValues = expected.ToArray();
            int[] actualValues = actual.ToArray();
            if (!expectedValues.SequenceEqual(actualValues))
                throw new InvalidOperationException(string.Format(
                    "Expected [{0}], got [{1}].",
                    string.Join(",", expectedValues.Select(value => value.ToString()).ToArray()),
                    string.Join(",", actualValues.Select(value => value.ToString()).ToArray())));
        }

        private sealed class Fixture : IDisposable
        {
            private static readonly int[] ReadyBeforeIds =
                { 101, 102, 103, 104, 105, 106, 107, 108 };
            private static readonly int[] RankedBeforeIds =
                { 108, 103, 106, 101, 105, 102, 107, 104 };
            private static readonly int[] ExpectedFive =
                { 108, 103, 106, 101, 105 };
            private static readonly int[] ReadyAfterIds =
                { 102, 104, 107 };
            private static readonly int[] RankedAfterIds =
                { 107, 102, 104 };

            internal FakeObservationSource Source;
            internal FakeDomesticActionTransport Transport;
            internal SingleCityDomesticExecutor Executor;
            internal SingleCityDomesticExecutionRequest Request;
            internal DomesticAvailabilitySnapshot Before;
            internal DomesticAvailabilitySnapshot After;
            internal DomesticUiSnapshot Surface;
            internal DomesticUiSnapshot Outer;
            internal DomesticUiSnapshot Selector;
            internal DomesticUiSnapshot Selected;
            internal DomesticUiSnapshot Confirmation;
            internal List<int> DelayCalls;

            private string evidenceDirectory;

            internal static Fixture Valid(DomesticCommandKind command, int costPerOfficer)
            {
                DomesticAvailabilitySnapshot before = Availability(
                    command,
                    1000,
                    costPerOfficer,
                    true,
                    true,
                    ReadyBeforeIds,
                    RankedBeforeIds);
                DomesticAvailabilitySnapshot after = Availability(
                    command,
                    1000 - costPerOfficer * 5,
                    costPerOfficer,
                    false,
                    false,
                    ReadyAfterIds,
                    RankedAfterIds);

                DomesticUiSnapshot menu = Ui(UiLayerKind.DomesticCommandMenu);
                DomesticUiSnapshot surface = Ui(UiLayerKind.DomesticCommandMenu);
                surface.HoveredCommandState = UiObservationState.Verified;
                surface.HoveredCommand = DomesticExecutionCoordinates.CommandName(DomesticCommandKind.Commerce);
                DomesticUiSnapshot outer = Ui(UiLayerKind.DomesticOuterDialog);
                outer.OuterCommandName = DomesticExecutionCoordinates.CommandName(command);
                outer.CandidateOfficerIds = ReadyBeforeIds.ToArray();
                DomesticUiSnapshot selector = Ui(UiLayerKind.OfficerSelector);
                selector.CandidateOfficerIds = RankedBeforeIds.ToArray();
                DomesticUiSnapshot selected = Ui(UiLayerKind.OfficerSelector);
                selected.CandidateOfficerIds = RankedBeforeIds.ToArray();
                selected.SelectedOfficerIds = ExpectedFive.ToArray();
                DomesticUiSnapshot confirmation = Ui(UiLayerKind.DomesticConfirmation);
                confirmation.WorkingOfficerIds = ExpectedFive.ToArray();
                DomesticUiSnapshot result = Ui(UiLayerKind.StrategicMapCandidate);
                result.DomesticTargetCityId = CityId;
                DomesticUiSnapshot final = Ui(UiLayerKind.StrategicMapCandidate);
                final.DomesticTargetCityId = CityId;

                FakeObservationSource source = new FakeObservationSource(
                    command,
                    new[] { menu, surface, outer, selector, selected, confirmation, result, final },
                    new[] { before, after },
                    before);
                FakeDomesticActionTransport transport = new FakeDomesticActionTransport();
                List<int> delayCalls = new List<int>();
                string evidenceDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "San9AutoDomestic.Input.SelfTest",
                    Guid.NewGuid().ToString("N"));
                return new Fixture
                {
                    Source = source,
                    Transport = transport,
                    Executor = new SingleCityDomesticExecutor(source, transport, delayCalls.Add),
                    Request = new SingleCityDomesticExecutionRequest(Guid.NewGuid(), command, evidenceDirectory),
                    Before = before,
                    After = after,
                    Surface = surface,
                    Outer = outer,
                    Selector = selector,
                    Selected = selected,
                    Confirmation = confirmation,
                    DelayCalls = delayCalls,
                    evidenceDirectory = evidenceDirectory
                };
            }

            internal void SetReadyBefore(
                int[] readyCandidatesInSourceOrder,
                int[] rankedCandidates)
            {
                int[] source = readyCandidatesInSourceOrder == null
                    ? new int[0] : readyCandidatesInSourceOrder.ToArray();
                int[] ranked = rankedCandidates == null
                    ? new int[0] : rankedCandidates.ToArray();
                Before.ReadyCandidatesInSourceOrder = source;
                Before.RankedCandidates = ranked;
                Outer.CandidateOfficerIds = source.ToArray();
                Selector.CandidateOfficerIds = ranked.ToArray();
                Selected.CandidateOfficerIds = ranked.ToArray();
            }

            internal SingleCityDomesticExecutionResult Execute()
            {
                return Execute(null);
            }

            internal SingleCityDomesticExecutionResult Execute(DomesticExecutionStopSignal stopSignal)
            {
                return Executor.Execute(Request, stopSignal);
            }

            internal void SetPostAvailabilitySequence(
                params DomesticAvailabilitySnapshot[] snapshots)
            {
                List<DomesticAvailabilitySnapshot> sequence =
                    new List<DomesticAvailabilitySnapshot> { Before };
                if (snapshots != null) sequence.AddRange(snapshots);
                Source.ReplaceAvailability(sequence);
            }

            public void Dispose()
            {
                if (!string.IsNullOrEmpty(evidenceDirectory)
                    && Directory.Exists(evidenceDirectory))
                {
                    Directory.Delete(evidenceDirectory, true);
                }
            }

            private static DomesticUiSnapshot Ui(UiLayerKind layer)
            {
                return new DomesticUiSnapshot
                {
                    IsValid = true,
                    Error = string.Empty,
                    CapturedUtc = DateTimeOffset.UtcNow,
                    ProcessId = ProcessId,
                    ProcessCreationFileTimeUtc = ProcessCreationFileTimeUtc,
                    MainWindowHandle = MainWindowHandle,
                    Layer = layer,
                    VerifiedCityId = CityId,
                    WindowObservationToken = "synthetic-token-" + layer,
                    CandidateOfficerIds = new int[0],
                    SelectedOfficerIds = new int[0],
                    WorkingOfficerIds = new int[0]
                };
            }

            private static DomesticAvailabilitySnapshot Availability(
                DomesticCommandKind command,
                int money,
                int costPerOfficer,
                bool knownStaticSubsetWouldPass,
                bool? orderBitClearPassed,
                int[] readyCandidatesInSourceOrder,
                int[] rankedCandidates)
            {
                return new DomesticAvailabilitySnapshot
                {
                    IsValid = true,
                    Error = string.Empty,
                    ProcessId = ProcessId,
                    ProcessCreationFileTimeUtc = ProcessCreationFileTimeUtc,
                    CityId = CityId,
                    CorpsMoney = money,
                    IsDirectlyControlled = true,
                    CostPerOfficer = costPerOfficer,
                    KnownStaticSubsetWouldPass = knownStaticSubsetWouldPass,
                    OrderBitClearPassed = orderBitClearPassed,
                    ReadyCandidatesInSourceOrder = readyCandidatesInSourceOrder.ToArray(),
                    RankedCandidates = rankedCandidates.ToArray()
                };
            }
        }

        private sealed class FakeObservationSource : IDomesticExecutionObservationSource
        {
            private readonly DomesticCommandKind expectedCommand;
            private readonly Queue<DomesticUiSnapshot> uiSnapshots;
            private readonly Queue<DomesticAvailabilitySnapshot> availabilitySnapshots;
            private readonly DomesticAvailabilitySnapshot fallbackProbeAvailability;

            internal FakeObservationSource(
                DomesticCommandKind expectedCommand,
                IEnumerable<DomesticUiSnapshot> uiSnapshots,
                IEnumerable<DomesticAvailabilitySnapshot> availabilitySnapshots,
                DomesticAvailabilitySnapshot fallbackProbeAvailability)
            {
                this.expectedCommand = expectedCommand;
                this.uiSnapshots = new Queue<DomesticUiSnapshot>(uiSnapshots);
                this.availabilitySnapshots = new Queue<DomesticAvailabilitySnapshot>(availabilitySnapshots);
                this.fallbackProbeAvailability = fallbackProbeAvailability;
            }

            internal int UiCaptureCount { get; private set; }
            internal int AvailabilityCaptureCount { get; private set; }
            internal readonly List<string> Events = new List<string>();

            internal void ReplaceAvailability(
                IEnumerable<DomesticAvailabilitySnapshot> snapshots)
            {
                availabilitySnapshots.Clear();
                foreach (DomesticAvailabilitySnapshot snapshot in snapshots)
                    availabilitySnapshots.Enqueue(snapshot);
                AvailabilityCaptureCount = 0;
                Events.Clear();
            }

            public DomesticUiSnapshot CaptureUi()
            {
                UiCaptureCount++;
                Events.Add("U");
                if (uiSnapshots.Count == 0)
                    throw new InvalidOperationException("No synthetic UI observation remains.");
                return uiSnapshots.Dequeue();
            }

            public DomesticAvailabilitySnapshot CaptureAvailability(DomesticCommandKind command, int cityId)
            {
                AvailabilityCaptureCount++;
                Events.Add("A");
                if (command != expectedCommand)
                {
                    if (command == DomesticCommandKind.Commerce && fallbackProbeAvailability != null)
                        return fallbackProbeAvailability;
                    throw new InvalidOperationException("The executor requested the wrong command.");
                }
                if (cityId != CityId)
                    throw new InvalidOperationException("The executor requested the wrong city.");
                if (availabilitySnapshots.Count == 0)
                    throw new InvalidOperationException("No synthetic availability observation remains.");
                return availabilitySnapshots.Dequeue();
            }
        }

        private sealed class FakeDomesticActionTransport : IDomesticActionTransport
        {
            internal int ActivationCount;
            internal int ScreenshotCount;
            internal int InputCallCount;
            internal int HoverCallCount;
            internal int ClickCallCount;
            internal int ReturnCallCount;
            internal int LastHoverX;
            internal int LastHoverY;
            internal int FailInputOrdinal;
            internal bool AttemptFailedInput;

            public bool TryActivate(WindowBinding expected, out string error)
            {
                ActivationCount++;
                if (!BindingMatches(expected))
                {
                    error = "Synthetic binding mismatch.";
                    return false;
                }
                error = string.Empty;
                return true;
            }

            public bool TryCaptureClientPng(WindowBinding expected, string path, out string error)
            {
                ScreenshotCount++;
                if (!BindingMatches(expected) || string.IsNullOrWhiteSpace(path))
                {
                    error = "Synthetic screenshot rejection.";
                    return false;
                }
                error = string.Empty;
                return true;
            }

            public bool TryClick(
                WindowBinding expected,
                int clientX,
                int clientY,
                out bool attempted,
                out string error)
            {
                ClickCallCount++;
                return Input(expected, out attempted, out error);
            }

            public bool TryHover(
                WindowBinding expected,
                int clientX,
                int clientY,
                out bool attempted,
                out string error)
            {
                HoverCallCount++;
                LastHoverX = clientX;
                LastHoverY = clientY;
                return Input(expected, out attempted, out error);
            }

            public bool TryPressReturn(WindowBinding expected, out bool attempted, out string error)
            {
                ReturnCallCount++;
                return Input(expected, out attempted, out error);
            }

            public bool TryPressEscape(WindowBinding expected, out bool attempted, out string error)
            {
                return Input(expected, out attempted, out error);
            }

            private bool Input(WindowBinding expected, out bool attempted, out string error)
            {
                InputCallCount++;
                if (!BindingMatches(expected))
                {
                    attempted = false;
                    error = "Synthetic binding mismatch.";
                    return false;
                }
                if (FailInputOrdinal == InputCallCount)
                {
                    attempted = AttemptFailedInput;
                    error = "Synthetic input failure.";
                    return false;
                }
                attempted = true;
                error = string.Empty;
                return true;
            }

            private static bool BindingMatches(WindowBinding binding)
            {
                return binding != null
                    && binding.ProcessId == ProcessId
                    && binding.ProcessCreationFileTimeUtc == ProcessCreationFileTimeUtc
                    && binding.MainWindowHandle == MainWindowHandle;
            }
        }
    }
}
