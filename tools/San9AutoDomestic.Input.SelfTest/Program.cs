using System;
using System.Collections.Generic;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Input.Win32;

namespace San9AutoDomestic.Input.SelfTest
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            Run("exactly one targeted move completes", Success);
            Run("stale authorization token rejects before send", StaleTokenRejects);
            Run("pre-send city drift rejects before send", PreCityDriftRejects);
            Run("native binding rejection sends nothing", NativeGateRejects);
            Run("failed PostMessage attempt is uncertain and not retried", FailedPostIsUncertain);
            Run("unchanged hover aborts without retry", HoverNotMovedAborts);
            Run("foreground change aborts after one send", ForegroundChangeAborts);
            Run("post-send city change aborts", PostCityChangeAborts);
            Run("post-send structure change aborts", StructureChangeAborts);
            Run("hand is single-use", SingleUse);
            Run("process-wide single-flight rejects a second batch", GlobalSingleFlight);
            Run("post-send exception preserves message count", PostExceptionCountsSend);
            Run("unstable capture cannot authorize", UnstableCannotAuthorize);
            Run("authorization expires after thirty seconds", AuthorizationExpires);
            Run("facilities route verifies exact target city", FacilitiesRouteSuccess);
            Run("facilities route requires strategic map start", FacilitiesRouteRequiresMap);
            Run("facilities route stops on unexpected intermediate structure", FacilitiesIntermediateStructureMustStayStable);
            Run("facilities route rejects a wrong final city", FacilitiesWrongCityAborts);
            Run("facilities click requires exact game foreground", FacilitiesForegroundRequired);
            Run("facilities authorization does not require command hover", FacilitiesAuthorizationDoesNotRequireHover);
            Run("ready facilities map selects exact target city", FacilitiesRowSelectionSuccess);
            Run("facilities row selection rejects changed map token", FacilitiesRowSelectionRejectsChangedMap);
            Run("facilities row selection rejects wrong final city", FacilitiesRowSelectionWrongCity);
            Run("facilities map authorization rejects city menu", FacilitiesMapAuthorizationRejectsMenu);
            Run("facilities row selection uses SendInput only when requested", FacilitiesRowSelectionUsesSendInput);
            Run("facilities row selection uses staged SendInput only when requested", FacilitiesRowSelectionUsesStagedSendInput);
            ManualInputTraceSyntheticTests.Register(Run);
            DomesticExecutorSyntheticTests.Register(Run);
            DomesticPlanExecutorSyntheticTests.Register(Run);
            AllDirectCitiesDomesticPlanSyntheticTests.Register(Run);
            Console.WriteLine("Input hand synthetic summary: {0} passed, {1} failed.", passed, failed);
            return failed == 0 ? 0 : 1;
        }

        private static void Success()
        {
            Fixture f = Fixture.Valid();
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.Completed, result.Status);
            Equal(1, result.MessageCount);
            Equal(1, f.Transport.SendCount);
            True(result.BindingStable && result.ForegroundStable && result.StateVerified);
        }

        private static void StaleTokenRejects()
        {
            Fixture f = Fixture.Valid();
            f.Pre.WindowObservationToken = "changed";
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.RejectedBeforeSend, result.Status);
            Equal(0, result.MessageCount);
            Equal(0, f.Transport.SendCount);
        }

        private static void PreCityDriftRejects()
        {
            Fixture f = Fixture.Valid();
            f.Pre.CityId = 0;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.RejectedBeforeSend, result.Status);
            Equal(0, f.Transport.SendCount);
        }

        private static void NativeGateRejects()
        {
            Fixture f = Fixture.Valid();
            f.Transport.SendAllowed = false;
            f.Transport.AttemptedOnFailure = false;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.RejectedBeforeSend, result.Status);
            Equal(0, result.MessageCount);
        }

        private static void HoverNotMovedAborts()
        {
            Fixture f = Fixture.Valid();
            f.Post.HoveredCommand = DomesticHoverCommand.Commerce;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            Equal(1, result.MessageCount);
            Equal(1, f.Transport.SendCount);
        }

        private static void FailedPostIsUncertain()
        {
            Fixture f = Fixture.Valid();
            f.Transport.SendAllowed = false;
            f.Transport.AttemptedOnFailure = true;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            Equal(1, result.MessageCount);
            Equal(1, f.Transport.SendCount);
        }

        private static void ForegroundChangeAborts()
        {
            Fixture f = Fixture.Valid();
            f.Transport.After.ForegroundWindowHandle++;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            True(!result.ForegroundStable);
            Equal(1, f.Transport.SendCount);
        }

        private static void PostCityChangeAborts()
        {
            Fixture f = Fixture.Valid();
            f.Post.CityId = 8;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            True(!result.StateVerified);
        }

        private static void StructureChangeAborts()
        {
            Fixture f = Fixture.Valid();
            f.Post.StructuralToken = "different-task-structure";
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            True(!result.StateVerified);
        }

        private static void SingleUse()
        {
            Fixture f = Fixture.Valid();
            TargetedInputResult first = f.Execute();
            TargetedInputResult second = f.Hand.Execute(f.Request);
            Equal(TargetedInputStatus.Completed, first.Status);
            Equal(TargetedInputStatus.RejectedBeforeSend, second.Status);
            Equal("BATCH_ALREADY_USED", second.Code);
            Equal(1, f.Transport.SendCount);
        }

        private static void PostExceptionCountsSend()
        {
            Fixture f = Fixture.Valid();
            f.Source.ThrowOnCaptureNumber = 3;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            Equal(1, result.MessageCount);
            Equal(1, f.Transport.SendCount);
        }

        private static void GlobalSingleFlight()
        {
            Fixture first = Fixture.Valid();
            Fixture second = Fixture.Valid();
            first.Source.BlockOnCaptureNumber = 2;
            TargetedInputResult firstResult = null;
            Thread worker = new Thread(new ThreadStart(delegate { firstResult = first.Execute(); }));
            worker.Start();
            True(first.Source.CaptureEntered.WaitOne(2000));
            TargetedInputResult secondResult = second.Execute();
            first.Source.ReleaseCapture.Set();
            True(worker.Join(2000));
            Equal(TargetedInputStatus.Completed, firstResult.Status);
            Equal(TargetedInputStatus.RejectedBeforeSend, secondResult.Status);
            Equal("GLOBAL_INPUT_BUSY", secondResult.Code);
            Equal(0, second.Transport.SendCount);
        }

        private static void UnstableCannotAuthorize()
        {
            InputUiSnapshot unstable = Snapshot(DomesticHoverCommand.Commerce);
            unstable.StableAbc = false;
            QueueSource source = new QueueSource(unstable);
            San9Pk101TargetedInputHand hand = new San9Pk101TargetedInputHand(source, FakeTransport.Valid(), delegate { });
            TargetedHoverAuthorization authorization = hand.CaptureAuthorization();
            True(!authorization.CanAuthorize);
        }

        private static void AuthorizationExpires()
        {
            InputUiSnapshot authorization = Snapshot(DomesticHoverCommand.Commerce);
            authorization.CapturedUtc = DateTimeOffset.UtcNow.AddSeconds(-31);
            QueueSource source = new QueueSource(authorization);
            FakeTransport transport = FakeTransport.Valid();
            San9Pk101TargetedInputHand hand = new San9Pk101TargetedInputHand(source, transport, delegate { });
            TargetedHoverAuthorization capture = hand.CaptureAuthorization();
            TargetedHoverRequest request = new TargetedHoverRequest(Guid.NewGuid(), capture, DomesticHoverCommand.Cultivate, 637, 405);
            TargetedInputResult result = hand.Execute(request);
            Equal(TargetedInputStatus.RejectedBeforeSend, result.Status);
            Equal("AUTHORIZATION_EXPIRED", result.Code);
            Equal(0, transport.SendCount);
        }

        private static void FacilitiesRouteSuccess()
        {
            RouteFixture f = RouteFixture.Valid();
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.Completed, result.Status);
            Equal("FACILITIES_CITY_SWITCH_VERIFIED", result.Code);
            Equal(6, result.MessageCount);
            Equal(2, result.GestureCount);
            Equal(16, result.ObservedCityId.Value);
            Equal(2, f.Transport.ClickCount);
        }

        private static void FacilitiesRouteRequiresMap()
        {
            RouteFixture f = RouteFixture.Valid();
            f.Map.Layer = UiLayerKind.DomesticCommandMenu;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.RejectedBeforeSend, result.Status);
            Equal(0, result.MessageCount);
            Equal(0, f.Transport.ClickCount);
        }

        private static void FacilitiesIntermediateStructureMustStayStable()
        {
            RouteFixture f = RouteFixture.Valid();
            f.Panel.StructuralToken = "unexpected-task-structure";
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            Equal(3, result.MessageCount);
            Equal(1, result.GestureCount);
            Equal(1, f.Transport.ClickCount);
        }

        private static void FacilitiesWrongCityAborts()
        {
            RouteFixture f = RouteFixture.Valid();
            f.Final.CityId = 0;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            Equal(6, result.MessageCount);
            Equal(2, result.GestureCount);
            Equal(0, result.ObservedCityId.Value);
            Equal(2, f.Transport.ClickCount);
        }

        private static void FacilitiesForegroundRequired()
        {
            RouteFixture f = RouteFixture.Valid();
            f.Transport.AtSend.ForegroundProcessId = 1;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.RejectedBeforeSend, result.Status);
            Equal(0, result.MessageCount);
            Equal(0, f.Transport.ClickCount);
        }

        private static void FacilitiesAuthorizationDoesNotRequireHover()
        {
            InputUiSnapshot menu = Snapshot(DomesticHoverCommand.Commerce);
            menu.HoveredCommandVerified = false;
            QueueSource source = new QueueSource(menu);
            San9Pk101TargetedInputHand hand = new San9Pk101TargetedInputHand(source, FakeTransport.Valid(), delegate { });
            TargetedHoverAuthorization authorization = hand.CaptureFacilitiesCitySwitchAuthorization();
            True(authorization.CanAuthorize);
            Equal(46, authorization.CityId);
        }

        private static void FacilitiesRowSelectionSuccess()
        {
            MapRowFixture f = MapRowFixture.Valid();
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.Completed, result.Status);
            Equal("FACILITIES_ROW_TARGET_CITY_VERIFIED", result.Code);
            Equal(3, result.MessageCount);
            Equal(1, result.GestureCount);
            Equal(16, result.ObservedCityId.Value);
            Equal(1, f.Transport.ClickCount);
        }

        private static void FacilitiesRowSelectionRejectsChangedMap()
        {
            MapRowFixture f = MapRowFixture.Valid();
            f.Before.StructuralToken = "changed-map";
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.RejectedBeforeSend, result.Status);
            Equal(0, result.MessageCount);
            Equal(0, f.Transport.ClickCount);
        }

        private static void FacilitiesRowSelectionWrongCity()
        {
            MapRowFixture f = MapRowFixture.Valid();
            f.Final.CityId = 42;
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.AbortUncertain, result.Status);
            Equal(3, result.MessageCount);
            Equal(42, result.ObservedCityId.Value);
            Equal(1, f.Transport.ClickCount);
        }

        private static void FacilitiesMapAuthorizationRejectsMenu()
        {
            InputUiSnapshot menu = Snapshot(DomesticHoverCommand.Commerce);
            QueueSource source = new QueueSource(menu);
            San9Pk101TargetedInputHand hand = new San9Pk101TargetedInputHand(source, FakeTransport.Valid(), delegate { });
            FacilitiesMapAuthorization authorization = hand.CaptureFacilitiesMapAuthorization();
            True(!authorization.CanAuthorize);
        }

        private static void FacilitiesRowSelectionUsesSendInput()
        {
            MapRowFixture f = MapRowFixture.Valid();
            f.Request = new FacilitiesRowSelectionRequest(Guid.NewGuid(), f.Authorization, 16, 479, 101, TargetedClickMode.SendInput);
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.Completed, result.Status);
            Equal(0, f.Transport.ClickCount);
            Equal(1, f.Transport.SendInputClickCount);
            Equal(3, result.MessageCount);
        }

        private static void FacilitiesRowSelectionUsesStagedSendInput()
        {
            MapRowFixture f = MapRowFixture.Valid();
            f.Request = new FacilitiesRowSelectionRequest(Guid.NewGuid(), f.Authorization, 16, 479, 101, TargetedClickMode.SendInputStaged);
            TargetedInputResult result = f.Execute();
            Equal(TargetedInputStatus.Completed, result.Status);
            Equal(0, f.Transport.ClickCount);
            Equal(0, f.Transport.SendInputClickCount);
            Equal(1, f.Transport.StagedSendInputClickCount);
            Equal(3, result.MessageCount);
        }

        private static InputUiSnapshot Snapshot(DomesticHoverCommand hover)
        {
            return new InputUiSnapshot
            {
                ReadSucceeded = true,
                StableAbc = true,
                HasBlockingIssue = false,
                CapturedUtc = DateTimeOffset.UtcNow,
                ProcessId = 37692,
                ProcessCreationFileTimeUtc = 639218891755456732,
                MainWindowHandle = 0x00120ED8,
                Layer = UiLayerKind.DomesticCommandMenu,
                StrategicInput = true,
                HoveredCommandVerified = true,
                HoveredCommand = hover,
                CityId = 46,
                WindowObservationToken = hover == DomesticHoverCommand.Commerce ? "pre-token" : "post-token",
                StructuralToken = "scene|root|controller|menu|phase"
            };
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { failed++; Console.WriteLine("FAIL: {0}: {1}", name, exception.Message); }
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

        private sealed class Fixture
        {
            internal QueueSource Source;
            internal FakeTransport Transport;
            internal San9Pk101TargetedInputHand Hand;
            internal TargetedHoverRequest Request;
            internal InputUiSnapshot Pre;
            internal InputUiSnapshot Post;

            internal static Fixture Valid()
            {
                InputUiSnapshot authorization = Snapshot(DomesticHoverCommand.Commerce);
                InputUiSnapshot pre = Snapshot(DomesticHoverCommand.Commerce);
                InputUiSnapshot post = Snapshot(DomesticHoverCommand.Cultivate);
                QueueSource source = new QueueSource(authorization, pre, post);
                FakeTransport transport = FakeTransport.Valid();
                San9Pk101TargetedInputHand hand = new San9Pk101TargetedInputHand(source, transport, delegate { });
                TargetedHoverAuthorization capture = hand.CaptureFacilitiesCitySwitchAuthorization();
                return new Fixture
                {
                    Source = source,
                    Transport = transport,
                    Hand = hand,
                    Request = new TargetedHoverRequest(Guid.NewGuid(), capture, DomesticHoverCommand.Cultivate, 637, 405),
                    Pre = pre,
                    Post = post
                };
            }

            internal TargetedInputResult Execute()
            {
                return Hand.Execute(Request);
            }
        }

        private sealed class RouteFixture
        {
            internal QueueSource Source;
            internal FakeTransport Transport;
            internal San9Pk101TargetedInputHand Hand;
            internal FacilitiesCitySwitchRequest Request;
            internal InputUiSnapshot Map;
            internal InputUiSnapshot Panel;
            internal InputUiSnapshot Final;

            internal static RouteFixture Valid()
            {
                InputUiSnapshot authorization = Snapshot(DomesticHoverCommand.Commerce);
                InputUiSnapshot map = NavigationSnapshot("strategic-map");
                InputUiSnapshot panel = NavigationSnapshot("strategic-map");
                InputUiSnapshot final = Snapshot(DomesticHoverCommand.Commerce);
                final.CityId = 16;
                final.WindowObservationToken = "target-city-menu";
                QueueSource source = new QueueSource(authorization, map, panel, final);
                FakeTransport transport = FakeTransport.Valid();
                San9Pk101TargetedInputHand hand = new San9Pk101TargetedInputHand(source, transport, delegate { });
                TargetedHoverAuthorization capture = hand.CaptureAuthorization();
                return new RouteFixture
                {
                    Source = source,
                    Transport = transport,
                    Hand = hand,
                    Request = new FacilitiesCitySwitchRequest(Guid.NewGuid(), capture, 16, 479, 101),
                    Map = map,
                    Panel = panel,
                    Final = final
                };
            }

            internal TargetedInputResult Execute()
            {
                return Hand.ExecuteFacilitiesCitySwitch(Request);
            }
        }

        private sealed class MapRowFixture
        {
            internal QueueSource Source;
            internal FakeTransport Transport;
            internal San9Pk101TargetedInputHand Hand;
            internal FacilitiesRowSelectionRequest Request;
            internal FacilitiesMapAuthorization Authorization;
            internal InputUiSnapshot Before;
            internal InputUiSnapshot Final;

            internal static MapRowFixture Valid()
            {
                InputUiSnapshot authorization = NavigationSnapshot("strategic-map");
                InputUiSnapshot before = NavigationSnapshot("strategic-map");
                InputUiSnapshot final = Snapshot(DomesticHoverCommand.Commerce);
                final.CityId = 16;
                QueueSource source = new QueueSource(authorization, before, final);
                FakeTransport transport = FakeTransport.Valid();
                San9Pk101TargetedInputHand hand = new San9Pk101TargetedInputHand(source, transport, delegate { });
                FacilitiesMapAuthorization capture = hand.CaptureFacilitiesMapAuthorization();
                return new MapRowFixture
                {
                    Source = source,
                    Transport = transport,
                    Hand = hand,
                    Authorization = capture,
                    Request = new FacilitiesRowSelectionRequest(Guid.NewGuid(), capture, 16, 479, 101),
                    Before = before,
                    Final = final
                };
            }

            internal TargetedInputResult Execute()
            {
                return Hand.ExecuteFacilitiesRowSelection(Request);
            }
        }

        private static InputUiSnapshot NavigationSnapshot(string structure)
        {
            InputUiSnapshot value = Snapshot(DomesticHoverCommand.Commerce);
            value.Layer = UiLayerKind.StrategicMapCandidate;
            value.CityId = -1;
            value.HoveredCommandVerified = false;
            value.WindowObservationToken = structure + "-token";
            value.StructuralToken = structure;
            return value;
        }

        private sealed class QueueSource : IInputUiObservationSource
        {
            private readonly Queue<InputUiSnapshot> values;
            private int captures;
            internal int ThrowOnCaptureNumber;
            internal int BlockOnCaptureNumber;
            internal readonly ManualResetEvent CaptureEntered = new ManualResetEvent(false);
            internal readonly ManualResetEvent ReleaseCapture = new ManualResetEvent(false);

            internal QueueSource(params InputUiSnapshot[] values)
            {
                this.values = new Queue<InputUiSnapshot>(values);
            }

            public InputUiSnapshot Capture()
            {
                captures++;
                if (ThrowOnCaptureNumber == captures) throw new InvalidOperationException("synthetic capture failure");
                if (BlockOnCaptureNumber == captures)
                {
                    CaptureEntered.Set();
                    if (!ReleaseCapture.WaitOne(2000)) throw new TimeoutException("synthetic capture release timeout");
                }
                if (values.Count == 0) throw new InvalidOperationException("No synthetic observation remains.");
                return values.Dequeue();
            }
        }

        private sealed class FakeTransport : ITargetedWindowTransport
        {
            internal WindowEnvironment AtSend;
            internal WindowEnvironment After;
            internal bool SendAllowed = true;
            internal bool AttemptedOnFailure;
            internal bool CaptureAllowed = true;
            internal int SendCount;
            internal int ClickCount;
            internal int SendInputClickCount;
            internal int StagedSendInputClickCount;

            internal static FakeTransport Valid()
            {
                return new FakeTransport
                {
                    AtSend = Environment(37692, 0x00120ED8),
                    After = Environment(37692, 0x00120ED8)
                };
            }

            public bool TryPostMouseMove(WindowBinding expected, int clientX, int clientY, out WindowEnvironment environment, out bool attempted, out string error)
            {
                environment = AtSend;
                attempted = SendAllowed || AttemptedOnFailure;
                if (attempted) SendCount++;
                error = SendAllowed ? string.Empty : "synthetic send rejection";
                return SendAllowed;
            }

            public bool TryPostLeftClick(WindowBinding expected, int clientX, int clientY, out WindowEnvironment environment, out int attemptedMessageCount, out string error)
            {
                environment = AtSend;
                bool gate = SendAllowed && environment.TargetMatches(expected) && environment.TargetIsForeground(expected);
                attemptedMessageCount = gate ? 3 : AttemptedOnFailure ? 1 : 0;
                if (gate) ClickCount++;
                error = gate ? string.Empty : "synthetic click rejection";
                return gate;
            }

            public bool TrySendInputLeftClick(WindowBinding expected, int clientX, int clientY, out WindowEnvironment environment, out int attemptedInputCount, out string error)
            {
                environment = AtSend;
                bool gate = SendAllowed && environment.TargetMatches(expected) && environment.TargetIsForeground(expected);
                attemptedInputCount = gate ? 3 : AttemptedOnFailure ? 3 : 0;
                if (gate) SendInputClickCount++;
                error = gate ? string.Empty : "synthetic SendInput rejection";
                return gate;
            }

            public bool TrySendInputStagedLeftClick(WindowBinding expected, int clientX, int clientY, out WindowEnvironment environment, out int attemptedInputCount, out string error)
            {
                environment = AtSend;
                bool gate = SendAllowed && environment.TargetMatches(expected) && environment.TargetIsForeground(expected);
                attemptedInputCount = gate ? 3 : AttemptedOnFailure ? 3 : 0;
                if (gate) StagedSendInputClickCount++;
                error = gate ? string.Empty : "synthetic staged SendInput rejection";
                return gate;
            }

            public bool TryCapture(WindowBinding expected, out WindowEnvironment environment, out string error)
            {
                environment = After;
                error = CaptureAllowed ? string.Empty : "synthetic capture rejection";
                return CaptureAllowed;
            }

            private static WindowEnvironment Environment(int foregroundProcessId, long foregroundWindow)
            {
                return new WindowEnvironment
                {
                    TargetProcessId = 37692,
                    TargetProcessCreationFileTimeUtc = 639218891755456732,
                    TargetWindowHandle = 0x00120ED8,
                    TargetPath = WindowBinding.ExpectedExecutablePath,
                    ForegroundProcessId = foregroundProcessId,
                    ForegroundWindowHandle = foregroundWindow,
                    ClientWidth = 1024,
                    ClientHeight = 768
                };
            }
        }
    }
}
