using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.Input.Win32
{
    public sealed class SingleCityDomesticPlanExecutor
    {
        private static readonly DomesticExecutionPoint CenterCurrentCity = new DomesticExecutionPoint(507, 364);
        private static readonly DomesticExecutionPoint OpenFacilities = new DomesticExecutionPoint(539, 379);
        private readonly IDomesticExecutionObservationSource observationSource;
        private readonly IDomesticActionTransport transport;
        private readonly Action<int> delay;
        private int used;

        public SingleCityDomesticPlanExecutor()
            : this(new AdapterDomesticExecutionObservationSource(), new Win32DomesticActionTransport(), Thread.Sleep)
        {
        }

        internal SingleCityDomesticPlanExecutor(
            IDomesticExecutionObservationSource observationSource,
            IDomesticActionTransport transport,
            Action<int> delay)
        {
            if (observationSource == null) throw new ArgumentNullException("observationSource");
            if (transport == null) throw new ArgumentNullException("transport");
            if (delay == null) throw new ArgumentNullException("delay");
            this.observationSource = observationSource;
            this.transport = transport;
            this.delay = delay;
        }

        public SingleCityDomesticPlanResult Execute(
            SingleCityDomesticPlanRequest request,
            DomesticExecutionStopSignal stopSignal)
        {
            if (request == null) throw new ArgumentNullException("request");
            List<SingleCityDomesticExecutionResult> tasks = new List<SingleCityDomesticExecutionResult>();
            List<DomesticExecutionEvidence> navigationEvidence = new List<DomesticExecutionEvidence>();
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "PLAN_EXECUTOR_ALREADY_USED", "A plan executor is single-use.", null, tasks, navigationEvidence, 0);

            DomesticExecutionBatchLease.Lease lease;
            if (!DomesticExecutionBatchLease.TryAcquire(out lease))
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "GLOBAL_EXECUTION_BUSY", "Another domestic input batch is active.", null, tasks, navigationEvidence, 0);

            using (lease)
            {
                return ExecuteCore(request, stopSignal, lease, null);
            }
        }

        internal SingleCityDomesticPlanResult ExecuteWithinLease(
            SingleCityDomesticPlanRequest request,
            DomesticExecutionStopSignal stopSignal,
            DomesticExecutionBatchLease.Lease lease)
        {
            if (request == null) throw new ArgumentNullException("request");
            List<SingleCityDomesticExecutionResult> tasks = new List<SingleCityDomesticExecutionResult>();
            List<DomesticExecutionEvidence> navigationEvidence = new List<DomesticExecutionEvidence>();
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "PLAN_EXECUTOR_ALREADY_USED", "A plan executor is single-use.", null, tasks, navigationEvidence, 0);
            if (lease == null || !lease.IsHeld)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "BATCH_LEASE_REQUIRED", "The internal city plan requires the active outer batch lease.", null, tasks, navigationEvidence, 0);
            return ExecuteCore(request, stopSignal, lease, null);
        }

        internal SingleCityDomesticPlanResult ExecuteWithinLease(
            SingleCityDomesticPlanRequest request,
            DomesticExecutionStopSignal stopSignal,
            DomesticExecutionBatchLease.Lease lease,
            int expectedCityId)
        {
            if (request == null) throw new ArgumentNullException("request");
            List<SingleCityDomesticExecutionResult> tasks = new List<SingleCityDomesticExecutionResult>();
            List<DomesticExecutionEvidence> navigationEvidence = new List<DomesticExecutionEvidence>();
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "PLAN_EXECUTOR_ALREADY_USED", "A plan executor is single-use.", null, tasks, navigationEvidence, 0);
            if (lease == null || !lease.IsHeld)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "BATCH_LEASE_REQUIRED", "The internal city plan requires the active outer batch lease.", null, tasks, navigationEvidence, 0);
            return ExecuteCore(request, stopSignal, lease, expectedCityId);
        }

        private SingleCityDomesticPlanResult ExecuteCore(
            SingleCityDomesticPlanRequest request,
            DomesticExecutionStopSignal stopSignal,
            DomesticExecutionBatchLease.Lease lease,
            int? expectedCityId)
        {
            List<SingleCityDomesticExecutionResult> tasks = new List<SingleCityDomesticExecutionResult>();
            List<DomesticExecutionEvidence> navigationEvidence = new List<DomesticExecutionEvidence>();
            int? initialCityId = null;
            int navigationInputCount = 0;
            try
            {
                    Directory.CreateDirectory(request.EvidenceDirectory);
                    DomesticUiSnapshot menu = observationSource.CaptureUi();
                    string error;
                    if (!SingleCityDomesticExecutor.ValidateUi(menu, null, UiLayerKind.DomesticCommandMenu, true, out error))
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "PLAN_START_MENU_REJECTED", error, null, tasks, navigationEvidence, 0);
                    if (menu.ProcessId != request.ExpectedProcessId
                        || menu.ProcessCreationFileTimeUtc != request.ExpectedProcessCreationFileTimeUtc)
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "AUTHORIZED_PROCESS_GENERATION_MISMATCH", "The current game PID or creation generation differs from the user-authorized diagnostic generation.", null, tasks, navigationEvidence, 0);
                    initialCityId = menu.VerifiedCityId;
                    if (expectedCityId.HasValue
                        && (initialCityId != expectedCityId || menu.DomesticTargetCityId != expectedCityId))
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "EXPECTED_CITY_MISMATCH", "The fresh city plan menu does not match the outer batch's discovered city.", initialCityId, tasks, navigationEvidence, 0);
                    WindowBinding binding = SingleCityDomesticExecutor.CreateBinding(menu);
                    bool onMap = false;

                    for (int index = 0; index < request.Commands.Count; index++)
                    {
                        DomesticCommandKind command = request.Commands[index];
                        string taskRoot = Path.Combine(
                            request.EvidenceDirectory,
                            (index + 1).ToString("00") + "-" + DomesticExecutionCoordinates.CommandName(command));
                        SingleCityDomesticExecutionRequest taskRequest = new SingleCityDomesticExecutionRequest(
                            request.AuthorizationId,
                            command,
                            Path.Combine(taskRoot, "command"));

                        if (stopSignal != null && stopSignal.IsStopRequested)
                        {
                            DomesticExecutionRunState stoppedState = new DomesticExecutionRunState { CityId = initialCityId };
                            SingleCityDomesticExecutionResult stopped = SingleCityDomesticExecutor.Result(
                                taskRequest,
                                DomesticExecutionStatus.StoppedBeforeCommit,
                                "PLAN_STOPPED_AT_BOUNDARY",
                                "Stop was requested at a safe task boundary.",
                                stoppedState);
                            tasks.Add(stopped);
                            return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, stopped.Code, stopped.Message, initialCityId, tasks, navigationEvidence, TotalInput(tasks, navigationInputCount));
                        }

                        DomesticAvailabilitySnapshot availability = observationSource.CaptureAvailability(command, initialCityId.Value);
                        DomesticPreparedStep prepared = new DomesticPreparedStep
                        {
                            Binding = binding,
                            CityId = initialCityId.Value,
                            Availability = availability,
                            Menu = menu
                        };

                        string availabilityError;
                        bool availabilityValid = SingleCityDomesticExecutor.ValidateAvailability(
                            availability,
                            binding,
                            initialCityId.Value,
                            out availabilityError);
                        bool knownSkip = availabilityValid && IsKnownSkip(availability);
                        bool navigated = false;
                        if (availabilityValid && !knownSkip && onMap
                            && (stopSignal == null || !stopSignal.IsStopRequested))
                        {
                            NavigationRunState navigation = new NavigationRunState(taskRoot);
                            try
                            {
                                PrepareMenuFromMap(binding, initialCityId.Value, stopSignal, navigation, prepared);
                            }
                            finally
                            {
                                navigationInputCount += navigation.InputActionCount;
                                navigationEvidence.AddRange(navigation.Evidence);
                            }
                            navigated = true;
                            prepared.MenuPreparationAttempted = navigation.InputActionCount > 0;
                            if (!prepared.MenuPreparationFailed) menu = prepared.Menu;
                        }

                        if (availabilityValid && !knownSkip && !prepared.MenuPreparationFailed)
                        {
                            if (navigated)
                            {
                                availability = observationSource.CaptureAvailability(command, initialCityId.Value);
                                prepared.Availability = availability;
                                availabilityValid = SingleCityDomesticExecutor.ValidateAvailability(
                                    availability,
                                    binding,
                                    initialCityId.Value,
                                    out availabilityError);
                                knownSkip = availabilityValid && IsKnownSkip(availability);
                            }
                            if (availabilityValid && !knownSkip)
                            {
                                DomesticUiSnapshot freshMenu = observationSource.CaptureUi();
                                prepared.Menu = freshMenu;
                                menu = freshMenu;
                            }
                        }

                        SingleCityDomesticExecutor stepExecutor = new SingleCityDomesticExecutor(
                            observationSource,
                            transport,
                            delay);
                        SingleCityDomesticExecutionResult taskResult = stepExecutor.ExecutePreparedWithinLease(
                            taskRequest,
                            stopSignal,
                            lease,
                            prepared);
                        tasks.Add(taskResult);

                        if (taskResult.Status == DomesticExecutionStatus.Completed)
                        {
                            onMap = true;
                            menu = null;
                            continue;
                        }
                        if (taskResult.Status == DomesticExecutionStatus.SkippedUnavailable
                            || taskResult.Status == DomesticExecutionStatus.SkippedFewerThanFive)
                        {
                            if (navigated)
                            {
                                onMap = false;
                                menu = prepared.Menu;
                            }
                            continue;
                        }

                        int terminalTotal = TotalInput(tasks, navigationInputCount);
                        DomesticExecutionStatus terminalStatus = taskResult.Status == DomesticExecutionStatus.RejectedBeforeInput
                            && terminalTotal > 0
                                ? DomesticExecutionStatus.AbortUncertain
                                : taskResult.Status;
                        return Result(
                            request,
                            terminalStatus,
                            taskResult.Code,
                            taskResult.Message,
                            initialCityId,
                            tasks,
                            navigationEvidence,
                            TotalInput(tasks, navigationInputCount));
                    }

                    return Result(
                        request,
                        DomesticExecutionStatus.Completed,
                        "PLAN_COMPLETED",
                        "All planned commands completed or were safely skipped.",
                        initialCityId,
                        tasks,
                        navigationEvidence,
                        TotalInput(tasks, navigationInputCount));
            }
            catch (Exception exception)
            {
                int total = TotalInput(tasks, navigationInputCount);
                return Result(
                    request,
                    total == 0 ? DomesticExecutionStatus.RejectedBeforeInput : DomesticExecutionStatus.AbortUncertain,
                    total == 0 ? "PLAN_EXECUTION_REJECTED" : "PLAN_EXECUTION_EXCEPTION_AFTER_INPUT",
                    exception.GetType().Name + ": " + exception.Message,
                    initialCityId,
                    tasks,
                    navigationEvidence,
                    total);
            }
        }

        private void PrepareMenuFromMap(
            WindowBinding binding,
            int cityId,
            DomesticExecutionStopSignal stopSignal,
            NavigationRunState state,
            DomesticPreparedStep prepared)
        {
            DomesticUiSnapshot freshMap = observationSource.CaptureUi();
            string error;
            if (!SingleCityDomesticExecutor.ValidateUi(freshMap, binding, UiLayerKind.StrategicMapCandidate, false, out error)
                || freshMap.DomesticTargetCityId != cityId)
            {
                FailPreparation(prepared, false, false, "NAVIGATION_MAP_REJECTED", string.IsNullOrEmpty(error) ? "The fresh map did not preserve the exact target city." : error);
                return;
            }
            if (stopSignal != null && stopSignal.IsStopRequested)
            {
                FailPreparation(prepared, false, true, "STOPPED_BEFORE_NAVIGATION", "Stop was requested after the fresh map gate and before any navigation input.");
                return;
            }
            ActionDispatch dispatch = DispatchNavigation(state, binding, "reopen-center-city", CenterCurrentCity);
            if (!dispatch.Succeeded)
            {
                FailPreparation(prepared, dispatch.Attempted || state.InputActionCount > 0, false, "NAVIGATE_CENTER_CITY_FAILED", dispatch.Error);
                return;
            }
            DomesticUiSnapshot centerMenu = observationSource.CaptureUi();
            if (!SingleCityDomesticExecutor.ValidateUi(centerMenu, binding, UiLayerKind.DomesticCommandMenu, true, out error)
                || centerMenu.VerifiedCityId != cityId)
            {
                FailPreparation(prepared, true, false, "NAVIGATE_CENTER_CITY_STATE_REJECTED", string.IsNullOrEmpty(error) ? "The center-city action did not preserve the exact city." : error);
                return;
            }
            if (stopSignal != null && stopSignal.IsStopRequested)
            {
                FailPreparation(prepared, true, true, "STOPPED_DURING_NAVIGATION", "Stop was requested after the center-city state was verified.");
                return;
            }

            dispatch = DispatchNavigation(state, binding, "reopen-facilities", OpenFacilities);
            if (!dispatch.Succeeded)
            {
                FailPreparation(prepared, dispatch.Attempted || state.InputActionCount > 0, false, "NAVIGATE_FACILITIES_FAILED", dispatch.Error);
                return;
            }
            DomesticUiSnapshot facilities = observationSource.CaptureUi();
            if (!SingleCityDomesticExecutor.ValidateUi(facilities, binding, UiLayerKind.DomesticCommandMenu, true, out error)
                || facilities.VerifiedCityId != cityId)
            {
                FailPreparation(prepared, true, false, "NAVIGATE_FACILITIES_STATE_REJECTED", string.IsNullOrEmpty(error) ? "The facilities action did not preserve the exact city." : error);
                return;
            }
            prepared.Menu = facilities;
        }

        private ActionDispatch DispatchNavigation(
            NavigationRunState state,
            WindowBinding binding,
            string action,
            DomesticExecutionPoint point)
        {
            string error;
            if (!transport.TryActivate(binding, out error)) return ActionDispatch.Rejected(error);
            int ordinal = state.InputActionCount + 1;
            string directory = Path.Combine(state.TaskRoot, "navigation");
            string beforePath = Path.Combine(directory, ordinal.ToString("00") + "-" + action + "-before.png");
            string afterPath = Path.Combine(directory, ordinal.ToString("00") + "-" + action + "-after.png");
            if (!transport.TryCaptureClientPng(binding, beforePath, out error)) return ActionDispatch.Rejected(error);
            bool attempted;
            bool sent = transport.TryClick(binding, point.X, point.Y, out attempted, out error);
            if (attempted) state.InputActionCount++;
            delay(150);
            string screenshotError;
            bool screenshot = transport.TryCaptureClientPng(binding, afterPath, out screenshotError);
            state.Evidence.Add(new DomesticExecutionEvidence(action, beforePath, screenshot ? afterPath : string.Empty));
            if (!sent) return attempted ? ActionDispatch.Uncertain(error, true) : ActionDispatch.Rejected(error);
            if (!screenshot) return ActionDispatch.Uncertain(screenshotError, true);
            return ActionDispatch.Completed(attempted);
        }

        private static bool IsKnownSkip(DomesticAvailabilitySnapshot availability)
        {
            return !availability.IsDirectlyControlled
                || !availability.KnownStaticSubsetWouldPass
                || availability.ReadyCandidatesInSourceOrder.Length < 5
                || availability.CorpsMoney < checked(availability.CostPerOfficer * 5);
        }

        private static void FailPreparation(
            DomesticPreparedStep prepared,
            bool attempted,
            bool stopped,
            string code,
            string error)
        {
            prepared.MenuPreparationFailed = true;
            prepared.MenuPreparationAttempted = attempted;
            prepared.MenuPreparationStopped = stopped;
            prepared.MenuPreparationCode = code;
            prepared.MenuPreparationError = error ?? string.Empty;
        }

        private static int TotalInput(IList<SingleCityDomesticExecutionResult> tasks, int navigationInputCount)
        {
            return navigationInputCount + tasks.Sum(task => task.InputActionCount);
        }

        private static SingleCityDomesticPlanResult Result(
            SingleCityDomesticPlanRequest request,
            DomesticExecutionStatus status,
            string code,
            string message,
            int? initialCityId,
            IList<SingleCityDomesticExecutionResult> tasks,
            IList<DomesticExecutionEvidence> navigationEvidence,
            int inputActionCount)
        {
            return new SingleCityDomesticPlanResult(
                request,
                status,
                code,
                message,
                initialCityId,
                tasks,
                navigationEvidence,
                inputActionCount);
        }

        private sealed class NavigationRunState
        {
            internal NavigationRunState(string taskRoot)
            {
                TaskRoot = taskRoot;
                Evidence = new List<DomesticExecutionEvidence>();
            }

            internal readonly string TaskRoot;
            internal int InputActionCount;
            internal readonly List<DomesticExecutionEvidence> Evidence;
        }
    }
}
