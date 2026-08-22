using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Input.Win32;

namespace San9AutoDomestic.Input.SelfTest
{
    internal static class AllDirectCitiesDomesticPlanSyntheticTests
    {
        private const int ProcessId = 37692;
        private const long Generation = 639218891755456732;
        private const long WindowHandle = 0x00120ED8;

        internal static void Register(Action<string, Action> run)
        {
            run("all-cities plan discovers facilities rows instead of binding rows to sorted city IDs", DiscoversRowsInFacilitiesOrder);
            run("all-cities plan still identifies every row when every city is a known skip", KnownSkipsStillDiscoverEveryRow);
            run("all-cities plan blocks more than four rows before Escape or click", MoreThanFourRowsRejectsBeforeInput);
            run("all-cities plan rejects a changed direct-city set before the next row", DirectCitySetDriftRejects);
            run("all-cities plan aborts duplicate discovered city without another plan", DuplicateDiscoveryAborts);
            run("all-cities plan aborts discovered city outside frozen direct set", OutsideFrozenSetAborts);
            run("all-cities plan preserves safe stop at verified city root", VerifiedRootStopStaysStopped);
            run("all-cities plan stops after uncertain city plan", UncertainCityPlanStopsOuterBatch);
            run("all-cities plan shares the domestic batch lease", SharedLeaseRejectsOuterBatch);
            run("visible navigator discovers row identity and uses calibrated coordinates", VisibleNavigatorDiscoversAndUsesCoordinates);
            run("visible navigator accepts a selected row that directly opens its exact city menu", VisibleNavigatorAcceptsSelectedRowDirectOpen);
            run("visible navigator rejects a selected-row menu with disagreeing city identity", VisibleNavigatorRejectsSelectedRowWrongIdentity);
            run("visible navigator rejects a selected-row direct-open city outside the frozen set", VisibleNavigatorRejectsSelectedRowOutsideFrozenSet);
            run("visible navigator rejects a selected-row direct-open duplicate before facilities", VisibleNavigatorRejectsSelectedRowDuplicate);
            run("visible navigator blocks an already visited discovered city before facilities", VisibleNavigatorBlocksDuplicateBeforeFacilities);
            run("visible navigator propagates stop at verified city root", VisibleNavigatorStopsSafelyAtRoot);
            run("visible navigator performs at most two staged Escape transitions", VisibleNavigatorSupportsTwoStageEscape);
            run("visible navigator retains attempted click when delay throws", VisibleNavigatorRetainsClickWhenDelayThrows);
            run("visible navigator retains attempted Escape when delay throws", VisibleNavigatorRetainsEscapeWhenDelayThrows);
            run("visible navigator aborts when click transport throws after attempt", VisibleNavigatorAbortsClickTransportThrow);
            run("visible navigator aborts when Escape transport throws after attempt", VisibleNavigatorAbortsEscapeTransportThrow);
            run("domestic transport recovers attempted click from dispatch ledger", DomesticTransportRecoversClickAttemptFromLedger);
            run("all-cities outer preserves Escape child abort with zero recovered count", OuterPreservesEscapeChildAbort);
            run("all-cities outer preserves click child abort with zero recovered count", OuterPreservesClickChildAbort);
            run("visible navigator blocks unverified scrolling range", VisibleNavigatorBlocksBeyondFourRows);
        }

        private static void DiscoversRowsInFacilitiesOrder()
        {
            int[] frozen = { 0, 16, 42, 46 };
            int[] discovered = { 46, 0, 16, 42 };
            using (Fixture fixture = Fixture.Create(frozen, 42))
            {
                fixture.Source.AddUi(Menu(42));
                fixture.Navigator.EscapeResults.Enqueue(EscapeCompleted(42, 1));
                fixture.Source.AddUi(Map(42));
                for (int index = 0; index < discovered.Length; index++)
                {
                    int cityId = discovered[index];
                    fixture.Navigator.Results.Enqueue(NavigationCompleted(cityId, 3));
                    fixture.Source.AddUi(Menu(cityId));
                    fixture.Source.AddAvailability(DomesticCommandKind.Commerce, cityId, Available(cityId, 8, true));
                    fixture.Runner.Results.Enqueue(new PlanResponse(cityId, DomesticExecutionStatus.Completed, 1));
                    if (index + 1 < discovered.Length) fixture.Source.AddUi(Map(cityId));
                }

                AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);

                Equal(DomesticExecutionStatus.Completed, result.Status);
                SequenceEqual(frozen, result.DirectCityIds);
                SequenceEqual(discovered, result.CityResults.Select(item => item.CityId));
                SequenceEqual(new[] { 0, 1, 2, 3 }, fixture.Navigator.RowIndices);
                Equal(1, fixture.Navigator.EscapeCallCount);
                Equal(4, fixture.Navigator.CallCount);
                Equal(4, fixture.Runner.Requests.Count);
                Equal(17, result.InputActionCount);
                SequenceEqual(new int[0], fixture.Navigator.VisitedSets[0]);
                SequenceEqual(new[] { 46, 0, 16 }, fixture.Navigator.VisitedSets[3]);
                True(fixture.Navigator.AllLeasesHeld && fixture.Runner.AllLeasesHeld);
            }
        }

        private static void KnownSkipsStillDiscoverEveryRow()
        {
            using (Fixture fixture = Fixture.Create(new[] { 1, 2 }, 1))
            {
                fixture.Source.AddUi(Menu(1));
                fixture.Navigator.EscapeResults.Enqueue(EscapeCompleted(1, 1));
                fixture.Source.AddUi(Map(1));
                fixture.Navigator.Results.Enqueue(NavigationCompleted(2, 3));
                fixture.Source.AddUi(Menu(2));
                fixture.Source.AddAvailability(DomesticCommandKind.Commerce, 2, Available(2, 4, true));

                fixture.Source.AddUi(Menu(2));
                fixture.Navigator.EscapeResults.Enqueue(EscapeCompleted(2, 1));
                fixture.Source.AddUi(Map(2));
                fixture.Navigator.Results.Enqueue(NavigationCompleted(1, 3));
                fixture.Source.AddUi(Menu(1));
                fixture.Source.AddAvailability(DomesticCommandKind.Commerce, 1, Available(1, 8, false));

                AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);

                Equal(DomesticExecutionStatus.Completed, result.Status);
                SequenceEqual(new[] { 2, 1 }, result.CityResults.Select(item => item.CityId));
                Equal(DomesticExecutionStatus.SkippedFewerThanFive, result.CityResults[0].Status);
                Equal("CITY_ALL_COMMANDS_UNAVAILABLE", result.CityResults[1].Code);
                Equal(2, fixture.Navigator.CallCount);
                Equal(2, fixture.Navigator.EscapeCallCount);
                Equal(0, fixture.Runner.Requests.Count);
                Equal(8, result.InputActionCount);
            }
        }

        private static void MoreThanFourRowsRejectsBeforeInput()
        {
            using (Fixture fixture = Fixture.Create(new[] { 0, 1, 2, 3, 4 }, 0))
            {
                AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);
                Equal(DomesticExecutionStatus.RejectedBeforeInput, result.Status);
                Equal("DIRECT_CITY_VISIBLE_ROW_LIMIT_EXCEEDED", result.Code);
                Equal(0, result.InputActionCount);
                Equal(0, fixture.Navigator.EscapeCallCount);
                Equal(0, fixture.Navigator.CallCount);
            }
        }

        private static void DirectCitySetDriftRejects()
        {
            using (Fixture fixture = Fixture.Create(new[] { 1, 2 }, 1))
            {
                fixture.QueueSource.SetSequence(new[] { 1, 2 }, new[] { 1, 3 });
                AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);
                Equal(DomesticExecutionStatus.RejectedBeforeInput, result.Status);
                Equal("DIRECT_CITY_QUEUE_CHANGED", result.Code);
                Equal(0, result.InputActionCount);
                Equal(0, fixture.Navigator.CallCount);
            }
        }

        private static void DuplicateDiscoveryAborts()
        {
            using (Fixture fixture = Fixture.Create(new[] { 1, 2 }, 1))
            {
                fixture.Source.AddUi(Menu(1));
                fixture.Navigator.EscapeResults.Enqueue(EscapeCompleted(1, 1));
                fixture.Source.AddUi(Map(1));
                fixture.Navigator.Results.Enqueue(NavigationCompleted(2, 3));
                fixture.Source.AddUi(Menu(2));
                fixture.Source.AddAvailability(DomesticCommandKind.Commerce, 2, Available(2, 8, true));
                fixture.Runner.Results.Enqueue(new PlanResponse(2, DomesticExecutionStatus.Completed, 1));
                fixture.Source.AddUi(Map(2));
                fixture.Navigator.Results.Enqueue(NavigationCompleted(2, 2));

                AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal(2, fixture.Navigator.CallCount);
                Equal(1, fixture.Runner.Requests.Count);
                Equal(7, result.InputActionCount);
            }
        }

        private static void OutsideFrozenSetAborts()
        {
            using (Fixture fixture = Fixture.Create(new[] { 1 }, 1))
            {
                fixture.Source.AddUi(Menu(1));
                fixture.Navigator.EscapeResults.Enqueue(EscapeCompleted(1, 1));
                fixture.Source.AddUi(Map(1));
                fixture.Navigator.Results.Enqueue(NavigationCompleted(3, 2));

                AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal(3, result.InputActionCount);
                Equal(0, fixture.Runner.Requests.Count);
            }
        }

        private static void VerifiedRootStopStaysStopped()
        {
            using (Fixture fixture = Fixture.Create(new[] { 1 }, 1))
            {
                fixture.Source.AddUi(Menu(1));
                fixture.Navigator.EscapeResults.Enqueue(EscapeCompleted(1, 1));
                fixture.Source.AddUi(Map(1));
                fixture.Navigator.Results.Enqueue(new DirectCityNavigationResult
                {
                    Status = DomesticExecutionStatus.StoppedBeforeCommit,
                    Code = "STOPPED_AT_VERIFIED_CITY_ROOT",
                    Message = "safe",
                    InputActionCount = 2,
                    ObservedCityId = 1,
                    BindingStable = true,
                    StateVerified = true,
                    Evidence = new[] { Evidence("row"), Evidence("center") }
                });

                AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);

                Equal(DomesticExecutionStatus.StoppedBeforeCommit, result.Status);
                Equal("STOPPED_AT_VERIFIED_CITY_ROOT", result.Code);
                Equal(3, result.InputActionCount);
                Equal(0, fixture.Runner.Requests.Count);
            }
        }

        private static void UncertainCityPlanStopsOuterBatch()
        {
            using (Fixture fixture = Fixture.Create(new[] { 1 }, 1))
            {
                fixture.Source.AddUi(Menu(1));
                fixture.Navigator.EscapeResults.Enqueue(EscapeCompleted(1, 1));
                fixture.Source.AddUi(Map(1));
                fixture.Navigator.Results.Enqueue(NavigationCompleted(1, 3));
                fixture.Source.AddUi(Menu(1));
                fixture.Source.AddAvailability(DomesticCommandKind.Commerce, 1, Available(1, 8, true));
                fixture.Runner.Results.Enqueue(new PlanResponse(1, DomesticExecutionStatus.AbortUncertain, 1));

                AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);

                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal(1, result.CityResults.Count);
                Equal(1, fixture.Runner.Requests.Count);
                Equal(5, result.InputActionCount);
            }
        }

        private static void SharedLeaseRejectsOuterBatch()
        {
            using (Fixture fixture = Fixture.Create(new[] { 1 }, 1))
            {
                DomesticExecutionBatchLease.Lease held;
                True(DomesticExecutionBatchLease.TryAcquire(out held));
                using (held)
                {
                    AllDirectCitiesDomesticPlanResult result = fixture.Execute(null);
                    Equal(DomesticExecutionStatus.RejectedBeforeInput, result.Status);
                    Equal("GLOBAL_EXECUTION_BUSY", result.Code);
                }
                Equal(0, fixture.Source.UiCaptureCount);
            }
        }

        private static void VisibleNavigatorDiscoversAndUsesCoordinates()
        {
            FakeObservationSource source = new FakeObservationSource();
            source.AddUi(Map(42));
            source.AddUi(Menu(16));
            source.AddUi(Menu(16));
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(42), 42, 2, 4, new[] { 0, 16, 42, 46 }, new[] { 46, 0 }, TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal(16, result.ObservedCityId);
                Equal(3, result.InputActionCount);
                PointEqual(927, 355, transport.Clicks[0]);
                PointEqual(507, 364, transport.Clicks[1]);
                PointEqual(539, 379, transport.Clicks[2]);
            }
        }

        private static void VisibleNavigatorAcceptsSelectedRowDirectOpen()
        {
            FakeObservationSource source = new FakeObservationSource();
            source.AddUi(Menu(16));
            source.AddUi(Menu(16));
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(42), 42, 2, 4, new[] { 0, 16, 42, 46 }, new[] { 46, 0 }, TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal(16, result.ObservedCityId);
                Equal(2, result.InputActionCount);
                Equal(2, transport.Clicks.Count);
                PointEqual(927, 355, transport.Clicks[0]);
                PointEqual(539, 379, transport.Clicks[1]);
            }
        }

        private static void VisibleNavigatorRejectsSelectedRowWrongIdentity()
        {
            FakeObservationSource source = new FakeObservationSource();
            DomesticUiSnapshot wrong = Menu(16);
            wrong.DomesticTargetCityId = 42;
            source.AddUi(wrong);
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(42), 42, 1, 2, new[] { 16, 42 }, new int[0], TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("CITY_ROW_POSTCONDITION_REJECTED", result.Code);
                Equal(1, result.InputActionCount);
                Equal(1, transport.Clicks.Count);
            }
        }

        private static void VisibleNavigatorRejectsSelectedRowOutsideFrozenSet()
        {
            FakeObservationSource source = new FakeObservationSource();
            source.AddUi(Menu(99));
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(42), 42, 0, 2, new[] { 16, 42 }, new int[0], TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("CENTER_CITY_NOT_IN_FROZEN_DIRECT_SET", result.Code);
                Equal(1, result.InputActionCount);
                Equal(1, transport.Clicks.Count);
            }
        }

        private static void VisibleNavigatorRejectsSelectedRowDuplicate()
        {
            FakeObservationSource source = new FakeObservationSource();
            source.AddUi(Menu(16));
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(42), 42, 0, 2, new[] { 16, 42 }, new[] { 16 }, TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("CENTER_CITY_DUPLICATE", result.Code);
                Equal(1, result.InputActionCount);
                Equal(1, transport.Clicks.Count);
            }
        }

        private static void VisibleNavigatorBlocksDuplicateBeforeFacilities()
        {
            FakeObservationSource source = new FakeObservationSource();
            source.AddUi(Map(1));
            source.AddUi(Menu(2));
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(1), 1, 1, 2, new[] { 1, 2 }, new[] { 2 }, TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("CENTER_CITY_DUPLICATE", result.Code);
                Equal(2, result.InputActionCount);
                Equal(2, transport.Clicks.Count);
            }
        }

        private static void VisibleNavigatorStopsSafelyAtRoot()
        {
            FakeObservationSource source = new FakeObservationSource();
            DomesticExecutionStopSignal stop = new DomesticExecutionStopSignal();
            source.AfterUiCapture = delegate(int ordinal) { if (ordinal == 2) stop.RequestStop(); };
            source.AddUi(Map(1));
            source.AddUi(Menu(2));
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(1), 1, 0, 2, new[] { 1, 2 }, new int[0], TempEvidence(), stop, lease);
                Equal(DomesticExecutionStatus.StoppedBeforeCommit, result.Status);
                Equal(2, result.ObservedCityId);
                True(result.BindingStable && result.StateVerified);
                Equal(2, transport.Clicks.Count);
            }
        }

        private static void VisibleNavigatorSupportsTwoStageEscape()
        {
            FakeObservationSource source = new FakeObservationSource();
            source.AddUi(Menu(1));
            source.AddUi(Map(1));
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.EscapeToMap(
                    Guid.NewGuid(), Menu(1), TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.Completed, result.Status);
                Equal(2, result.InputActionCount);
                Equal(2, transport.EscapeCallCount);
                Equal(2, result.Evidence.Length);
            }
        }

        private static void VisibleNavigatorRetainsClickWhenDelayThrows()
        {
            FakeObservationSource source = new FakeObservationSource();
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(
                source,
                transport,
                delegate(int _) { throw new InvalidOperationException("synthetic delay failure"); });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(1), 1, 0, 1, new[] { 1 }, new int[0], TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("CITY_ROW_INPUT_FAILED", result.Code);
                Equal(1, result.InputActionCount);
                Equal(1, result.Evidence.Length);
                Equal(1, transport.Clicks.Count);
            }
        }

        private static void VisibleNavigatorRetainsEscapeWhenDelayThrows()
        {
            FakeObservationSource source = new FakeObservationSource();
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(
                source,
                transport,
                delegate(int _) { throw new InvalidOperationException("synthetic delay failure"); });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.EscapeToMap(
                    Guid.NewGuid(), Menu(1), TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("ESCAPE_INPUT_FAILED", result.Code);
                Equal(1, result.InputActionCount);
                Equal(1, result.Evidence.Length);
                Equal(1, transport.EscapeCallCount);
            }
        }

        private static void VisibleNavigatorAbortsClickTransportThrow()
        {
            FakeObservationSource source = new FakeObservationSource();
            FakeTransport transport = new FakeTransport { ThrowAfterAttemptClick = true };
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(
                source,
                transport,
                delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(1), 1, 0, 1, new[] { 1 }, new int[0], TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("CITY_NAVIGATOR_TRANSPORT_EXCEPTION", result.Code);
                Equal(1, transport.Clicks.Count);
            }
        }

        private static void VisibleNavigatorAbortsEscapeTransportThrow()
        {
            FakeObservationSource source = new FakeObservationSource();
            FakeTransport transport = new FakeTransport { ThrowAfterAttemptEscape = true };
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(
                source,
                transport,
                delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.EscapeToMap(
                    Guid.NewGuid(), Menu(1), TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
                Equal("ESCAPE_NAVIGATOR_TRANSPORT_EXCEPTION", result.Code);
                Equal(1, transport.EscapeCallCount);
            }
        }

        private static void DomesticTransportRecoversClickAttemptFromLedger()
        {
            ThrowingTargetedTransport targeted = new ThrowingTargetedTransport();
            Win32DomesticActionTransport transport = new Win32DomesticActionTransport(targeted);
            bool attempted;
            string error;
            bool sent = transport.TryClick(Binding(), 927, 315, out attempted, out error);
            True(!sent);
            True(attempted);
            True(!string.IsNullOrEmpty(error));
            Equal(1, targeted.LastProgramDispatch.AttemptedInputCount);
        }

        private static void OuterPreservesEscapeChildAbort()
        {
            AllDirectCitiesDomesticPlanResult result = ExecuteOuterWithThrowingTransport(true);
            Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
            Equal("ESCAPE_NAVIGATOR_TRANSPORT_EXCEPTION", result.Code);
            Equal(0, result.InputActionCount);
        }

        private static void OuterPreservesClickChildAbort()
        {
            AllDirectCitiesDomesticPlanResult result = ExecuteOuterWithThrowingTransport(false);
            Equal(DomesticExecutionStatus.AbortUncertain, result.Status);
            Equal("CITY_NAVIGATOR_TRANSPORT_EXCEPTION", result.Code);
            Equal(0, result.InputActionCount);
        }

        private static AllDirectCitiesDomesticPlanResult ExecuteOuterWithThrowingTransport(bool throwOnEscape)
        {
            string evidence = TempEvidence();
            try
            {
                FakeObservationSource source = new FakeObservationSource();
                source.AddUi(Menu(1));
                source.AddUi(throwOnEscape ? Menu(1) : Map(1));
                FakeQueueSource queue = new FakeQueueSource(new[] { 1 });
                FakeTransport transport = new FakeTransport
                {
                    ThrowAfterAttemptEscape = throwOnEscape,
                    ThrowAfterAttemptClick = !throwOnEscape
                };
                VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(
                    source,
                    transport,
                    delegate(int _) { });
                AllDirectCitiesDomesticPlanExecutor executor = new AllDirectCitiesDomesticPlanExecutor(
                    source,
                    queue,
                    navigator,
                    new FakeRunner());
                AllDirectCitiesDomesticPlanRequest request = new AllDirectCitiesDomesticPlanRequest(
                    Guid.NewGuid(),
                    new[] { DomesticCommandKind.Commerce },
                    evidence,
                    ProcessId,
                    Generation);
                return executor.Execute(request, null);
            }
            finally
            {
                if (Directory.Exists(evidence)) Directory.Delete(evidence, true);
            }
        }

        private static void VisibleNavigatorBlocksBeyondFourRows()
        {
            FakeObservationSource source = new FakeObservationSource();
            FakeTransport transport = new FakeTransport();
            VerifiedVisibleFacilitiesRowNavigator navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, delegate(int _) { });
            DomesticExecutionBatchLease.Lease lease;
            True(DomesticExecutionBatchLease.TryAcquire(out lease));
            using (lease)
            {
                DirectCityNavigationResult result = navigator.Navigate(
                    Guid.NewGuid(), Map(1), 1, 1, 5, new[] { 0, 1, 2, 3, 4 }, new int[0], TempEvidence(), null, lease);
                Equal(DomesticExecutionStatus.RejectedBeforeInput, result.Status);
                Equal("FACILITIES_VISIBLE_ROW_RANGE_UNAUTHORIZED", result.Code);
                Equal(0, transport.InputCallCount);
            }
        }

        private static DirectCityNavigationResult EscapeCompleted(int cityId, int inputs)
        {
            return NavigationResult(DomesticExecutionStatus.Completed, "escape-complete", cityId, inputs, true, true);
        }

        private static DirectCityNavigationResult NavigationCompleted(int cityId, int inputs)
        {
            return NavigationResult(DomesticExecutionStatus.Completed, "navigation-complete", cityId, inputs, true, true);
        }

        private static DirectCityNavigationResult NavigationResult(
            DomesticExecutionStatus status,
            string code,
            int cityId,
            int inputs,
            bool bindingStable,
            bool stateVerified)
        {
            List<DomesticExecutionEvidence> evidence = new List<DomesticExecutionEvidence>();
            for (int index = 0; index < inputs; index++) evidence.Add(Evidence(code + "-" + index));
            return new DirectCityNavigationResult
            {
                Status = status,
                Code = code,
                Message = string.Empty,
                InputActionCount = inputs,
                ObservedCityId = cityId,
                BindingStable = bindingStable,
                StateVerified = stateVerified,
                Evidence = evidence.ToArray()
            };
        }

        private static DomesticExecutionEvidence Evidence(string action)
        {
            return new DomesticExecutionEvidence(action, action + "-before.png", action + "-after.png");
        }

        private static DomesticAvailabilitySnapshot Available(int cityId, int readyCount, bool actionable)
        {
            int[] ready = Enumerable.Range(100 + cityId * 10, readyCount).ToArray();
            return new DomesticAvailabilitySnapshot
            {
                IsValid = true,
                Error = string.Empty,
                ProcessId = ProcessId,
                ProcessCreationFileTimeUtc = Generation,
                CityId = cityId,
                CorpsMoney = 1000,
                IsDirectlyControlled = true,
                CostPerOfficer = 50,
                KnownStaticSubsetWouldPass = actionable,
                OrderBitClearPassed = actionable,
                ReadyCandidatesInSourceOrder = ready,
                RankedCandidates = ready.Reverse().ToArray()
            };
        }

        private static DomesticUiSnapshot Menu(int cityId)
        {
            DomesticUiSnapshot value = Ui(UiLayerKind.DomesticCommandMenu);
            value.VerifiedCityId = cityId;
            value.DomesticTargetCityId = cityId;
            return value;
        }

        private static DomesticUiSnapshot Map(int cityId)
        {
            DomesticUiSnapshot value = Ui(UiLayerKind.StrategicMapCandidate);
            value.VerifiedCityId = null;
            value.DomesticTargetCityId = cityId;
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
                ProcessCreationFileTimeUtc = Generation,
                MainWindowHandle = WindowHandle,
                Layer = layer,
                WindowObservationToken = Guid.NewGuid().ToString("N"),
                CandidateOfficerIds = new int[0],
                SelectedOfficerIds = new int[0],
                WorkingOfficerIds = new int[0]
            };
        }

        private static WindowBinding Binding()
        {
            return new WindowBinding
            {
                ProcessId = ProcessId,
                ProcessCreationFileTimeUtc = Generation,
                MainWindowHandle = WindowHandle
            };
        }

        private static string TempEvidence()
        {
            return Path.Combine(Path.GetTempPath(), "San9AutoDomestic.Input.SelfTest", Guid.NewGuid().ToString("N"));
        }

        private static void PointEqual(int x, int y, DomesticExecutionPoint actual)
        {
            Equal(x, actual.X);
            Equal(y, actual.Y);
        }

        private static void SequenceEqual(IEnumerable<int> expected, IEnumerable<int> actual)
        {
            if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Integer sequences differ.");
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
            internal FakeObservationSource Source;
            internal FakeQueueSource QueueSource;
            internal FakeNavigator Navigator;
            internal FakeRunner Runner;
            internal AllDirectCitiesDomesticPlanExecutor Executor;
            internal AllDirectCitiesDomesticPlanRequest Request;
            internal string EvidenceDirectory;

            internal static Fixture Create(int[] cityIds, int currentCityId)
            {
                FakeObservationSource source = new FakeObservationSource();
                source.AddUi(Menu(currentCityId));
                FakeQueueSource queue = new FakeQueueSource(cityIds);
                FakeNavigator navigator = new FakeNavigator();
                FakeRunner runner = new FakeRunner();
                string evidence = TempEvidence();
                return new Fixture
                {
                    Source = source,
                    QueueSource = queue,
                    Navigator = navigator,
                    Runner = runner,
                    Executor = new AllDirectCitiesDomesticPlanExecutor(source, queue, navigator, runner),
                    Request = new AllDirectCitiesDomesticPlanRequest(
                        Guid.NewGuid(), new[] { DomesticCommandKind.Commerce }, evidence, ProcessId, Generation),
                    EvidenceDirectory = evidence
                };
            }

            internal AllDirectCitiesDomesticPlanResult Execute(DomesticExecutionStopSignal stopSignal)
            {
                return Executor.Execute(Request, stopSignal);
            }

            public void Dispose()
            {
                if (Directory.Exists(EvidenceDirectory)) Directory.Delete(EvidenceDirectory, true);
            }
        }

        private sealed class AvailabilityEntry
        {
            internal DomesticCommandKind Command;
            internal int CityId;
            internal DomesticAvailabilitySnapshot Snapshot;
        }

        private sealed class FakeObservationSource : IDomesticExecutionObservationSource
        {
            private readonly Queue<DomesticUiSnapshot> ui = new Queue<DomesticUiSnapshot>();
            private readonly Queue<AvailabilityEntry> availability = new Queue<AvailabilityEntry>();
            internal int UiCaptureCount;
            internal int AvailabilityCaptureCount;
            internal Action<int> AfterUiCapture;

            internal void AddUi(DomesticUiSnapshot snapshot) { ui.Enqueue(snapshot); }
            internal void AddAvailability(DomesticCommandKind command, int cityId, DomesticAvailabilitySnapshot snapshot)
            {
                availability.Enqueue(new AvailabilityEntry { Command = command, CityId = cityId, Snapshot = snapshot });
            }

            public DomesticUiSnapshot CaptureUi()
            {
                UiCaptureCount++;
                if (ui.Count == 0) throw new InvalidOperationException("No all-city UI snapshot remains.");
                DomesticUiSnapshot result = ui.Dequeue();
                if (AfterUiCapture != null) AfterUiCapture(UiCaptureCount);
                return result;
            }

            public DomesticAvailabilitySnapshot CaptureAvailability(DomesticCommandKind command, int cityId)
            {
                AvailabilityCaptureCount++;
                if (availability.Count == 0) throw new InvalidOperationException("No all-city availability remains.");
                AvailabilityEntry entry = availability.Dequeue();
                if (entry.Command != command || entry.CityId != cityId)
                    throw new InvalidOperationException("Unexpected all-city availability request.");
                return entry.Snapshot;
            }
        }

        private sealed class FakeQueueSource : IDomesticDirectCityQueueSource
        {
            private readonly int[] fallback;
            private readonly Queue<int[]> sequence = new Queue<int[]>();
            internal int CaptureCount;

            internal FakeQueueSource(int[] cityIds) { fallback = cityIds.ToArray(); }

            internal void SetSequence(params int[][] snapshots)
            {
                sequence.Clear();
                foreach (int[] snapshot in snapshots) sequence.Enqueue(snapshot.ToArray());
            }

            public DomesticDirectCityQueueSnapshot CaptureDirectCityQueue()
            {
                CaptureCount++;
                int[] cityIds = sequence.Count == 0 ? fallback : sequence.Dequeue();
                return new DomesticDirectCityQueueSnapshot
                {
                    IsValid = true,
                    Error = string.Empty,
                    ProcessId = ProcessId,
                    ProcessCreationFileTimeUtc = Generation,
                    CityIds = cityIds.ToArray()
                };
            }
        }

        private sealed class FakeNavigator : IAllDirectCitiesDomesticNavigator
        {
            internal readonly Queue<DirectCityNavigationResult> EscapeResults = new Queue<DirectCityNavigationResult>();
            internal readonly Queue<DirectCityNavigationResult> Results = new Queue<DirectCityNavigationResult>();
            internal readonly List<int> RowIndices = new List<int>();
            internal readonly List<int> DirectCounts = new List<int>();
            internal readonly List<int[]> FrozenSets = new List<int[]>();
            internal readonly List<int[]> VisitedSets = new List<int[]>();
            internal int CallCount;
            internal int EscapeCallCount;
            internal bool AllLeasesHeld = true;

            public DirectCityNavigationResult EscapeToMap(
                Guid authorizationId,
                DomesticUiSnapshot verifiedMenu,
                string evidenceDirectory,
                DomesticExecutionStopSignal stopSignal,
                DomesticExecutionBatchLease.Lease lease)
            {
                EscapeCallCount++;
                AllLeasesHeld &= lease != null && lease.IsHeld;
                if (EscapeResults.Count == 0) throw new InvalidOperationException("No Escape result remains.");
                return EscapeResults.Dequeue();
            }

            public DirectCityNavigationResult Navigate(
                Guid authorizationId,
                DomesticUiSnapshot verifiedSource,
                int sourceCityId,
                int targetRowIndex,
                int directCityCount,
                int[] frozenDirectCityIds,
                int[] visitedCityIds,
                string evidenceDirectory,
                DomesticExecutionStopSignal stopSignal,
                DomesticExecutionBatchLease.Lease lease)
            {
                CallCount++;
                RowIndices.Add(targetRowIndex);
                DirectCounts.Add(directCityCount);
                FrozenSets.Add(frozenDirectCityIds.ToArray());
                VisitedSets.Add(visitedCityIds.ToArray());
                AllLeasesHeld &= lease != null && lease.IsHeld;
                if (Results.Count == 0) throw new InvalidOperationException("No navigation result remains.");
                return Results.Dequeue();
            }
        }

        private sealed class PlanResponse
        {
            internal PlanResponse(int cityId, DomesticExecutionStatus status, int inputCount)
            {
                CityId = cityId;
                Status = status;
                InputCount = inputCount;
            }
            internal int CityId;
            internal DomesticExecutionStatus Status;
            internal int InputCount;
        }

        private sealed class FakeRunner : ISingleCityDomesticPlanRunner
        {
            internal readonly Queue<PlanResponse> Results = new Queue<PlanResponse>();
            internal readonly List<SingleCityDomesticPlanRequest> Requests = new List<SingleCityDomesticPlanRequest>();
            internal bool AllLeasesHeld = true;

            public SingleCityDomesticPlanResult Execute(
                SingleCityDomesticPlanRequest request,
                int expectedCityId,
                DomesticExecutionStopSignal stopSignal,
                DomesticExecutionBatchLease.Lease lease)
            {
                Requests.Add(request);
                AllLeasesHeld &= lease != null && lease.IsHeld;
                if (Results.Count == 0) throw new InvalidOperationException("No city-plan result remains.");
                PlanResponse response = Results.Dequeue();
                if (response.CityId != expectedCityId)
                    throw new InvalidOperationException("The outer executor passed the wrong expected city to the plan runner.");
                return new SingleCityDomesticPlanResult(
                    request,
                    response.Status,
                    response.Status == DomesticExecutionStatus.Completed ? "PLAN_COMPLETED" : "synthetic-plan-failure",
                    string.Empty,
                    response.CityId,
                    new List<SingleCityDomesticExecutionResult>(),
                    new List<DomesticExecutionEvidence>(),
                    response.InputCount);
            }
        }

        private sealed class FakeTransport : IDomesticActionTransport
        {
            internal readonly List<DomesticExecutionPoint> Clicks = new List<DomesticExecutionPoint>();
            internal int InputCallCount;
            internal int EscapeCallCount;
            internal bool ThrowAfterAttemptClick;
            internal bool ThrowAfterAttemptEscape;

            public bool TryActivate(WindowBinding expected, out string error)
            {
                error = string.Empty;
                return true;
            }

            public bool TryCaptureClientPng(WindowBinding expected, string path, out string error)
            {
                error = string.Empty;
                return true;
            }

            public bool TryHover(WindowBinding expected, int clientX, int clientY, out bool attempted, out string error)
            {
                return Input(out attempted, out error);
            }

            public bool TryClick(WindowBinding expected, int clientX, int clientY, out bool attempted, out string error)
            {
                Clicks.Add(new DomesticExecutionPoint(clientX, clientY));
                if (ThrowAfterAttemptClick)
                {
                    InputCallCount++;
                    attempted = true;
                    error = "synthetic click transport exception";
                    throw new InvalidOperationException(error);
                }
                return Input(out attempted, out error);
            }

            public bool TryPressReturn(WindowBinding expected, out bool attempted, out string error)
            {
                return Input(out attempted, out error);
            }

            public bool TryPressEscape(WindowBinding expected, out bool attempted, out string error)
            {
                EscapeCallCount++;
                if (ThrowAfterAttemptEscape)
                {
                    InputCallCount++;
                    attempted = true;
                    error = "synthetic Escape transport exception";
                    throw new InvalidOperationException(error);
                }
                return Input(out attempted, out error);
            }

            private bool Input(out bool attempted, out string error)
            {
                InputCallCount++;
                attempted = true;
                error = string.Empty;
                return true;
            }
        }

        private sealed class ThrowingTargetedTransport : ITargetedWindowTransport, IProgramInputDispatchTraceSource
        {
            public ProgramInputDispatchReport LastProgramDispatch { get; private set; }

            public bool TrySendInputStagedLeftClick(
                WindowBinding expected,
                int clientX,
                int clientY,
                out WindowEnvironment environment,
                out int attemptedInputCount,
                out string error)
            {
                environment = new WindowEnvironment();
                attemptedInputCount = 0;
                error = string.Empty;
                LastProgramDispatch = new ProgramInputDispatchReport
                {
                    ProcessId = expected.ProcessId,
                    ProcessCreationFileTimeUtc = expected.ProcessCreationFileTimeUtc,
                    MainWindowHandle = expected.MainWindowHandle,
                    ClientX = clientX,
                    ClientY = clientY,
                    AttemptedInputCount = 1
                };
                throw new InvalidOperationException("synthetic staged transport exception");
            }

            public bool TryPostMouseMove(WindowBinding expected, int clientX, int clientY, out WindowEnvironment environment, out bool attempted, out string error)
            {
                environment = new WindowEnvironment();
                attempted = false;
                error = "not used";
                return false;
            }

            public bool TryPostLeftClick(WindowBinding expected, int clientX, int clientY, out WindowEnvironment environment, out int attemptedMessageCount, out string error)
            {
                environment = new WindowEnvironment();
                attemptedMessageCount = 0;
                error = "not used";
                return false;
            }

            public bool TrySendInputLeftClick(WindowBinding expected, int clientX, int clientY, out WindowEnvironment environment, out int attemptedInputCount, out string error)
            {
                environment = new WindowEnvironment();
                attemptedInputCount = 0;
                error = "not used";
                return false;
            }

            public bool TryCapture(WindowBinding expected, out WindowEnvironment environment, out string error)
            {
                environment = new WindowEnvironment();
                error = "not used";
                return false;
            }
        }
    }
}
