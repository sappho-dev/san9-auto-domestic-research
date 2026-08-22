using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.Input.Win32
{
    public sealed class SingleCityDomesticExecutor
    {
        private const int InitialPostCommitDelayMilliseconds = 500;
        private const int PostCommitPollIntervalMilliseconds = 250;
        private const int MaximumPostCommitWaitMilliseconds = 5000;
        private readonly IDomesticExecutionObservationSource observationSource;
        private readonly IDomesticActionTransport transport;
        private readonly Action<int> delay;
        private int used;

        public SingleCityDomesticExecutor()
            : this(new AdapterDomesticExecutionObservationSource(), new Win32DomesticActionTransport(), Thread.Sleep)
        {
        }

        internal SingleCityDomesticExecutor(
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

        public SingleCityDomesticExecutionResult Execute(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionStopSignal stopSignal)
        {
            if (request == null) throw new ArgumentNullException("request");
            DomesticExecutionRunState state = new DomesticExecutionRunState();
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "EXECUTOR_ALREADY_USED", "An executor instance is single-use.", state);
            DomesticExecutionBatchLease.Lease lease;
            if (!DomesticExecutionBatchLease.TryAcquire(out lease))
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "GLOBAL_EXECUTION_BUSY", "Another domestic input batch is active.", state);
            using (lease)
            {
                return ExecuteCore(request, stopSignal, null);
            }
        }

        internal SingleCityDomesticExecutionResult ExecutePreparedWithinLease(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionStopSignal stopSignal,
            DomesticExecutionBatchLease.Lease lease,
            DomesticPreparedStep prepared)
        {
            if (request == null) throw new ArgumentNullException("request");
            DomesticExecutionRunState state = new DomesticExecutionRunState();
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "EXECUTOR_ALREADY_USED", "An executor instance is single-use.", state);
            if (lease == null || !lease.IsHeld)
                return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "BATCH_LEASE_REQUIRED", "The internal step requires the active plan lease.", state);
            if (prepared == null) throw new ArgumentNullException("prepared");
            return ExecuteCore(request, stopSignal, prepared);
        }

        private SingleCityDomesticExecutionResult ExecuteCore(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionStopSignal stopSignal,
            DomesticPreparedStep prepared)
        {
            DomesticExecutionRunState state = new DomesticExecutionRunState();
            try
            {
                try { Directory.CreateDirectory(request.EvidenceDirectory); }
                catch (Exception exception)
                {
                    return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "EVIDENCE_DIRECTORY_FAILED", exception.Message, state);
                }

                DomesticUiSnapshot menu;
                WindowBinding binding;
                DomesticAvailabilitySnapshot before;
                string error;
                if (prepared == null)
                {
                    menu = observationSource.CaptureUi();
                    if (!ValidateUi(menu, null, UiLayerKind.DomesticCommandMenu, true, out error))
                        return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "START_MENU_REJECTED", error, state);
                    state.CityId = menu.VerifiedCityId;
                    binding = CreateBinding(menu);
                    before = observationSource.CaptureAvailability(request.Command, state.CityId.Value);
                }
                else
                {
                    menu = prepared.Menu;
                    state.CityId = prepared.CityId;
                    binding = prepared.Binding;
                    before = prepared.Availability;
                }
                if (!ValidateAvailability(before, binding, state.CityId.Value, out error))
                    return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "PRE_AVAILABILITY_REJECTED", error, state);
                state.MoneyBefore = before.CorpsMoney;
                state.ReadyBefore = before.ReadyCandidatesInSourceOrder.Length;
                if (!before.IsDirectlyControlled)
                    return Result(request, DomesticExecutionStatus.SkippedUnavailable, "CITY_NOT_DIRECTLY_CONTROLLED", "The city is delegated or no longer directly controlled.", state);
                if (!before.KnownStaticSubsetWouldPass)
                    return Result(request, DomesticExecutionStatus.SkippedUnavailable, "COMMAND_UNAVAILABLE", "The exact static command gate is disabled (including an already-used/grey command).", state);
                if (before.ReadyCandidatesInSourceOrder.Length < 5)
                    return Result(request, DomesticExecutionStatus.SkippedFewerThanFive, "FEWER_THAN_FIVE_READY", "Fewer than five ready officers remain; no input was sent.", state);
                int requiredMoney = checked(before.CostPerOfficer * 5);
                if (before.CorpsMoney < requiredMoney)
                    return Result(request, DomesticExecutionStatus.SkippedUnavailable, "INSUFFICIENT_MONEY_FOR_FIVE", "The corps cannot pay for exactly five officers.", state);
                int[] expectedFive = before.RankedCandidates.Take(5).ToArray();

                if (prepared != null && prepared.MenuPreparationFailed)
                    return Result(
                        request,
                        prepared.MenuPreparationStopped
                            ? DomesticExecutionStatus.StoppedBeforeCommit
                            : prepared.MenuPreparationAttempted ? DomesticExecutionStatus.AbortUncertain : DomesticExecutionStatus.RejectedBeforeInput,
                        prepared.MenuPreparationCode,
                        prepared.MenuPreparationError,
                        state);
                if (StopRequested(stopSignal))
                    return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_BEFORE_INPUT", "Stop was requested before any input.", state);
                if (prepared != null && (!ValidateUi(menu, binding, UiLayerKind.DomesticCommandMenu, true, out error)
                    || menu.VerifiedCityId != state.CityId))
                    return Result(
                        request,
                        prepared.MenuPreparationAttempted ? DomesticExecutionStatus.AbortUncertain : DomesticExecutionStatus.RejectedBeforeInput,
                        "START_MENU_REJECTED",
                        string.IsNullOrEmpty(error) ? "The prepared domestic menu city changed." : error,
                        state);

                DomesticCommandKind probeCommand;
                if (!TryResolveSurfaceProbe(request.Command, binding, state.CityId.Value, out probeCommand, out error))
                    return Result(request, DomesticExecutionStatus.RejectedBeforeInput, "SURFACE_PROBE_UNAVAILABLE", error, state);
                if (StopRequested(stopSignal))
                    return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_BEFORE_SURFACE_PROBE", "Stop was requested before the surface-probe input.", state);
                ActionDispatch dispatch = Hover(
                    request,
                    state,
                    binding,
                    "probe-domestic-surface",
                    DomesticExecutionCoordinates.ForCommand(probeCommand));
                if (!dispatch.Succeeded)
                    return DispatchFailure(request, state, dispatch, "SURFACE_PROBE_HOVER_FAILED");
                DomesticUiSnapshot surface = observationSource.CaptureUi();
                if (!ValidateUi(surface, binding, UiLayerKind.DomesticCommandMenu, true, out error)
                    || surface.VerifiedCityId != state.CityId
                    || !surface.HoveredCommandVerified
                    || !string.Equals(surface.HoveredCommand, DomesticExecutionCoordinates.CommandName(probeCommand), StringComparison.Ordinal))
                    return Uncertain(request, state, "SURFACE_PROBE_REJECTED", string.IsNullOrEmpty(error) ? "The fresh domestic surface did not verify the same city and expected command hover." : error);

                if (StopRequested(stopSignal))
                    return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_AFTER_SURFACE_PROBE", "Stop was requested after the domestic surface probe.", state);

                dispatch = Click(request, state, binding, "open-command", DomesticExecutionCoordinates.ForCommand(request.Command));
                if (!dispatch.Succeeded) return DispatchFailure(request, state, dispatch, "OPEN_COMMAND_FAILED");
                DomesticUiSnapshot outer = observationSource.CaptureUi();
                if (!ValidateUi(outer, binding, UiLayerKind.DomesticOuterDialog, true, out error)
                    || outer.VerifiedCityId != state.CityId
                    || !string.Equals(outer.OuterCommandName, DomesticExecutionCoordinates.CommandName(request.Command), StringComparison.Ordinal)
                    || !outer.CandidateOfficerIds.SequenceEqual(before.ReadyCandidatesInSourceOrder))
                    return Uncertain(request, state, "OUTER_DIALOG_MISMATCH", string.IsNullOrEmpty(error) ? "The outer dialog command, city, or candidate order differs from the pre-read." : error);

                if (StopRequested(stopSignal))
                    return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_WITH_OUTER_OPEN", "Stop was requested before commit; the outer dialog was left open.", state);

                dispatch = Click(request, state, binding, "open-selector", DomesticExecutionCoordinates.ChooseOfficers);
                if (!dispatch.Succeeded) return DispatchFailure(request, state, dispatch, "OPEN_SELECTOR_FAILED");
                DomesticUiSnapshot selector = observationSource.CaptureUi();
                if (!ValidateUi(selector, binding, UiLayerKind.OfficerSelector, true, out error)
                    || selector.VerifiedCityId != state.CityId
                    || !selector.CandidateOfficerIds.SequenceEqual(before.RankedCandidates))
                    return Uncertain(request, state, "SELECTOR_SOURCE_MISMATCH", string.IsNullOrEmpty(error) ? "The selector city or candidate order differs from the pre-read." : error);

                if (StopRequested(stopSignal))
                    return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_WITH_SELECTOR_OPEN", "Stop was requested before commit; the selector was left open.", state);

                dispatch = Click(request, state, binding, "select-native-max", DomesticExecutionCoordinates.SelectNativeMaximum);
                if (!dispatch.Succeeded) return DispatchFailure(request, state, dispatch, "SELECT_MAX_FAILED");
                DomesticUiSnapshot selected = observationSource.CaptureUi();
                if (!ValidateUi(selected, binding, UiLayerKind.OfficerSelector, true, out error)
                    || selected.VerifiedCityId != state.CityId
                    || !selected.CandidateOfficerIds.SequenceEqual(before.RankedCandidates)
                    || !selected.SelectedOfficerIds.SequenceEqual(expectedFive))
                    return Uncertain(request, state, "EXACT_FIVE_NOT_SELECTED", string.IsNullOrEmpty(error) ? "Native maximum selection was not exactly the expected first five officers." : error);
                state.SelectedOfficerIds.AddRange(expectedFive);

                if (StopRequested(stopSignal))
                    return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_WITH_SELECTION_READY", "Stop was requested before accepting the selected officers.", state);

                dispatch = Click(request, state, binding, "accept-selector", DomesticExecutionCoordinates.AcceptSelector);
                if (!dispatch.Succeeded) return DispatchFailure(request, state, dispatch, "ACCEPT_SELECTOR_FAILED");
                DomesticUiSnapshot confirmation = observationSource.CaptureUi();
                if (!ValidateUi(confirmation, binding, UiLayerKind.DomesticConfirmation, true, out error)
                    || confirmation.VerifiedCityId != state.CityId
                    || !SameDistinctSet(confirmation.WorkingOfficerIds, expectedFive))
                    return Uncertain(request, state, "CONFIRMATION_MISMATCH", string.IsNullOrEmpty(error) ? "The confirmation dialog does not contain the exact selected five." : error);

                if (StopRequested(stopSignal))
                    return Result(request, DomesticExecutionStatus.StoppedBeforeCommit, "STOPPED_AT_CONFIRMATION", "Stop was requested before commit; the confirmation dialog was left open.", state);

                dispatch = Click(request, state, binding, "execute-command", DomesticExecutionCoordinates.ExecuteCommand);
                state.CommitClickAttempted = dispatch.Attempted;
                if (!dispatch.Succeeded) return DispatchFailure(request, state, dispatch, "COMMIT_CLICK_FAILED");

                delay(InitialPostCommitDelayMilliseconds);
                DomesticUiSnapshot resultLayer = observationSource.CaptureUi();
                if (!ValidateUi(resultLayer, binding, UiLayerKind.StrategicMapCandidate, false, out error)
                    || resultLayer.DomesticTargetCityId != state.CityId)
                    return Uncertain(request, state, "RESULT_LAYER_MISMATCH", string.IsNullOrEmpty(error) ? "The committed command did not reach the expected result/map layer." : error);

                PostCommitPollStatus pollStatus = PollCommittedAvailability(
                    request,
                    state,
                    binding,
                    before,
                    expectedFive,
                    out error);
                if (pollStatus == PostCommitPollStatus.TechnicallyInvalid)
                    return Uncertain(request, state, "POST_AVAILABILITY_REJECTED", error);
                if (pollStatus == PostCommitPollStatus.PartialOrUnexplained)
                    return Uncertain(request, state, "COMMIT_POSTCONDITION_MISMATCH", error);
                if (pollStatus == PostCommitPollStatus.TimedOutUnchanged)
                    return Uncertain(request, state, "COMMIT_POSTCONDITION_TIMEOUT", error);

                dispatch = PressReturn(request, state, binding, "acknowledge-result");
                if (!dispatch.Succeeded) return DispatchFailure(request, state, dispatch, "ACKNOWLEDGEMENT_FAILED");
                DomesticUiSnapshot final = observationSource.CaptureUi();
                if (!ValidateUi(final, binding, UiLayerKind.StrategicMapCandidate, false, out error)
                    || final.DomesticTargetCityId != state.CityId)
                    return Uncertain(request, state, "FINAL_MAP_MISMATCH", string.IsNullOrEmpty(error) ? "The result acknowledgement did not return to the stable map state." : error);

                return Result(request, DomesticExecutionStatus.Completed, "SINGLE_COMMAND_COMPLETED", "The command committed with exactly five officers and all postconditions passed.", state);
            }
            catch (Exception exception)
            {
                return Result(
                    request,
                    state.InputActionCount == 0 ? DomesticExecutionStatus.RejectedBeforeInput : DomesticExecutionStatus.AbortUncertain,
                    state.InputActionCount == 0 ? "EXECUTION_REJECTED" : "EXECUTION_EXCEPTION_AFTER_INPUT",
                    exception.GetType().Name + ": " + exception.Message,
                    state);
            }
        }

        private ActionDispatch Click(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionRunState state,
            WindowBinding binding,
            string action,
            DomesticExecutionPoint point)
        {
            return Dispatch(request, state, binding, action, delegate(out bool attempted, out string error)
            {
                return transport.TryClick(binding, point.X, point.Y, out attempted, out error);
            });
        }

        private ActionDispatch Hover(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionRunState state,
            WindowBinding binding,
            string action,
            DomesticExecutionPoint point)
        {
            return Dispatch(request, state, binding, action, delegate(out bool attempted, out string error)
            {
                return transport.TryHover(binding, point.X, point.Y, out attempted, out error);
            });
        }

        private ActionDispatch PressReturn(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionRunState state,
            WindowBinding binding,
            string action)
        {
            return Dispatch(request, state, binding, action, delegate(out bool attempted, out string error)
            {
                return transport.TryPressReturn(binding, out attempted, out error);
            });
        }

        private ActionDispatch Dispatch(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionRunState state,
            WindowBinding binding,
            string action,
            TryInputAction input)
        {
            string error;
            if (!transport.TryActivate(binding, out error)) return ActionDispatch.Rejected(error);
            int ordinal = state.InputActionCount + 1;
            string beforePath = Path.Combine(request.EvidenceDirectory, ordinal.ToString("00") + "-" + action + "-before.png");
            string afterPath = Path.Combine(request.EvidenceDirectory, ordinal.ToString("00") + "-" + action + "-after.png");
            if (!transport.TryCaptureClientPng(binding, beforePath, out error)) return ActionDispatch.Rejected(error);
            bool attempted;
            bool sent = input(out attempted, out error);
            if (attempted) state.InputActionCount++;
            delay(150);
            string screenshotError;
            bool screenshot = transport.TryCaptureClientPng(binding, afterPath, out screenshotError);
            state.Evidence.Add(new DomesticExecutionEvidence(action, beforePath, screenshot ? afterPath : string.Empty));
            if (!sent)
                return attempted ? ActionDispatch.Uncertain(error, true) : ActionDispatch.Rejected(error);
            if (!screenshot) return ActionDispatch.Uncertain(screenshotError, true);
            return ActionDispatch.Completed(attempted);
        }

        internal static bool ValidateUi(
            DomesticUiSnapshot snapshot,
            WindowBinding binding,
            UiLayerKind layer,
            bool requireVerifiedCity,
            out string error)
        {
            if (snapshot == null || !snapshot.IsValid)
            {
                error = snapshot == null ? "No UI snapshot was returned." : snapshot.Error;
                return false;
            }
            if (binding != null && (snapshot.ProcessId != binding.ProcessId
                || snapshot.ProcessCreationFileTimeUtc != binding.ProcessCreationFileTimeUtc
                || snapshot.MainWindowHandle != binding.MainWindowHandle))
            {
                error = "PID, process generation, or HWND changed.";
                return false;
            }
            if (snapshot.Layer != layer)
            {
                error = "Expected UI layer " + layer + " but observed " + snapshot.Layer + ".";
                return false;
            }
            if (requireVerifiedCity && !snapshot.VerifiedCityId.HasValue)
            {
                error = "The current city is not independently verified.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        internal static bool ValidateAvailability(
            DomesticAvailabilitySnapshot snapshot,
            WindowBinding binding,
            int cityId,
            out string error)
        {
            if (snapshot == null || !snapshot.IsValid)
            {
                error = snapshot == null ? "No availability snapshot was returned." : snapshot.Error;
                return false;
            }
            if (snapshot.ProcessId != binding.ProcessId
                || snapshot.ProcessCreationFileTimeUtc != binding.ProcessCreationFileTimeUtc
                || snapshot.CityId != cityId)
            {
                error = "Availability PID, generation, or city changed.";
                return false;
            }
            if (!SameDistinctSet(snapshot.ReadyCandidatesInSourceOrder, snapshot.RankedCandidates))
            {
                error = "The ranked candidates are not an exact permutation of the source-order ready candidates.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool ValidateCommittedPostcondition(
            DomesticAvailabilitySnapshot before,
            DomesticAvailabilitySnapshot after,
            int[] selected,
            out string error)
        {
            if (!after.IsDirectlyControlled)
            {
                error = "The city ceased to be directly controlled after commit.";
                return false;
            }
            if (after.OrderBitClearPassed != false)
            {
                error = "The command order bit was not observed as used after commit.";
                return false;
            }
            int[] expectedReady = before.ReadyCandidatesInSourceOrder
                .Where(id => !selected.Contains(id))
                .ToArray();
            if (!after.ReadyCandidatesInSourceOrder.SequenceEqual(expectedReady))
            {
                error = "The post-commit ready source order is not the exact pre-commit order with the selected officers removed.";
                return false;
            }
            if (selected.Any(id => after.ReadyCandidatesInSourceOrder.Contains(id)))
            {
                error = "At least one selected officer remains ready after commit.";
                return false;
            }
            int expectedMoney = checked(before.CorpsMoney - before.CostPerOfficer * 5);
            if (after.CorpsMoney != expectedMoney)
            {
                error = "Corps money did not change by the exact five-officer cost.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private PostCommitPollStatus PollCommittedAvailability(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionRunState state,
            WindowBinding binding,
            DomesticAvailabilitySnapshot before,
            int[] selected,
            out string error)
        {
            int elapsedMilliseconds = InitialPostCommitDelayMilliseconds;
            while (true)
            {
                DomesticAvailabilitySnapshot after = observationSource.CaptureAvailability(
                    request.Command,
                    state.CityId.Value);
                if (!ValidateAvailability(after, binding, state.CityId.Value, out error))
                    return PostCommitPollStatus.TechnicallyInvalid;

                state.MoneyAfter = after.CorpsMoney;
                state.ReadyAfter = after.ReadyCandidatesInSourceOrder.Length;
                string postconditionError;
                if (ValidateCommittedPostcondition(before, after, selected, out postconditionError))
                {
                    error = string.Empty;
                    return PostCommitPollStatus.Completed;
                }

                if (!IsExactPreCommitState(before, after))
                {
                    error = postconditionError;
                    return PostCommitPollStatus.PartialOrUnexplained;
                }

                if (elapsedMilliseconds >= MaximumPostCommitWaitMilliseconds)
                {
                    error = "The availability snapshot remained at the exact pre-commit state for "
                        + MaximumPostCommitWaitMilliseconds
                        + " milliseconds.";
                    return PostCommitPollStatus.TimedOutUnchanged;
                }

                delay(PostCommitPollIntervalMilliseconds);
                elapsedMilliseconds += PostCommitPollIntervalMilliseconds;
            }
        }

        private static bool IsExactPreCommitState(
            DomesticAvailabilitySnapshot before,
            DomesticAvailabilitySnapshot observed)
        {
            return observed.CorpsMoney == before.CorpsMoney
                && observed.IsDirectlyControlled == before.IsDirectlyControlled
                && observed.CostPerOfficer == before.CostPerOfficer
                && observed.KnownStaticSubsetWouldPass == before.KnownStaticSubsetWouldPass
                && observed.OrderBitClearPassed == before.OrderBitClearPassed
                && observed.ReadyCandidatesInSourceOrder.SequenceEqual(
                    before.ReadyCandidatesInSourceOrder)
                && observed.RankedCandidates.SequenceEqual(before.RankedCandidates);
        }

        private bool TryResolveSurfaceProbe(
            DomesticCommandKind command,
            WindowBinding binding,
            int cityId,
            out DomesticCommandKind probeCommand,
            out string error)
        {
            if (command == DomesticCommandKind.Commerce
                || command == DomesticCommandKind.Cultivate
                || command == DomesticCommandKind.Repair)
            {
                probeCommand = command;
                error = string.Empty;
                return true;
            }

            DomesticCommandKind[] candidates =
                { DomesticCommandKind.Commerce, DomesticCommandKind.Cultivate, DomesticCommandKind.Repair };
            for (int index = 0; index < candidates.Length; index++)
            {
                DomesticAvailabilitySnapshot candidate = observationSource.CaptureAvailability(candidates[index], cityId);
                if (!ValidateAvailability(candidate, binding, cityId, out error))
                {
                    probeCommand = default(DomesticCommandKind);
                    return false;
                }
                if (candidate.IsDirectlyControlled && candidate.KnownStaticSubsetWouldPass)
                {
                    probeCommand = candidates[index];
                    error = string.Empty;
                    return true;
                }
            }
            probeCommand = default(DomesticCommandKind);
            error = "No Commerce, Cultivate, or Repair command is currently verified as an available hover probe.";
            return false;
        }

        internal static WindowBinding CreateBinding(DomesticUiSnapshot snapshot)
        {
            return new WindowBinding
            {
                ProcessId = snapshot.ProcessId,
                ProcessCreationFileTimeUtc = snapshot.ProcessCreationFileTimeUtc,
                MainWindowHandle = snapshot.MainWindowHandle
            };
        }

        private static bool StopRequested(DomesticExecutionStopSignal signal)
        {
            return signal != null && signal.IsStopRequested;
        }

        private static SingleCityDomesticExecutionResult DispatchFailure(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionRunState state,
            ActionDispatch dispatch,
            string code)
        {
            return Result(
                request,
                dispatch.Attempted || state.InputActionCount > 0
                    ? DomesticExecutionStatus.AbortUncertain
                    : DomesticExecutionStatus.RejectedBeforeInput,
                code,
                dispatch.Error,
                state);
        }

        private static SingleCityDomesticExecutionResult Uncertain(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionRunState state,
            string code,
            string message)
        {
            return Result(request, DomesticExecutionStatus.AbortUncertain, code, message, state);
        }

        internal static SingleCityDomesticExecutionResult Result(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionStatus status,
            string code,
            string message,
            DomesticExecutionRunState state)
        {
            return SingleCityDomesticExecutionResult.Create(request, status, code, message, state);
        }

        internal static bool SameDistinctSet(int[] left, int[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            return left.Distinct().Count() == left.Length
                && right.Distinct().Count() == right.Length
                && !left.Except(right).Any()
                && !right.Except(left).Any();
        }

        private enum PostCommitPollStatus
        {
            Completed,
            TechnicallyInvalid,
            PartialOrUnexplained,
            TimedOutUnchanged
        }

        private delegate bool TryInputAction(out bool attempted, out string error);
    }

    internal sealed class DomesticPreparedStep
    {
        internal WindowBinding Binding;
        internal int CityId;
        internal DomesticAvailabilitySnapshot Availability;
        internal DomesticUiSnapshot Menu;
        internal bool MenuPreparationFailed;
        internal bool MenuPreparationAttempted;
        internal bool MenuPreparationStopped;
        internal string MenuPreparationCode;
        internal string MenuPreparationError;
    }

    internal sealed class DomesticExecutionRunState
    {
        internal DomesticExecutionRunState()
        {
            SelectedOfficerIds = new List<int>();
            Evidence = new List<DomesticExecutionEvidence>();
        }

        internal int? CityId;
        internal int? MoneyBefore;
        internal int? MoneyAfter;
        internal int? ReadyBefore;
        internal int? ReadyAfter;
        internal bool CommitClickAttempted;
        internal int InputActionCount;
        internal List<int> SelectedOfficerIds;
        internal List<DomesticExecutionEvidence> Evidence;
    }

    internal struct DomesticExecutionPoint
    {
        internal DomesticExecutionPoint(int x, int y) { X = x; Y = y; }
        internal readonly int X;
        internal readonly int Y;
    }

    internal static class DomesticExecutionCoordinates
    {
        internal static readonly DomesticExecutionPoint ChooseOfficers = new DomesticExecutionPoint(329, 333);
        internal static readonly DomesticExecutionPoint SelectNativeMaximum = new DomesticExecutionPoint(273, 643);
        internal static readonly DomesticExecutionPoint AcceptSelector = new DomesticExecutionPoint(679, 665);
        internal static readonly DomesticExecutionPoint ExecuteCommand = new DomesticExecutionPoint(590, 544);

        internal static bool IsSupported(DomesticCommandKind command)
        {
            return command == DomesticCommandKind.Patrol
                || command == DomesticCommandKind.Commerce
                || command == DomesticCommandKind.Cultivate
                || command == DomesticCommandKind.Repair
                || command == DomesticCommandKind.Train;
        }

        internal static DomesticExecutionPoint ForCommand(DomesticCommandKind command)
        {
            switch (command)
            {
                case DomesticCommandKind.Patrol: return new DomesticExecutionPoint(607, 377);
                case DomesticCommandKind.Commerce: return new DomesticExecutionPoint(607, 405);
                case DomesticCommandKind.Cultivate: return new DomesticExecutionPoint(607, 433);
                case DomesticCommandKind.Repair: return new DomesticExecutionPoint(607, 461);
                case DomesticCommandKind.Train: return new DomesticExecutionPoint(607, 517);
                default: throw new ArgumentOutOfRangeException("command");
            }
        }

        internal static string CommandName(DomesticCommandKind command)
        {
            switch (command)
            {
                case DomesticCommandKind.Patrol: return "Patrol";
                case DomesticCommandKind.Commerce: return "Commerce";
                case DomesticCommandKind.Cultivate: return "Cultivate";
                case DomesticCommandKind.Repair: return "Repair";
                case DomesticCommandKind.Train: return "Train";
                default: throw new ArgumentOutOfRangeException("command");
            }
        }
    }

    internal sealed class ActionDispatch
    {
        private ActionDispatch(bool succeeded, bool attempted, string error)
        {
            Succeeded = succeeded;
            Attempted = attempted;
            Error = error ?? string.Empty;
        }

        internal bool Succeeded;
        internal bool Attempted;
        internal string Error;
        internal static ActionDispatch Completed(bool attempted) { return new ActionDispatch(true, attempted, string.Empty); }
        internal static ActionDispatch Rejected(string error) { return new ActionDispatch(false, false, error); }
        internal static ActionDispatch Uncertain(string error, bool attempted) { return new ActionDispatch(false, attempted, error); }
    }
}
