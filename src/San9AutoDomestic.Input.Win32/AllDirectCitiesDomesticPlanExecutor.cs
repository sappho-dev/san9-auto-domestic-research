using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.Input.Win32
{
    internal interface IAllDirectCitiesDomesticNavigator
    {
        DirectCityNavigationResult EscapeToMap(
            Guid authorizationId,
            DomesticUiSnapshot verifiedMenu,
            string evidenceDirectory,
            DomesticExecutionStopSignal stopSignal,
            DomesticExecutionBatchLease.Lease lease);

        DirectCityNavigationResult Navigate(
            Guid authorizationId,
            DomesticUiSnapshot verifiedSource,
            int sourceCityId,
            int targetRowIndex,
            int directCityCount,
            int[] frozenDirectCityIds,
            int[] visitedCityIds,
            string evidenceDirectory,
            DomesticExecutionStopSignal stopSignal,
            DomesticExecutionBatchLease.Lease lease);
    }

    internal interface ISingleCityDomesticPlanRunner
    {
        SingleCityDomesticPlanResult Execute(
            SingleCityDomesticPlanRequest request,
            int expectedCityId,
            DomesticExecutionStopSignal stopSignal,
            DomesticExecutionBatchLease.Lease lease);
    }

    internal sealed class DirectCityNavigationResult
    {
        internal DirectCityNavigationResult()
        {
            Code = string.Empty;
            Message = string.Empty;
            Evidence = new DomesticExecutionEvidence[0];
        }

        internal DomesticExecutionStatus Status;
        internal string Code;
        internal string Message;
        internal int InputActionCount;
        internal int? ObservedCityId;
        internal bool BindingStable;
        internal bool StateVerified;
        internal DomesticExecutionEvidence[] Evidence;
    }

    internal sealed class VerifiedVisibleFacilitiesRowNavigator : IAllDirectCitiesDomesticNavigator
    {
        private const int VerifiedVisibleDirectRowLimit = 4;
        private const int RowClientX = 927;
        private const int FirstRowClientY = 315;
        private const int RowPitch = 20;
        private static readonly DomesticExecutionPoint CenterCity = new DomesticExecutionPoint(507, 364);
        private static readonly DomesticExecutionPoint OpenFacilities = new DomesticExecutionPoint(539, 379);
        private readonly IDomesticExecutionObservationSource observationSource;
        private readonly IDomesticActionTransport transport;
        private readonly Action<int> delay;

        internal VerifiedVisibleFacilitiesRowNavigator(
            IDomesticExecutionObservationSource observationSource,
            IDomesticActionTransport transport,
            Action<int> delay)
        {
            this.observationSource = observationSource;
            this.transport = transport;
            this.delay = delay;
        }

        public DirectCityNavigationResult EscapeToMap(
            Guid authorizationId,
            DomesticUiSnapshot verifiedMenu,
            string evidenceDirectory,
            DomesticExecutionStopSignal stopSignal,
            DomesticExecutionBatchLease.Lease lease)
        {
            NavigationState state = new NavigationState();
            try
            {
            WindowBinding binding = verifiedMenu == null ? null : SingleCityDomesticExecutor.CreateBinding(verifiedMenu);
            string error;
            if (lease == null || !lease.IsHeld)
                return Failure(state, DomesticExecutionStatus.RejectedBeforeInput, "BATCH_LEASE_REQUIRED", "The map bootstrap requires the active outer lease.");
            if (!SingleCityDomesticExecutor.ValidateUi(verifiedMenu, binding, UiLayerKind.DomesticCommandMenu, true, out error))
                return Failure(state, DomesticExecutionStatus.RejectedBeforeInput, "ESCAPE_SOURCE_MENU_REJECTED", error);
            int sourceCityId = verifiedMenu.VerifiedCityId.Value;
            for (int phase = 0; phase < 2; phase++)
            {
                if (stopSignal != null && stopSignal.IsStopRequested)
                {
                    if (state.InputActionCount == 0)
                        return Failure(state, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_BEFORE_ESCAPE", "Stop was requested before Escape.");
                    return StoppedAtVerifiedCity(state, sourceCityId);
                }

                ActionDispatch dispatch = DispatchEscape(state, binding, evidenceDirectory, "escape-domestic-menu-" + (phase + 1));
                if (!dispatch.Succeeded)
                    return Failure(state, StatusAfterDispatch(state, dispatch), "ESCAPE_INPUT_FAILED", dispatch.Error);
                DomesticUiSnapshot observed;
                if (!TryCaptureUi(out observed, out error))
                    return Failure(state, DomesticExecutionStatus.AbortUncertain, "ESCAPE_MAP_CAPTURE_FAILED", error);
                if (SingleCityDomesticExecutor.ValidateUi(observed, binding, UiLayerKind.StrategicMapCandidate, false, out error)
                    && observed.DomesticTargetCityId == sourceCityId)
                {
                    return new DirectCityNavigationResult
                    {
                        Status = DomesticExecutionStatus.Completed,
                        Code = "DOMESTIC_MENU_ESCAPE_COMPLETED",
                        Message = "Escape reached the exact source-city strategic map.",
                        InputActionCount = state.InputActionCount,
                        ObservedCityId = sourceCityId,
                        BindingStable = true,
                        StateVerified = true,
                        Evidence = state.Evidence.ToArray()
                    };
                }
                int menuCityId;
                if (phase == 0 && TryReadExactCityMenu(observed, binding, out menuCityId, out error)
                    && menuCityId == sourceCityId)
                    continue;
                return Failure(state, DomesticExecutionStatus.AbortUncertain, "ESCAPE_MAP_POSTCONDITION_REJECTED", string.IsNullOrEmpty(error) ? "Two staged Escape inputs did not reach the exact source-city strategic map." : error);
            }
            return Failure(state, DomesticExecutionStatus.AbortUncertain, "ESCAPE_MAP_POSTCONDITION_REJECTED", "Two staged Escape inputs did not reach the exact source-city strategic map.");
            }
            catch (Exception exception)
            {
                return Failure(
                    state,
                    DomesticExecutionStatus.AbortUncertain,
                    "ESCAPE_NAVIGATOR_TRANSPORT_EXCEPTION",
                    exception.GetType().Name + ": " + exception.Message);
            }
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
            NavigationState state = new NavigationState();
            try
            {
            WindowBinding binding = verifiedSource == null ? null : SingleCityDomesticExecutor.CreateBinding(verifiedSource);
            string error;
            if (lease == null || !lease.IsHeld)
                return Failure(state, DomesticExecutionStatus.RejectedBeforeInput, "BATCH_LEASE_REQUIRED", "The visible-row navigator requires the active outer lease.");
            if (directCityCount < 1 || directCityCount > VerifiedVisibleDirectRowLimit
                || targetRowIndex < 0 || targetRowIndex >= directCityCount)
                return Failure(state, DomesticExecutionStatus.RejectedBeforeInput, "FACILITIES_VISIBLE_ROW_RANGE_UNAUTHORIZED", "Only the first four empirically verified visible CITY rows are authorized; scrolling is not authorized.");
            if (frozenDirectCityIds == null || visitedCityIds == null
                || frozenDirectCityIds.Length != directCityCount
                || frozenDirectCityIds.Distinct().Count() != frozenDirectCityIds.Length
                || visitedCityIds.Distinct().Count() != visitedCityIds.Length
                || visitedCityIds.Any(cityId => !frozenDirectCityIds.Contains(cityId)))
                return Failure(state, DomesticExecutionStatus.RejectedBeforeInput, "FACILITIES_DISCOVERY_SET_REJECTED", "The frozen or visited direct-city discovery set is invalid.");
            if (!SingleCityDomesticExecutor.ValidateUi(verifiedSource, binding, UiLayerKind.StrategicMapCandidate, false, out error))
                return Failure(state, DomesticExecutionStatus.RejectedBeforeInput, "FACILITIES_MAP_SOURCE_REJECTED", error);
            if (verifiedSource.DomesticTargetCityId != sourceCityId)
                return Failure(state, DomesticExecutionStatus.RejectedBeforeInput, "FACILITIES_MAP_SOURCE_REJECTED", "The fresh map no longer identifies the verified source city.");
            if (stopSignal != null && stopSignal.IsStopRequested)
                return Failure(state, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_BEFORE_CITY_ROW", "Stop was requested before the CITY-row input.");

            ActionDispatch dispatch = Dispatch(
                state,
                binding,
                evidenceDirectory,
                "select-visible-city-row",
                new DomesticExecutionPoint(RowClientX, FirstRowClientY + RowPitch * targetRowIndex));
            if (!dispatch.Succeeded)
                return Failure(state, StatusAfterDispatch(state, dispatch), "CITY_ROW_INPUT_FAILED", dispatch.Error);
            DomesticUiSnapshot centered;
            if (!TryCaptureUi(out centered, out error))
                return Failure(state, DomesticExecutionStatus.AbortUncertain, "CITY_ROW_CAPTURE_FAILED", error);
            int discoveredCityId;
            if (!TryReadExactCityMenu(centered, binding, out discoveredCityId, out error))
            {
                if (!SingleCityDomesticExecutor.ValidateUi(centered, binding, UiLayerKind.StrategicMapCandidate, false, out error))
                    return Failure(state, DomesticExecutionStatus.AbortUncertain, "CITY_ROW_POSTCONDITION_REJECTED", error);
                if (stopSignal != null && stopSignal.IsStopRequested)
                    return Failure(state, DomesticExecutionStatus.AbortUncertain, "STOP_AFTER_UNVERIFIED_CITY_ROW", "Stop arrived after the row click, before the centered city could be identified by a menu.");

                dispatch = Dispatch(state, binding, evidenceDirectory, "open-centered-city", CenterCity);
                if (!dispatch.Succeeded)
                    return Failure(state, StatusAfterDispatch(state, dispatch), "CENTER_CITY_INPUT_FAILED", dispatch.Error);
                DomesticUiSnapshot rootMenu;
                if (!TryCaptureUi(out rootMenu, out error))
                    return Failure(state, DomesticExecutionStatus.AbortUncertain, "CENTER_CITY_CAPTURE_FAILED", error);
                if (!TryReadExactCityMenu(rootMenu, binding, out discoveredCityId, out error))
                    return Failure(state, DomesticExecutionStatus.AbortUncertain, "CENTER_CITY_IDENTITY_REJECTED", error);
            }
            if (!frozenDirectCityIds.Contains(discoveredCityId))
                return Failure(state, DomesticExecutionStatus.AbortUncertain, "CENTER_CITY_NOT_IN_FROZEN_DIRECT_SET", "The discovered CITY row is not in the frozen direct-city set.");
            if (visitedCityIds.Contains(discoveredCityId))
                return Failure(state, DomesticExecutionStatus.AbortUncertain, "CENTER_CITY_DUPLICATE", "The discovered CITY row repeats a city that was already visited.");
            if (stopSignal != null && stopSignal.IsStopRequested)
                return StoppedAtVerifiedCity(state, discoveredCityId);

            dispatch = Dispatch(state, binding, evidenceDirectory, "open-target-city-facilities", OpenFacilities);
            if (!dispatch.Succeeded)
                return Failure(state, StatusAfterDispatch(state, dispatch), "TARGET_FACILITIES_INPUT_FAILED", dispatch.Error);
            DomesticUiSnapshot facilities;
            if (!TryCaptureUi(out facilities, out error))
                return Failure(state, DomesticExecutionStatus.AbortUncertain, "TARGET_FACILITIES_CAPTURE_FAILED", error);
            if (!ValidateExactCityMenu(facilities, binding, discoveredCityId, out error))
                return Failure(state, DomesticExecutionStatus.AbortUncertain, "TARGET_FACILITIES_POSTCONDITION_REJECTED", error);

            return new DirectCityNavigationResult
            {
                Status = DomesticExecutionStatus.Completed,
                Code = "VISIBLE_CITY_ROW_NAVIGATION_COMPLETED",
                Message = "The verified visible CITY row was discovered, opened, and matched to the frozen direct-city set.",
                InputActionCount = state.InputActionCount,
                ObservedCityId = discoveredCityId,
                BindingStable = true,
                StateVerified = true,
                Evidence = state.Evidence.ToArray()
            };
            }
            catch (Exception exception)
            {
                return Failure(
                    state,
                    DomesticExecutionStatus.AbortUncertain,
                    "CITY_NAVIGATOR_TRANSPORT_EXCEPTION",
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private ActionDispatch Dispatch(
            NavigationState state,
            WindowBinding binding,
            string evidenceDirectory,
            string action,
            DomesticExecutionPoint point)
        {
            string error;
            if (!transport.TryActivate(binding, out error)) return ActionDispatch.Rejected(error);
            int ordinal = state.InputActionCount + 1;
            string beforePath = Path.Combine(evidenceDirectory, ordinal.ToString("00") + "-" + action + "-before.png");
            string afterPath = Path.Combine(evidenceDirectory, ordinal.ToString("00") + "-" + action + "-after.png");
            if (!transport.TryCaptureClientPng(binding, beforePath, out error)) return ActionDispatch.Rejected(error);
            bool attempted;
            bool sent = transport.TryClick(binding, point.X, point.Y, out attempted, out error);
            if (attempted) state.InputActionCount++;
            try
            {
                delay(150);
                string screenshotError;
                bool screenshot = transport.TryCaptureClientPng(binding, afterPath, out screenshotError);
                state.Evidence.Add(new DomesticExecutionEvidence(action, beforePath, screenshot ? afterPath : string.Empty));
                if (!sent) return attempted ? ActionDispatch.Uncertain(error, true) : ActionDispatch.Rejected(error);
                if (!screenshot) return ActionDispatch.Uncertain(screenshotError, true);
                return ActionDispatch.Completed(attempted);
            }
            catch (Exception exception)
            {
                state.Evidence.Add(new DomesticExecutionEvidence(action, beforePath, string.Empty));
                string exceptionError = exception.GetType().Name + ": " + exception.Message;
                return attempted || state.InputActionCount > 0
                    ? ActionDispatch.Uncertain(exceptionError, true)
                    : ActionDispatch.Rejected(exceptionError);
            }
        }

        private ActionDispatch DispatchEscape(
            NavigationState state,
            WindowBinding binding,
            string evidenceDirectory,
            string action)
        {
            string error;
            if (!transport.TryActivate(binding, out error)) return ActionDispatch.Rejected(error);
            int ordinal = state.InputActionCount + 1;
            string beforePath = Path.Combine(evidenceDirectory, ordinal.ToString("00") + "-" + action + "-before.png");
            string afterPath = Path.Combine(evidenceDirectory, ordinal.ToString("00") + "-" + action + "-after.png");
            if (!transport.TryCaptureClientPng(binding, beforePath, out error)) return ActionDispatch.Rejected(error);
            bool attempted;
            bool sent = transport.TryPressEscape(binding, out attempted, out error);
            if (attempted) state.InputActionCount++;
            try
            {
                delay(150);
                string screenshotError;
                bool screenshot = transport.TryCaptureClientPng(binding, afterPath, out screenshotError);
                state.Evidence.Add(new DomesticExecutionEvidence(action, beforePath, screenshot ? afterPath : string.Empty));
                if (!sent) return attempted ? ActionDispatch.Uncertain(error, true) : ActionDispatch.Rejected(error);
                if (!screenshot) return ActionDispatch.Uncertain(screenshotError, true);
                return ActionDispatch.Completed(attempted);
            }
            catch (Exception exception)
            {
                state.Evidence.Add(new DomesticExecutionEvidence(action, beforePath, string.Empty));
                string exceptionError = exception.GetType().Name + ": " + exception.Message;
                return attempted || state.InputActionCount > 0
                    ? ActionDispatch.Uncertain(exceptionError, true)
                    : ActionDispatch.Rejected(exceptionError);
            }
        }

        private static bool ValidateExactCityMenu(DomesticUiSnapshot snapshot, WindowBinding binding, int cityId, out string error)
        {
            int observedCityId;
            if (!TryReadExactCityMenu(snapshot, binding, out observedCityId, out error)) return false;
            if (observedCityId != cityId)
            {
                error = "The fresh menu did not independently verify the exact target city.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private bool TryCaptureUi(out DomesticUiSnapshot snapshot, out string error)
        {
            try
            {
                snapshot = observationSource.CaptureUi();
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                snapshot = null;
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool TryReadExactCityMenu(
            DomesticUiSnapshot snapshot,
            WindowBinding binding,
            out int cityId,
            out string error)
        {
            cityId = -1;
            if (!SingleCityDomesticExecutor.ValidateUi(snapshot, binding, UiLayerKind.DomesticCommandMenu, true, out error))
                return false;
            if (!snapshot.VerifiedCityId.HasValue
                || !snapshot.DomesticTargetCityId.HasValue
                || snapshot.VerifiedCityId.Value != snapshot.DomesticTargetCityId.Value)
            {
                error = "The fresh menu did not independently agree on one exact city.";
                return false;
            }
            cityId = snapshot.VerifiedCityId.Value;
            error = string.Empty;
            return true;
        }

        private static DirectCityNavigationResult StoppedAtVerifiedCity(NavigationState state, int cityId)
        {
            return new DirectCityNavigationResult
            {
                Status = DomesticExecutionStatus.StoppedBeforeCommit,
                Code = "STOPPED_AT_VERIFIED_CITY_ROOT",
                Message = "Stop was requested at the exact verified discovered-city root menu.",
                InputActionCount = state.InputActionCount,
                ObservedCityId = cityId,
                BindingStable = true,
                StateVerified = true,
                Evidence = state.Evidence.ToArray()
            };
        }

        private static DomesticExecutionStatus StatusAfterDispatch(NavigationState state, ActionDispatch dispatch)
        {
            return dispatch.Attempted || state.InputActionCount > 0
                ? DomesticExecutionStatus.AbortUncertain
                : DomesticExecutionStatus.RejectedBeforeInput;
        }

        private static DirectCityNavigationResult Failure(NavigationState state, DomesticExecutionStatus status, string code, string message)
        {
            return new DirectCityNavigationResult
            {
                Status = status,
                Code = code,
                Message = message,
                InputActionCount = state.InputActionCount,
                BindingStable = state.InputActionCount == 0,
                StateVerified = false,
                Evidence = state.Evidence.ToArray()
            };
        }

        private sealed class NavigationState
        {
            internal readonly List<DomesticExecutionEvidence> Evidence = new List<DomesticExecutionEvidence>();
            internal int InputActionCount;
        }
    }

    public sealed class AllDirectCitiesDomesticPlanExecutor
    {
        private readonly IDomesticExecutionObservationSource observationSource;
        private readonly IDomesticDirectCityQueueSource queueSource;
        private readonly IAllDirectCitiesDomesticNavigator navigator;
        private readonly ISingleCityDomesticPlanRunner planRunner;
        private int used;

        public AllDirectCitiesDomesticPlanExecutor()
        {
            AdapterDomesticExecutionObservationSource source = new AdapterDomesticExecutionObservationSource();
            Win32DomesticActionTransport transport = new Win32DomesticActionTransport();
            observationSource = source;
            queueSource = source;
            navigator = new VerifiedVisibleFacilitiesRowNavigator(source, transport, Thread.Sleep);
            planRunner = new SingleCityPlanRunner(source, transport, Thread.Sleep);
        }

        internal AllDirectCitiesDomesticPlanExecutor(
            IDomesticExecutionObservationSource observationSource,
            IDomesticDirectCityQueueSource queueSource,
            IAllDirectCitiesDomesticNavigator navigator,
            ISingleCityDomesticPlanRunner planRunner)
        {
            if (observationSource == null) throw new ArgumentNullException("observationSource");
            if (queueSource == null) throw new ArgumentNullException("queueSource");
            if (navigator == null) throw new ArgumentNullException("navigator");
            if (planRunner == null) throw new ArgumentNullException("planRunner");
            this.observationSource = observationSource;
            this.queueSource = queueSource;
            this.navigator = navigator;
            this.planRunner = planRunner;
        }

        public AllDirectCitiesDomesticPlanResult Execute(
            AllDirectCitiesDomesticPlanRequest request,
            DomesticExecutionStopSignal stopSignal)
        {
            if (request == null) throw new ArgumentNullException("request");
            List<int> cityIds = new List<int>();
            List<DirectCityDomesticPlanItemResult> cities = new List<DirectCityDomesticPlanItemResult>();
            List<DomesticExecutionEvidence> navigationEvidence = new List<DomesticExecutionEvidence>();
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "ALL_CITIES_EXECUTOR_ALREADY_USED", "The all-cities executor is single-use.", cityIds, cities, navigationEvidence, 0);

            DomesticExecutionBatchLease.Lease lease;
            if (!DomesticExecutionBatchLease.TryAcquire(out lease))
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "GLOBAL_EXECUTION_BUSY", "Another domestic input batch is active.", cityIds, cities, navigationEvidence, 0);

            using (lease)
            {
                int totalInput = 0;
                try
                {
                    Directory.CreateDirectory(request.EvidenceDirectory);
                    DomesticUiSnapshot initial = observationSource.CaptureUi();
                    string error;
                    if (!SingleCityDomesticExecutor.ValidateUi(initial, null, UiLayerKind.DomesticCommandMenu, true, out error))
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "ALL_CITIES_START_MENU_REJECTED", error, cityIds, cities, navigationEvidence, totalInput);
                    if (initial.ProcessId != request.ExpectedProcessId
                        || initial.ProcessCreationFileTimeUtc != request.ExpectedProcessCreationFileTimeUtc)
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "AUTHORIZED_PROCESS_GENERATION_MISMATCH", "The live game process differs from the user-authorized generation.", cityIds, cities, navigationEvidence, totalInput);
                    WindowBinding binding = SingleCityDomesticExecutor.CreateBinding(initial);
                    int currentCityId = initial.VerifiedCityId.Value;
                    if (initial.DomesticTargetCityId != currentCityId)
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "ALL_CITIES_START_CITY_REJECTED", "The starting menu did not independently agree on one exact city.", cityIds, cities, navigationEvidence, totalInput);

                    DomesticDirectCityQueueSnapshot queue = queueSource.CaptureDirectCityQueue();
                    if (!ValidateQueue(queue, binding, out error))
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "DIRECT_CITY_QUEUE_REJECTED", error, cityIds, cities, navigationEvidence, totalInput);
                    cityIds.AddRange(queue.CityIds);
                    if (cityIds.Count > 4)
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "DIRECT_CITY_VISIBLE_ROW_LIMIT_EXCEEDED", "More than four direct CITY rows requires unverified scrolling and is not authorized.", cityIds, cities, navigationEvidence, totalInput);
                    if (!cityIds.Contains(currentCityId))
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "CURRENT_CITY_NOT_DIRECT", "The currently open city is absent from the stable direct-city queue.", cityIds, cities, navigationEvidence, totalInput);

                    List<int> visitedCityIds = new List<int>();
                    for (int cityIndex = 0; cityIndex < cityIds.Count; cityIndex++)
                    {
                        if (stopSignal != null && stopSignal.IsStopRequested)
                            return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "ALL_CITIES_STOPPED_AT_BOUNDARY", "Stop was requested at a safe city boundary.", cityIds, cities, navigationEvidence, totalInput);

                        DomesticDirectCityQueueSnapshot currentQueue = queueSource.CaptureDirectCityQueue();
                        if (!ValidateFrozenQueue(currentQueue, binding, cityIds, out error))
                        {
                            return Result(request, totalInput == 0 ? DomesticExecutionStatus.RejectedBeforeInput : DomesticExecutionStatus.AbortUncertain,
                                "DIRECT_CITY_QUEUE_CHANGED", error, cityIds, cities, navigationEvidence, totalInput);
                        }

                        DomesticUiSnapshot mapSource = observationSource.CaptureUi();
                        int observedMenuCityId;
                        if (TryValidateExactMenu(mapSource, binding, out observedMenuCityId, out error))
                        {
                            if (observedMenuCityId != currentCityId)
                                return Result(request, totalInput == 0 ? DomesticExecutionStatus.RejectedBeforeInput : DomesticExecutionStatus.AbortUncertain,
                                    "ROW_SOURCE_MENU_CITY_CHANGED", "The menu city changed before the row scan.", cityIds, cities, navigationEvidence, totalInput);
                            DirectCityNavigationResult escape = navigator.EscapeToMap(
                                request.AuthorizationId,
                                mapSource,
                                Path.Combine(request.EvidenceDirectory, (cityIndex + 1).ToString("00") + "-row-escape"),
                                stopSignal,
                                lease);
                            if (escape == null) throw new InvalidOperationException("The city navigator returned no Escape result.");
                            totalInput += escape.InputActionCount;
                            if (escape.Evidence != null) navigationEvidence.AddRange(escape.Evidence);
                            if (escape.Status == DomesticExecutionStatus.StoppedBeforeCommit
                                && (escape.InputActionCount == 0
                                    || (escape.BindingStable && escape.StateVerified && escape.ObservedCityId == currentCityId)))
                                return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, escape.Code, escape.Message, cityIds, cities, navigationEvidence, totalInput);
                            if (escape.Status != DomesticExecutionStatus.Completed
                                || !escape.BindingStable || !escape.StateVerified
                                || escape.ObservedCityId != currentCityId)
                            {
                                DomesticExecutionStatus escapeStatus = escape.Status == DomesticExecutionStatus.StoppedBeforeCommit
                                    ? DomesticExecutionStatus.AbortUncertain
                                    : MergeChildFailureStatus(escape.Status, totalInput);
                                return Result(request, escapeStatus, escape.Code, escape.Message, cityIds, cities, navigationEvidence, totalInput);
                            }
                            mapSource = observationSource.CaptureUi();
                        }
                        if (!ValidateExactMap(mapSource, binding, currentCityId, out error))
                        {
                            return Result(request, totalInput == 0 ? DomesticExecutionStatus.RejectedBeforeInput : DomesticExecutionStatus.AbortUncertain,
                                "ROW_SOURCE_MAP_REJECTED", error, cityIds, cities, navigationEvidence, totalInput);
                        }
                        if (stopSignal != null && stopSignal.IsStopRequested)
                            return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "ALL_CITIES_STOPPED_BEFORE_ROW", "Stop was requested before the next CITY-row input.", cityIds, cities, navigationEvidence, totalInput);

                        DirectCityNavigationResult navigation = navigator.Navigate(
                            request.AuthorizationId,
                            mapSource,
                            currentCityId,
                            cityIndex,
                            cityIds.Count,
                            cityIds.ToArray(),
                            visitedCityIds.ToArray(),
                            Path.Combine(request.EvidenceDirectory, (cityIndex + 1).ToString("00") + "-row-navigation"),
                            stopSignal,
                            lease);
                        if (navigation == null) throw new InvalidOperationException("The city navigator returned no result.");
                        totalInput += navigation.InputActionCount;
                        if (navigation.Evidence != null) navigationEvidence.AddRange(navigation.Evidence);
                        bool discoveredIdentityIsSafe = navigation.BindingStable
                            && navigation.StateVerified
                            && navigation.ObservedCityId.HasValue
                            && cityIds.Contains(navigation.ObservedCityId.Value)
                            && !visitedCityIds.Contains(navigation.ObservedCityId.Value);
                        if (navigation.Status == DomesticExecutionStatus.StoppedBeforeCommit)
                        {
                            if (navigation.InputActionCount == 0 || discoveredIdentityIsSafe)
                            {
                                if (navigation.ObservedCityId.HasValue)
                                    cities.Add(Item(navigation.ObservedCityId.Value, DomesticExecutionStatus.StoppedBeforeCommit, navigation.Code, navigation.Message, null));
                                return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, navigation.Code, navigation.Message, cityIds, cities, navigationEvidence, totalInput);
                            }
                            return Result(request, DomesticExecutionStatus.AbortUncertain, "STOP_AFTER_UNVERIFIED_CITY_ROW", navigation.Message, cityIds, cities, navigationEvidence, totalInput);
                        }
                        if (navigation.Status != DomesticExecutionStatus.Completed || !discoveredIdentityIsSafe)
                            return Result(request, MergeChildFailureStatus(navigation.Status, totalInput),
                                navigation.Code, navigation.Message, cityIds, cities, navigationEvidence, totalInput);

                        int discoveredCityId = navigation.ObservedCityId.Value;
                        DomesticUiSnapshot afterNavigation = observationSource.CaptureUi();
                        int afterNavigationCityId;
                        if (!TryValidateExactMenu(afterNavigation, binding, out afterNavigationCityId, out error)
                            || afterNavigationCityId != discoveredCityId)
                            return Result(request, DomesticExecutionStatus.AbortUncertain, "CITY_NAVIGATION_POSTCONDITION_REJECTED", error, cityIds, cities, navigationEvidence, totalInput);
                        currentCityId = discoveredCityId;

                        CityPreflight preflight = CaptureCityPreflight(request.Commands, discoveredCityId, binding);
                        if (!preflight.IsValid)
                        {
                            cities.Add(Item(discoveredCityId, DomesticExecutionStatus.AbortUncertain, "CITY_PREFLIGHT_REJECTED", preflight.Error, null));
                            return Result(request, DomesticExecutionStatus.AbortUncertain,
                                "CITY_PREFLIGHT_REJECTED", preflight.Error, cityIds, cities, navigationEvidence, totalInput);
                        }
                        if (!preflight.IsDirectlyControlled)
                        {
                            cities.Add(Item(discoveredCityId, DomesticExecutionStatus.SkippedUnavailable, "CITY_NO_LONGER_DIRECT", "The city is no longer directly controlled.", null));
                            visitedCityIds.Add(discoveredCityId);
                            continue;
                        }
                        if (preflight.ReadyCount < 5)
                        {
                            cities.Add(Item(discoveredCityId, DomesticExecutionStatus.SkippedFewerThanFive, "CITY_FEWER_THAN_FIVE_READY", "Fewer than five ready officers remain for this city.", null));
                            visitedCityIds.Add(discoveredCityId);
                            continue;
                        }
                        if (!preflight.AnyActionable)
                        {
                            cities.Add(Item(discoveredCityId, DomesticExecutionStatus.SkippedUnavailable, "CITY_ALL_COMMANDS_UNAVAILABLE", "Every planned command is known unavailable or unaffordable.", null));
                            visitedCityIds.Add(discoveredCityId);
                            continue;
                        }
                        if (stopSignal != null && stopSignal.IsStopRequested)
                        {
                            cities.Add(Item(discoveredCityId, DomesticExecutionStatus.StoppedBeforeCommit, "ALL_CITIES_STOPPED_BEFORE_CITY_PLAN", "Stop was requested at the verified facilities-menu boundary.", null));
                            return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "ALL_CITIES_STOPPED_BEFORE_CITY_PLAN", "Stop was requested at the verified facilities-menu boundary.", cityIds, cities, navigationEvidence, totalInput);
                        }

                        string cityRoot = Path.Combine(request.EvidenceDirectory, (cityIndex + 1).ToString("00") + "-city-" + discoveredCityId, "plan");
                        SingleCityDomesticPlanRequest cityRequest = new SingleCityDomesticPlanRequest(
                            request.AuthorizationId,
                            request.Commands,
                            cityRoot,
                            request.ExpectedProcessId,
                            request.ExpectedProcessCreationFileTimeUtc);
                        SingleCityDomesticPlanResult cityPlan = planRunner.Execute(cityRequest, discoveredCityId, stopSignal, lease);
                        if (cityPlan == null) throw new InvalidOperationException("The city plan runner returned no result.");
                        totalInput += cityPlan.InputActionCount;
                        cities.Add(Item(discoveredCityId, cityPlan.Status, cityPlan.Code, cityPlan.Message, cityPlan));
                        if (cityPlan.Status == DomesticExecutionStatus.Completed)
                        {
                            visitedCityIds.Add(discoveredCityId);
                            continue;
                        }
                        DomesticExecutionStatus terminal = cityPlan.Status == DomesticExecutionStatus.RejectedBeforeInput && totalInput > 0
                            ? DomesticExecutionStatus.AbortUncertain : cityPlan.Status;
                        return Result(request, terminal, cityPlan.Code, cityPlan.Message, cityIds, cities, navigationEvidence, totalInput);
                    }

                    DomesticDirectCityQueueSnapshot finalQueue = queueSource.CaptureDirectCityQueue();
                    if (!ValidateFrozenQueue(finalQueue, binding, cityIds, out error))
                        return Result(request, totalInput == 0 ? DomesticExecutionStatus.RejectedBeforeInput : DomesticExecutionStatus.AbortUncertain,
                            "FINAL_DIRECT_CITY_QUEUE_CHANGED", error, cityIds, cities, navigationEvidence, totalInput);
                    if (visitedCityIds.Count != cityIds.Count
                        || visitedCityIds.Distinct().Count() != visitedCityIds.Count
                        || cityIds.Except(visitedCityIds).Any())
                        return Result(request, totalInput == 0 ? DomesticExecutionStatus.RejectedBeforeInput : DomesticExecutionStatus.AbortUncertain,
                            "DISCOVERED_CITY_SET_MISMATCH", "The discovered CITY-row set did not exactly match the frozen direct-city set.", cityIds, cities, navigationEvidence, totalInput);
                    return Result(request, DomesticExecutionStatus.Completed, "ALL_DIRECT_CITIES_PLAN_COMPLETED", "Every snapshotted direct city completed or was safely skipped.", cityIds, cities, navigationEvidence, totalInput);
                }
                catch (Exception exception)
                {
                    return Result(request,
                        totalInput == 0 ? DomesticExecutionStatus.RejectedBeforeInput : DomesticExecutionStatus.AbortUncertain,
                        totalInput == 0 ? "ALL_CITIES_EXECUTION_REJECTED" : "ALL_CITIES_EXCEPTION_AFTER_INPUT",
                        exception.GetType().Name + ": " + exception.Message,
                        cityIds,
                        cities,
                        navigationEvidence,
                        totalInput);
                }
            }
        }

        private CityPreflight CaptureCityPreflight(
            IEnumerable<DomesticCommandKind> commands,
            int cityId,
            WindowBinding binding)
        {
            CityPreflight result = new CityPreflight { IsValid = true, IsDirectlyControlled = true, ReadyCount = -1 };
            int[] expectedReady = null;
            foreach (DomesticCommandKind command in commands)
            {
                DomesticAvailabilitySnapshot snapshot = observationSource.CaptureAvailability(command, cityId);
                string error;
                if (!SingleCityDomesticExecutor.ValidateAvailability(snapshot, binding, cityId, out error))
                    return CityPreflight.Invalid(error);
                if (!snapshot.IsDirectlyControlled)
                {
                    result.IsDirectlyControlled = false;
                    result.ReadyCount = 0;
                    return result;
                }
                if (expectedReady == null)
                {
                    expectedReady = snapshot.ReadyCandidatesInSourceOrder;
                    result.ReadyCount = expectedReady.Length;
                }
                else if (!snapshot.ReadyCandidatesInSourceOrder.SequenceEqual(expectedReady))
                    return CityPreflight.Invalid("The ready-officer source order changed across the city preflight.");
                if (snapshot.KnownStaticSubsetWouldPass
                    && snapshot.CorpsMoney >= checked(snapshot.CostPerOfficer * 5))
                    result.AnyActionable = true;
            }
            return result;
        }

        private static bool ValidateQueue(DomesticDirectCityQueueSnapshot queue, WindowBinding binding, out string error)
        {
            if (queue == null || !queue.IsValid)
            {
                error = queue == null ? "No direct-city queue was returned." : queue.Error;
                return false;
            }
            if (queue.ProcessId != binding.ProcessId || queue.ProcessCreationFileTimeUtc != binding.ProcessCreationFileTimeUtc)
            {
                error = "The direct-city queue process binding changed.";
                return false;
            }
            if (queue.CityIds == null || queue.CityIds.Length == 0
                || queue.CityIds.Any(cityId => cityId < 0 || cityId >= 50)
                || queue.CityIds.Distinct().Count() != queue.CityIds.Length
                || !queue.CityIds.SequenceEqual(queue.CityIds.OrderBy(cityId => cityId)))
            {
                error = "The direct-city queue is empty, duplicated, invalid, or not in stable city-ID order.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static DomesticExecutionStatus MergeChildFailureStatus(
            DomesticExecutionStatus childStatus,
            int totalInput)
        {
            if (childStatus == DomesticExecutionStatus.AbortUncertain)
                return DomesticExecutionStatus.AbortUncertain;
            if (childStatus == DomesticExecutionStatus.StoppedBeforeCommit)
                return DomesticExecutionStatus.StoppedBeforeCommit;
            if (childStatus == DomesticExecutionStatus.RejectedBeforeInput)
                return totalInput > 0
                    ? DomesticExecutionStatus.AbortUncertain
                    : DomesticExecutionStatus.RejectedBeforeInput;
            return totalInput > 0
                ? DomesticExecutionStatus.AbortUncertain
                : DomesticExecutionStatus.RejectedBeforeInput;
        }

        private static bool ValidateFrozenQueue(
            DomesticDirectCityQueueSnapshot queue,
            WindowBinding binding,
            IEnumerable<int> frozenCityIds,
            out string error)
        {
            if (!ValidateQueue(queue, binding, out error)) return false;
            if (!queue.CityIds.SequenceEqual(frozenCityIds))
            {
                error = "The direct CITY set changed after the batch snapshot was frozen.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool TryValidateExactMenu(
            DomesticUiSnapshot snapshot,
            WindowBinding binding,
            out int cityId,
            out string error)
        {
            cityId = -1;
            if (!SingleCityDomesticExecutor.ValidateUi(snapshot, binding, UiLayerKind.DomesticCommandMenu, true, out error))
                return false;
            if (!snapshot.VerifiedCityId.HasValue
                || !snapshot.DomesticTargetCityId.HasValue
                || snapshot.VerifiedCityId.Value != snapshot.DomesticTargetCityId.Value)
            {
                error = "The fresh domestic menu did not independently agree on one exact city.";
                return false;
            }
            cityId = snapshot.VerifiedCityId.Value;
            error = string.Empty;
            return true;
        }

        private static bool ValidateExactMap(
            DomesticUiSnapshot snapshot,
            WindowBinding binding,
            int sourceCityId,
            out string error)
        {
            if (!SingleCityDomesticExecutor.ValidateUi(snapshot, binding, UiLayerKind.StrategicMapCandidate, false, out error))
                return false;
            if (snapshot.DomesticTargetCityId != sourceCityId)
            {
                error = "The fresh strategic map no longer identifies the verified source city.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static DirectCityDomesticPlanItemResult Item(int cityId, DomesticExecutionStatus status, string code, string message, SingleCityDomesticPlanResult plan)
        {
            return new DirectCityDomesticPlanItemResult(cityId, status, code, message, plan);
        }

        private static AllDirectCitiesDomesticPlanResult Result(
            AllDirectCitiesDomesticPlanRequest request,
            DomesticExecutionStatus status,
            string code,
            string message,
            IList<int> cityIds,
            IList<DirectCityDomesticPlanItemResult> cities,
            IList<DomesticExecutionEvidence> navigationEvidence,
            int inputActionCount)
        {
            return new AllDirectCitiesDomesticPlanResult(request, status, code, message, cityIds, cities, navigationEvidence, inputActionCount);
        }

        private sealed class CityPreflight
        {
            internal bool IsValid;
            internal string Error;
            internal bool IsDirectlyControlled;
            internal int ReadyCount;
            internal bool AnyActionable;

            internal static CityPreflight Invalid(string error)
            {
                return new CityPreflight { IsValid = false, Error = error ?? string.Empty };
            }
        }

        private sealed class SingleCityPlanRunner : ISingleCityDomesticPlanRunner
        {
            private readonly IDomesticExecutionObservationSource observationSource;
            private readonly IDomesticActionTransport transport;
            private readonly Action<int> delay;

            internal SingleCityPlanRunner(IDomesticExecutionObservationSource observationSource, IDomesticActionTransport transport, Action<int> delay)
            {
                this.observationSource = observationSource;
                this.transport = transport;
                this.delay = delay;
            }

            public SingleCityDomesticPlanResult Execute(
                SingleCityDomesticPlanRequest request,
                int expectedCityId,
                DomesticExecutionStopSignal stopSignal,
                DomesticExecutionBatchLease.Lease lease)
            {
                return new SingleCityDomesticPlanExecutor(observationSource, transport, delay)
                    .ExecuteWithinLease(request, stopSignal, lease, expectedCityId);
            }
        }
    }
}
