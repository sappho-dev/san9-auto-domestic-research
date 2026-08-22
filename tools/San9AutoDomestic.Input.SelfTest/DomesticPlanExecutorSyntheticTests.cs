using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Input.Win32;

namespace San9AutoDomestic.Input.SelfTest
{
    internal static class DomesticPlanExecutorSyntheticTests
    {
        private const int ProcessId = 37692;
        private const long ProcessCreationFileTimeUtc = 639218891755456732;
        private const long MainWindowHandle = 0x00120ED8;
        private const int CityId = 46;

        internal static void Register(Action<string, Action> run)
        {
            run("domestic plan completes Commerce then reopens for Cultivate", TwoStepPlanCompletesWithReopen);
            run("domestic plan skips first grey command then executes without reopening", FirstKnownSkipThenSecondExecutesWithoutReopen);
            run("domestic plan skips known second command on map without navigation", SecondKnownSkipDoesNotNavigate);
            run("domestic plan rejects wrong city after center navigation", WrongCityAfterCenterAborts);
            run("domestic plan rejects wrong city after facilities navigation", WrongCityAfterFacilitiesAborts);
            run("domestic plan rejects binding drift after center navigation", BindingDriftAfterCenterAborts);
            run("domestic plan rejects stale map before navigation without clicking", StaleMapBeforeNavigationSendsNoNavigationInput);
            run("domestic plan rejects a changed authorized process generation", AuthorizedGenerationMismatchRejectsBeforeInput);
            run("domestic plan does not retry attempted navigation failure", AttemptedNavigationFailureIsNotRetried);
            run("domestic plan retains attempted navigation evidence on capture exception", NavigationCaptureExceptionRetainsAttempt);
            run("domestic plan skips post-navigation grey command without command click", PostNavigationFreshGreySkipsCommand);
            run("domestic plan stops after verified center navigation boundary", StopAfterCenterNavigationIsSafe);
            run("domestic plan and single executor share one process lease", SharedLeaseRejectsConcurrentPlanAndSingle);
        }

        private static void FirstKnownSkipThenSecondExecutesWithoutReopen()
        {
            using (Fixture fixture = Fixture.CreateFirstKnownSkip())
            {
                SingleCityDomesticPlanResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal(DomesticExecutionStatus.SkippedUnavailable, result.TaskResults[0].Status);
                Equal(DomesticExecutionStatus.Completed, result.TaskResults[1].Status);
                Equal(0, result.NavigationEvidence.Count);
                Equal(7, result.InputActionCount);
                Equal(5, fixture.Transport.ClickCallCount);
                Equal(3, fixture.Source.AvailabilityCaptureCount);
                Equal(9, fixture.Source.UiCaptureCount);
            }
        }

        private static void TwoStepPlanCompletesWithReopen()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                SingleCityDomesticPlanResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal("PLAN_COMPLETED", result.Code);
                Equal(CityId, result.InitialCityId.Value);
                Equal(2, result.TaskResults.Count);
                Equal(DomesticExecutionStatus.Completed, result.TaskResults[0].Status);
                Equal(DomesticExecutionStatus.Completed, result.TaskResults[1].Status);
                Equal(2, result.NavigationEvidence.Count);
                Equal(16, result.InputActionCount);
                Equal(2, fixture.Transport.HoverCallCount);
                Equal(12, fixture.Transport.ClickCallCount);
                Equal(2, fixture.Transport.ReturnCallCount);
                Equal(16, fixture.Transport.InputCallCount);
                Equal(5, fixture.Source.AvailabilityCaptureCount);
                Equal(20, fixture.Source.UiCaptureCount);
                Equal("Cultivate", fixture.SecondSurface.HoveredCommand);
                True(result.NavigationEvidence.All(item => item.BeforeScreenshotPath.IndexOf("02-Cultivate", StringComparison.Ordinal) >= 0));
            }
        }

        private static void SecondKnownSkipDoesNotNavigate()
        {
            using (Fixture fixture = Fixture.Create(true))
            {
                SingleCityDomesticPlanResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal(2, result.TaskResults.Count);
                Equal(DomesticExecutionStatus.Completed, result.TaskResults[0].Status);
                Equal(DomesticExecutionStatus.SkippedUnavailable, result.TaskResults[1].Status);
                Equal("COMMAND_UNAVAILABLE", result.TaskResults[1].Code);
                Equal(0, result.NavigationEvidence.Count);
                Equal(7, result.InputActionCount);
                Equal(5, fixture.Transport.ClickCallCount);
                Equal(3, fixture.Source.AvailabilityCaptureCount);
                Equal(9, fixture.Source.UiCaptureCount);
            }
        }

        private static void AuthorizedGenerationMismatchRejectsBeforeInput()
        {
            using (Fixture fixture = Fixture.Create(true))
            {
                fixture.Request = new SingleCityDomesticPlanRequest(
                    Guid.NewGuid(),
                    new[] { DomesticCommandKind.Commerce, DomesticCommandKind.Cultivate },
                    fixture.EvidenceDirectory,
                    ProcessId,
                    ProcessCreationFileTimeUtc + 1);
                SingleCityDomesticPlanResult result = fixture.Execute();
                Equal(DomesticExecutionStatus.RejectedBeforeInput, result.Status);
                Equal("AUTHORIZED_PROCESS_GENERATION_MISMATCH", result.Code);
                Equal(0, result.InputActionCount);
                Equal(0, fixture.Transport.InputCallCount);
            }
        }

        private static void SharedLeaseRejectsConcurrentPlanAndSingle()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                DomesticExecutionBatchLease.Lease held;
                True(DomesticExecutionBatchLease.TryAcquire(out held));
                using (held)
                {
                    SingleCityDomesticPlanResult plan = fixture.Execute();
                    Equal(DomesticExecutionStatus.RejectedBeforeInput, plan.Status);
                    Equal("GLOBAL_EXECUTION_BUSY", plan.Code);

                    SingleCityDomesticExecutor single = new SingleCityDomesticExecutor(
                        fixture.Source,
                        fixture.Transport,
                        fixture.DelayCalls.Add);
                    SingleCityDomesticExecutionRequest request = new SingleCityDomesticExecutionRequest(
                        Guid.NewGuid(),
                        DomesticCommandKind.Commerce,
                        Path.Combine(fixture.EvidenceDirectory, "single-busy"));
                    SingleCityDomesticExecutionResult singleResult = single.Execute(request, null);
                    Equal(DomesticExecutionStatus.RejectedBeforeInput, singleResult.Status);
                    Equal("GLOBAL_EXECUTION_BUSY", singleResult.Code);
                }
                Equal(0, fixture.Source.UiCaptureCount);
                Equal(0, fixture.Transport.InputCallCount);
            }
        }

        private static void WrongCityAfterCenterAborts()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                fixture.CenterMenu.VerifiedCityId = CityId + 1;
                SingleCityDomesticPlanResult result = fixture.Execute();
                AssertNavigationAbort(result, fixture, "NAVIGATE_CENTER_CITY_STATE_REJECTED", 1, 8);
            }
        }

        private static void WrongCityAfterFacilitiesAborts()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                fixture.FacilitiesMenu.VerifiedCityId = CityId + 1;
                SingleCityDomesticPlanResult result = fixture.Execute();
                AssertNavigationAbort(result, fixture, "NAVIGATE_FACILITIES_STATE_REJECTED", 2, 9);
            }
        }

        private static void BindingDriftAfterCenterAborts()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                fixture.CenterMenu.ProcessCreationFileTimeUtc++;
                SingleCityDomesticPlanResult result = fixture.Execute();
                AssertNavigationAbort(result, fixture, "NAVIGATE_CENTER_CITY_STATE_REJECTED", 1, 8);
            }
        }

        private static void AttemptedNavigationFailureIsNotRetried()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                fixture.Transport.FailInputOrdinal = 8;
                fixture.Transport.AttemptFailedInput = true;
                SingleCityDomesticPlanResult result = fixture.Execute();
                AssertNavigationAbort(result, fixture, "NAVIGATE_CENTER_CITY_FAILED", 1, 8);
                Equal(6, fixture.Transport.ClickCallCount);
            }
        }

        private static void StaleMapBeforeNavigationSendsNoNavigationInput()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                fixture.NavigationMap.Layer = UiLayerKind.DomesticCommandMenu;
                SingleCityDomesticPlanResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("NAVIGATION_MAP_REJECTED", result.Code);
                Equal(0, result.NavigationEvidence.Count);
                Equal(7, result.InputActionCount);
                Equal(5, fixture.Transport.ClickCallCount);
                Equal(DomesticExecutionStatus.RejectedBeforeInput, result.TaskResults[1].Status);
            }
        }

        private static void NavigationCaptureExceptionRetainsAttempt()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                fixture.Source.ThrowOnUiCaptureOrdinal = 11;
                SingleCityDomesticPlanResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("PLAN_EXECUTION_EXCEPTION_AFTER_INPUT", result.Code);
                Equal(1, result.NavigationEvidence.Count);
                Equal(8, result.InputActionCount);
                Equal(6, fixture.Transport.ClickCallCount);
            }
        }

        private static void PostNavigationFreshGreySkipsCommand()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                fixture.SecondAfterNavigationAvailability.KnownStaticSubsetWouldPass = false;
                SingleCityDomesticPlanResult result = fixture.Execute();

                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal(DomesticExecutionStatus.SkippedUnavailable, result.TaskResults[1].Status);
                Equal("COMMAND_UNAVAILABLE", result.TaskResults[1].Code);
                Equal(2, result.NavigationEvidence.Count);
                Equal(9, result.InputActionCount);
                Equal(7, fixture.Transport.ClickCallCount);
                Equal(1, fixture.Transport.HoverCallCount);
                Equal(4, fixture.Source.AvailabilityCaptureCount);
            }
        }

        private static void StopAfterCenterNavigationIsSafe()
        {
            using (Fixture fixture = Fixture.Create(false))
            {
                DomesticExecutionStopSignal stop = new DomesticExecutionStopSignal();
                fixture.Source.AfterUiCapture = delegate(int count)
                {
                    if (count == 11) stop.RequestStop();
                };

                SingleCityDomesticPlanResult result = fixture.Execute(stop);

                Equal(DomesticExecutionStatus.StoppedBeforeCommit, result.Status);
                Equal(2, result.TaskResults.Count);
                Equal(DomesticExecutionStatus.StoppedBeforeCommit, result.TaskResults[1].Status);
                Equal(1, result.NavigationEvidence.Count);
                Equal(8, result.InputActionCount);
                Equal(6, fixture.Transport.ClickCallCount);
            }
        }

        private static void AssertNavigationAbort(
            SingleCityDomesticPlanResult result,
            Fixture fixture,
            string code,
            int navigationEvidenceCount,
            int inputActionCount)
        {
            Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
            Equal(code, result.Code);
            Equal(2, result.TaskResults.Count);
            Equal(DomesticExecutionStatus.AbortUncertain, result.TaskResults[1].Status);
            Equal(navigationEvidenceCount, result.NavigationEvidence.Count);
            Equal(inputActionCount, result.InputActionCount);
            Equal(0, fixture.Transport.ReturnCallCount - 1);
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

        private sealed class Fixture : IDisposable
        {
            private static readonly int[] ReadyTen =
                { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110 };
            private static readonly int[] RankedTen =
                { 110, 109, 108, 107, 106, 105, 104, 103, 102, 101 };
            private static readonly int[] SelectedFirst =
                { 110, 109, 108, 107, 106 };
            private static readonly int[] ReadyFive =
                { 101, 102, 103, 104, 105 };
            private static readonly int[] RankedFive =
                { 105, 104, 103, 102, 101 };
            private static readonly int[] PostNavigationRankedFive =
                { 101, 102, 103, 104, 105 };

            internal FakeObservationSource Source;
            internal FakeTransport Transport;
            internal SingleCityDomesticPlanExecutor Executor;
            internal SingleCityDomesticPlanRequest Request;
            internal List<int> DelayCalls;
            internal string EvidenceDirectory;
            internal DomesticUiSnapshot CenterMenu;
            internal DomesticUiSnapshot FacilitiesMenu;
            internal DomesticUiSnapshot NavigationMap;
            internal DomesticUiSnapshot SecondSurface;
            internal DomesticAvailabilitySnapshot SecondAfterNavigationAvailability;

            internal static Fixture Create(bool secondKnownSkip)
            {
                List<DomesticUiSnapshot> ui = new List<DomesticUiSnapshot>();
                List<AvailabilityEntry> availability = new List<AvailabilityEntry>();
                ui.Add(Menu());
                ui.Add(Menu());
                AddStepUi(ui, DomesticCommandKind.Commerce, ReadyTen, RankedTen, SelectedFirst);
                availability.Add(new AvailabilityEntry(DomesticCommandKind.Commerce, Available(1000, 50, true, true, ReadyTen, RankedTen)));
                availability.Add(new AvailabilityEntry(DomesticCommandKind.Commerce, Available(750, 50, false, false, ReadyFive, RankedFive)));

                DomesticUiSnapshot center = null;
                DomesticUiSnapshot facilities = null;
                DomesticUiSnapshot navigationMap = null;
                DomesticUiSnapshot secondSurface = null;
                DomesticAvailabilitySnapshot secondAfterNavigation = null;
                if (secondKnownSkip)
                {
                    availability.Add(new AvailabilityEntry(DomesticCommandKind.Cultivate, Available(750, 50, false, false, ReadyFive, RankedFive)));
                }
                else
                {
                    DomesticAvailabilitySnapshot secondBefore = Available(750, 50, true, true, ReadyFive, RankedFive);
                    availability.Add(new AvailabilityEntry(DomesticCommandKind.Cultivate, secondBefore));
                    secondAfterNavigation = Available(750, 50, true, true, ReadyFive, PostNavigationRankedFive);
                    availability.Add(new AvailabilityEntry(DomesticCommandKind.Cultivate, secondAfterNavigation));
                    availability.Add(new AvailabilityEntry(DomesticCommandKind.Cultivate, Available(500, 50, false, false, new int[0], new int[0])));
                    navigationMap = Map();
                    center = Menu();
                    facilities = Menu();
                    ui.Add(navigationMap);
                    ui.Add(center);
                    ui.Add(facilities);
                    ui.Add(Menu());
                    secondSurface = AddStepUi(ui, DomesticCommandKind.Cultivate, ReadyFive, PostNavigationRankedFive, PostNavigationRankedFive);
                }

                FakeObservationSource source = new FakeObservationSource(ui, availability);
                FakeTransport transport = new FakeTransport();
                List<int> delays = new List<int>();
                string evidence = Path.Combine(Path.GetTempPath(), "San9AutoDomestic.Input.SelfTest", Guid.NewGuid().ToString("N"));
                return new Fixture
                {
                    Source = source,
                    Transport = transport,
                    Executor = new SingleCityDomesticPlanExecutor(source, transport, delays.Add),
                    Request = new SingleCityDomesticPlanRequest(
                        Guid.NewGuid(),
                        new[] { DomesticCommandKind.Commerce, DomesticCommandKind.Cultivate },
                        evidence,
                        ProcessId,
                        ProcessCreationFileTimeUtc),
                    DelayCalls = delays,
                    EvidenceDirectory = evidence,
                    CenterMenu = center,
                    FacilitiesMenu = facilities,
                    NavigationMap = navigationMap,
                    SecondSurface = secondSurface,
                    SecondAfterNavigationAvailability = secondAfterNavigation
                };
            }

            internal static Fixture CreateFirstKnownSkip()
            {
                List<DomesticUiSnapshot> ui = new List<DomesticUiSnapshot>();
                List<AvailabilityEntry> availability = new List<AvailabilityEntry>();
                ui.Add(Menu());
                ui.Add(Menu());
                DomesticUiSnapshot secondSurface = AddStepUi(
                    ui,
                    DomesticCommandKind.Cultivate,
                    ReadyTen,
                    RankedTen,
                    SelectedFirst);
                availability.Add(new AvailabilityEntry(
                    DomesticCommandKind.Commerce,
                    Available(1000, 50, false, false, ReadyTen, RankedTen)));
                availability.Add(new AvailabilityEntry(
                    DomesticCommandKind.Cultivate,
                    Available(1000, 50, true, true, ReadyTen, RankedTen)));
                availability.Add(new AvailabilityEntry(
                    DomesticCommandKind.Cultivate,
                    Available(750, 50, false, false, ReadyFive, RankedFive)));
                FakeObservationSource source = new FakeObservationSource(ui, availability);
                FakeTransport transport = new FakeTransport();
                List<int> delays = new List<int>();
                string evidence = Path.Combine(Path.GetTempPath(), "San9AutoDomestic.Input.SelfTest", Guid.NewGuid().ToString("N"));
                return new Fixture
                {
                    Source = source,
                    Transport = transport,
                    Executor = new SingleCityDomesticPlanExecutor(source, transport, delays.Add),
                    Request = new SingleCityDomesticPlanRequest(
                        Guid.NewGuid(),
                        new[] { DomesticCommandKind.Commerce, DomesticCommandKind.Cultivate },
                        evidence,
                        ProcessId,
                        ProcessCreationFileTimeUtc),
                    DelayCalls = delays,
                    EvidenceDirectory = evidence,
                    SecondSurface = secondSurface
                };
            }

            internal SingleCityDomesticPlanResult Execute()
            {
                return Execute(null);
            }

            internal SingleCityDomesticPlanResult Execute(DomesticExecutionStopSignal stopSignal)
            {
                return Executor.Execute(Request, stopSignal);
            }

            public void Dispose()
            {
                if (Directory.Exists(EvidenceDirectory)) Directory.Delete(EvidenceDirectory, true);
            }

            private static DomesticUiSnapshot AddStepUi(
                IList<DomesticUiSnapshot> ui,
                DomesticCommandKind command,
                int[] source,
                int[] ranked,
                int[] selected)
            {
                DomesticUiSnapshot surface = Menu();
                surface.HoveredCommandState = UiObservationState.Verified;
                surface.HoveredCommand = DomesticExecutionCoordinates.CommandName(command);
                ui.Add(surface);
                DomesticUiSnapshot outer = Ui(UiLayerKind.DomesticOuterDialog);
                outer.OuterCommandName = DomesticExecutionCoordinates.CommandName(command);
                outer.CandidateOfficerIds = source.ToArray();
                ui.Add(outer);
                DomesticUiSnapshot selector = Ui(UiLayerKind.OfficerSelector);
                selector.CandidateOfficerIds = ranked.ToArray();
                ui.Add(selector);
                DomesticUiSnapshot selectedUi = Ui(UiLayerKind.OfficerSelector);
                selectedUi.CandidateOfficerIds = ranked.ToArray();
                selectedUi.SelectedOfficerIds = selected.Take(5).ToArray();
                ui.Add(selectedUi);
                DomesticUiSnapshot confirmation = Ui(UiLayerKind.DomesticConfirmation);
                confirmation.WorkingOfficerIds = selected.Take(5).ToArray();
                ui.Add(confirmation);
                ui.Add(Map());
                ui.Add(Map());
                return surface;
            }

            private static DomesticUiSnapshot Menu()
            {
                return Ui(UiLayerKind.DomesticCommandMenu);
            }

            private static DomesticUiSnapshot Map()
            {
                DomesticUiSnapshot value = Ui(UiLayerKind.StrategicMapCandidate);
                value.DomesticTargetCityId = CityId;
                return value;
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
                    WindowObservationToken = "plan-" + Guid.NewGuid().ToString("N"),
                    CandidateOfficerIds = new int[0],
                    SelectedOfficerIds = new int[0],
                    WorkingOfficerIds = new int[0]
                };
            }

            private static DomesticAvailabilitySnapshot Available(
                int money,
                int cost,
                bool staticPass,
                bool? orderClear,
                int[] source,
                int[] ranked)
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
                    CostPerOfficer = cost,
                    KnownStaticSubsetWouldPass = staticPass,
                    OrderBitClearPassed = orderClear,
                    ReadyCandidatesInSourceOrder = source.ToArray(),
                    RankedCandidates = ranked.ToArray()
                };
            }
        }

        private sealed class AvailabilityEntry
        {
            internal AvailabilityEntry(DomesticCommandKind command, DomesticAvailabilitySnapshot snapshot)
            {
                Command = command;
                Snapshot = snapshot;
            }
            internal readonly DomesticCommandKind Command;
            internal readonly DomesticAvailabilitySnapshot Snapshot;
        }

        private sealed class FakeObservationSource : IDomesticExecutionObservationSource
        {
            private readonly Queue<DomesticUiSnapshot> ui;
            private readonly Queue<AvailabilityEntry> availability;

            internal FakeObservationSource(IEnumerable<DomesticUiSnapshot> ui, IEnumerable<AvailabilityEntry> availability)
            {
                this.ui = new Queue<DomesticUiSnapshot>(ui);
                this.availability = new Queue<AvailabilityEntry>(availability);
            }

            internal int UiCaptureCount { get; private set; }
            internal int AvailabilityCaptureCount { get; private set; }
            internal Action<int> AfterUiCapture;
            internal int ThrowOnUiCaptureOrdinal;

            public DomesticUiSnapshot CaptureUi()
            {
                UiCaptureCount++;
                if (UiCaptureCount == ThrowOnUiCaptureOrdinal)
                    throw new InvalidOperationException("Synthetic UI capture exception.");
                if (ui.Count == 0) throw new InvalidOperationException("No plan UI snapshot remains.");
                DomesticUiSnapshot snapshot = ui.Dequeue();
                if (AfterUiCapture != null) AfterUiCapture(UiCaptureCount);
                return snapshot;
            }

            public DomesticAvailabilitySnapshot CaptureAvailability(DomesticCommandKind command, int cityId)
            {
                AvailabilityCaptureCount++;
                if (cityId != CityId || availability.Count == 0)
                    throw new InvalidOperationException("Unexpected plan availability request.");
                AvailabilityEntry entry = availability.Dequeue();
                if (entry.Command != command)
                    throw new InvalidOperationException("Plan requested the wrong command availability.");
                return entry.Snapshot;
            }
        }

        private sealed class FakeTransport : IDomesticActionTransport
        {
            internal int InputCallCount;
            internal int HoverCallCount;
            internal int ClickCallCount;
            internal int ReturnCallCount;
            internal int FailInputOrdinal;
            internal bool AttemptFailedInput;

            public bool TryActivate(WindowBinding expected, out string error)
            {
                error = Binding(expected) ? string.Empty : "binding";
                return Binding(expected);
            }

            public bool TryCaptureClientPng(WindowBinding expected, string path, out string error)
            {
                error = Binding(expected) && !string.IsNullOrWhiteSpace(path) ? string.Empty : "capture";
                return string.IsNullOrEmpty(error);
            }

            public bool TryHover(WindowBinding expected, int clientX, int clientY, out bool attempted, out string error)
            {
                HoverCallCount++;
                return Input(expected, out attempted, out error);
            }

            public bool TryClick(WindowBinding expected, int clientX, int clientY, out bool attempted, out string error)
            {
                ClickCallCount++;
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
                if (InputCallCount == FailInputOrdinal)
                {
                    attempted = AttemptFailedInput;
                    error = "synthetic input failure";
                    return false;
                }
                attempted = Binding(expected);
                error = attempted ? string.Empty : "binding";
                return attempted;
            }

            private static bool Binding(WindowBinding binding)
            {
                return binding != null
                    && binding.ProcessId == ProcessId
                    && binding.ProcessCreationFileTimeUtc == ProcessCreationFileTimeUtc
                    && binding.MainWindowHandle == MainWindowHandle;
            }
        }
    }
}
