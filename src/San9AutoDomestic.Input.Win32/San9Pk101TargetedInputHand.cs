using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.Input.Win32
{
    public sealed class San9Pk101TargetedInputHand
    {
        private static int globalInFlight;
        private readonly IInputUiObservationSource observationSource;
        private readonly ITargetedWindowTransport transport;
        private readonly Action<int> delay;
        private int used;

        public San9Pk101TargetedInputHand()
            : this(new AdapterInputUiObservationSource(), new Win32TargetedWindowTransport(), Thread.Sleep)
        {
        }

        internal San9Pk101TargetedInputHand(
            IInputUiObservationSource observationSource,
            ITargetedWindowTransport transport,
            Action<int> delay)
        {
            if (observationSource == null) throw new ArgumentNullException("observationSource");
            if (transport == null) throw new ArgumentNullException("transport");
            if (delay == null) throw new ArgumentNullException("delay");
            this.observationSource = observationSource;
            this.transport = transport;
            this.delay = delay;
        }

        public ProgramInputDispatchReport LastProgramDispatch
        {
            get
            {
                IProgramInputDispatchTraceSource source = transport as IProgramInputDispatchTraceSource;
                return source == null ? null : source.LastProgramDispatch;
            }
        }

        public TargetedHoverAuthorization CaptureAuthorization()
        {
            return CreateAuthorization(observationSource.Capture(), true);
        }

        public TargetedHoverAuthorization CaptureFacilitiesCitySwitchAuthorization()
        {
            return CreateAuthorization(observationSource.Capture(), false);
        }

        public FacilitiesMapAuthorization CaptureFacilitiesMapAuthorization()
        {
            InputUiSnapshot snapshot = observationSource.Capture();
            string error;
            if (!ValidateMapSnapshot(snapshot, out error))
            {
                return new FacilitiesMapAuthorization
                {
                    CanAuthorize = false,
                    FailureCode = "MAP_AUTHORIZATION_CAPTURE_REJECTED",
                    FailureMessage = error,
                    CapturedUtc = snapshot == null ? DateTimeOffset.UtcNow : snapshot.CapturedUtc
                };
            }
            return new FacilitiesMapAuthorization
            {
                CanAuthorize = true,
                CapturedUtc = snapshot.CapturedUtc,
                ProcessId = snapshot.ProcessId,
                ProcessCreationFileTimeUtc = snapshot.ProcessCreationFileTimeUtc,
                MainWindowHandle = snapshot.MainWindowHandle,
                WindowObservationToken = snapshot.WindowObservationToken,
                StructuralToken = snapshot.StructuralToken
            };
        }

        public TargetedInputResult Execute(TargetedHoverRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            int messageCount = 0;
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Rejected(request, "BATCH_ALREADY_USED", "A targeted-input hand is single-use.");
            if (Interlocked.CompareExchange(ref globalInFlight, 1, 0) != 0)
                return Rejected(request, "GLOBAL_INPUT_BUSY", "Another targeted input batch is already active.");

            try
            {
                TimeSpan authorizationAge = DateTimeOffset.UtcNow - request.AuthorizationCapturedUtc;
                if (authorizationAge < TimeSpan.FromSeconds(-5) || authorizationAge > TimeSpan.FromSeconds(30))
                    return Rejected(request, "AUTHORIZATION_EXPIRED", "The authorized UI capture is outside the 30-second runtime window.");
                InputUiSnapshot before = observationSource.Capture();
                string error;
                if (!ValidateBefore(request, before, out error))
                    return Rejected(request, "PRECONDITION_REJECTED", error);

                WindowBinding expected = WindowBinding.FromRequest(request);
                WindowEnvironment atSend;
                bool attempted;
                string sendError;
                bool sent = transport.TryPostMouseMove(
                    expected,
                    request.ClientX,
                    request.ClientY,
                    out atSend,
                    out attempted,
                    out sendError);
                if (attempted) messageCount = 1;
                if (!sent)
                {
                    return new TargetedInputResult
                    {
                        Status = attempted ? TargetedInputStatus.AbortUncertain : TargetedInputStatus.RejectedBeforeSend,
                        Code = attempted ? "POSTMESSAGE_UNCERTAIN" : "SEND_GATE_REJECTED",
                        Message = sendError,
                        AuthorizationId = request.AuthorizationId,
                        MessageCount = messageCount
                    };
                }

                delay(300);
                InputUiSnapshot after = observationSource.Capture();
                WindowEnvironment afterEnvironment;
                string captureError;
                bool environmentRead = transport.TryCapture(expected, out afterEnvironment, out captureError);

                bool bindingStable = environmentRead
                    && atSend.TargetMatches(expected)
                    && afterEnvironment.TargetMatches(expected);
                bool foregroundStable = environmentRead
                    && atSend.ForegroundWindowHandle == afterEnvironment.ForegroundWindowHandle
                    && atSend.ForegroundProcessId == afterEnvironment.ForegroundProcessId;
                bool stateVerified = ValidateAfter(request, before, after, out error);
                if (!environmentRead && string.IsNullOrEmpty(error)) error = captureError;

                if (!bindingStable || !foregroundStable || !stateVerified)
                {
                    return new TargetedInputResult
                    {
                        Status = TargetedInputStatus.AbortUncertain,
                        Code = "POSTCONDITION_UNCERTAIN",
                        Message = string.IsNullOrEmpty(error) ? "The post-send binding or foreground changed." : error,
                        AuthorizationId = request.AuthorizationId,
                        MessageCount = 1,
                        BindingStable = bindingStable,
                        ForegroundStable = foregroundStable,
                        StateVerified = stateVerified
                    };
                }

                return new TargetedInputResult
                {
                    Status = TargetedInputStatus.Completed,
                    Code = "TARGETED_HOVER_VERIFIED",
                    Message = "Exactly one WM_MOUSEMOVE was followed by a stable verified hover transition.",
                    AuthorizationId = request.AuthorizationId,
                    MessageCount = 1,
                    BindingStable = true,
                    ForegroundStable = true,
                    StateVerified = true
                };
            }
            catch (Exception exception)
            {
                return new TargetedInputResult
                {
                    Status = messageCount == 0 ? TargetedInputStatus.RejectedBeforeSend : TargetedInputStatus.AbortUncertain,
                    Code = messageCount == 0 ? "INPUT_BATCH_REJECTED" : "INPUT_BATCH_EXCEPTION_AFTER_SEND",
                    Message = exception.GetType().Name + ": " + exception.Message,
                    AuthorizationId = request.AuthorizationId,
                    MessageCount = messageCount
                };
            }
            finally
            {
                Interlocked.Exchange(ref globalInFlight, 0);
            }
        }

        public TargetedInputResult ExecuteFacilitiesCitySwitch(FacilitiesCitySwitchRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            int messageCount = 0;
            int gestureCount = 0;
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Rejected(request.AuthorizationId, "BATCH_ALREADY_USED", "A targeted-input hand is single-use.");
            if (Interlocked.CompareExchange(ref globalInFlight, 1, 0) != 0)
                return Rejected(request.AuthorizationId, "GLOBAL_INPUT_BUSY", "Another targeted input batch is already active.");

            try
            {
                TimeSpan authorizationAge = DateTimeOffset.UtcNow - request.AuthorizationCapturedUtc;
                if (authorizationAge < TimeSpan.FromSeconds(-5) || authorizationAge > TimeSpan.FromSeconds(120))
                    return Rejected(request.AuthorizationId, "AUTHORIZATION_EXPIRED", "The source-menu authorization is outside the 120-second assisted-navigation window.");

                InputUiSnapshot map = observationSource.Capture();
                string error;
                if (!ValidateNavigationSnapshot(request, map, UiLayerKind.StrategicMapCandidate, out error))
                    return Rejected(request.AuthorizationId, "MAP_PRECONDITION_REJECTED", error);

                WindowBinding expected = WindowBinding.FromRequest(request);
                WindowEnvironment atFacilityClick;
                int attempted;
                string sendError;
                bool sent = transport.TryPostLeftClick(
                    expected,
                    FacilitiesCitySwitchRequest.FacilitiesButtonClientX,
                    FacilitiesCitySwitchRequest.FacilitiesButtonClientY,
                    out atFacilityClick,
                    out attempted,
                    out sendError);
                messageCount += attempted;
                if (attempted > 0) gestureCount = 1;
                if (!sent)
                    return ClickFailure(request, messageCount, gestureCount, sendError);

                delay(300);
                InputUiSnapshot panel = observationSource.Capture();
                WindowEnvironment afterFacilityClick;
                string captureError;
                bool environmentRead = transport.TryCapture(expected, out afterFacilityClick, out captureError);
                bool firstBindingStable = environmentRead
                    && atFacilityClick.TargetMatches(expected)
                    && afterFacilityClick.TargetMatches(expected);
                bool firstForegroundStable = environmentRead
                    && atFacilityClick.TargetIsForeground(expected)
                    && afterFacilityClick.TargetIsForeground(expected);
                bool panelVerified = ValidateNavigationSnapshot(request, panel, UiLayerKind.StrategicMapCandidate, out error)
                    && string.Equals(map.StructuralToken, panel.StructuralToken, StringComparison.Ordinal);
                if (!panelVerified && string.IsNullOrEmpty(error))
                    error = "The strategic-map task structure changed unexpectedly after the facilities click.";
                if (!environmentRead && string.IsNullOrEmpty(error)) error = captureError;
                if (!firstBindingStable || !firstForegroundStable || !panelVerified)
                    return Uncertain(request, messageCount, gestureCount, error, firstBindingStable, firstForegroundStable, false, null);

                WindowEnvironment atRowClick;
                attempted = 0;
                sent = transport.TryPostLeftClick(
                    expected,
                    request.TargetRowClientX,
                    request.TargetRowClientY,
                    out atRowClick,
                    out attempted,
                    out sendError);
                messageCount += attempted;
                if (attempted > 0) gestureCount = 2;
                if (!sent)
                    return ClickFailure(request, messageCount, gestureCount, sendError);

                delay(300);
                InputUiSnapshot after = observationSource.Capture();
                WindowEnvironment afterRowClick;
                environmentRead = transport.TryCapture(expected, out afterRowClick, out captureError);
                bool bindingStable = environmentRead
                    && firstBindingStable
                    && atRowClick.TargetMatches(expected)
                    && afterRowClick.TargetMatches(expected);
                bool foregroundStable = environmentRead
                    && firstForegroundStable
                    && atRowClick.TargetIsForeground(expected)
                    && afterRowClick.TargetIsForeground(expected);
                bool stateVerified = ValidateFinalCityMenu(request, after, out error);
                if (!environmentRead && string.IsNullOrEmpty(error)) error = captureError;
                if (!bindingStable || !foregroundStable || !stateVerified)
                    return Uncertain(request, messageCount, gestureCount, error, bindingStable, foregroundStable, stateVerified,
                        after.CityId >= 0 ? (int?)after.CityId : null);

                return new TargetedInputResult
                {
                    Status = TargetedInputStatus.Completed,
                    Code = "FACILITIES_CITY_SWITCH_VERIFIED",
                    Message = "Two targeted click gestures ended at a stable domestic menu for the exact target city.",
                    AuthorizationId = request.AuthorizationId,
                    MessageCount = messageCount,
                    GestureCount = gestureCount,
                    ObservedCityId = after.CityId,
                    BindingStable = true,
                    ForegroundStable = true,
                    StateVerified = true
                };
            }
            catch (Exception exception)
            {
                return new TargetedInputResult
                {
                    Status = messageCount == 0 ? TargetedInputStatus.RejectedBeforeSend : TargetedInputStatus.AbortUncertain,
                    Code = messageCount == 0 ? "CITY_SWITCH_REJECTED" : "CITY_SWITCH_EXCEPTION_AFTER_SEND",
                    Message = exception.GetType().Name + ": " + exception.Message,
                    AuthorizationId = request.AuthorizationId,
                    MessageCount = messageCount,
                    GestureCount = gestureCount
                };
            }
            finally
            {
                Interlocked.Exchange(ref globalInFlight, 0);
            }
        }

        public TargetedInputResult ExecuteFacilitiesRowSelection(FacilitiesRowSelectionRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            int messageCount = 0;
            if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
                return Rejected(request.AuthorizationId, "BATCH_ALREADY_USED", "A targeted-input hand is single-use.");
            if (Interlocked.CompareExchange(ref globalInFlight, 1, 0) != 0)
                return Rejected(request.AuthorizationId, "GLOBAL_INPUT_BUSY", "Another targeted input batch is already active.");
            try
            {
                TimeSpan age = DateTimeOffset.UtcNow - request.AuthorizationCapturedUtc;
                if (age < TimeSpan.FromSeconds(-5) || age > TimeSpan.FromSeconds(30))
                    return Rejected(request.AuthorizationId, "AUTHORIZATION_EXPIRED", "The map authorization is outside the 30-second immediate-selection window.");

                InputUiSnapshot before = observationSource.Capture();
                string error;
                if (!ValidateMapSnapshot(before, out error)
                    || before.ProcessId != request.ProcessId
                    || before.ProcessCreationFileTimeUtc != request.ProcessCreationFileTimeUtc
                    || before.MainWindowHandle != request.MainWindowHandle
                    || !string.Equals(before.WindowObservationToken, request.PreObservationToken, StringComparison.Ordinal)
                    || !string.Equals(before.StructuralToken, request.PreStructuralToken, StringComparison.Ordinal))
                    return Rejected(request.AuthorizationId, "MAP_PRECONDITION_REJECTED",
                        string.IsNullOrEmpty(error) ? "The exact authorized map observation changed." : error);

                WindowBinding expected = WindowBinding.FromRequest(request);
                WindowEnvironment atClick;
                int attempted;
                string sendError;
                bool sent;
                if (request.ClickMode == TargetedClickMode.SendInputStaged)
                    sent = transport.TrySendInputStagedLeftClick(expected, request.TargetRowClientX, request.TargetRowClientY,
                        out atClick, out attempted, out sendError);
                else if (request.ClickMode == TargetedClickMode.SendInput)
                    sent = transport.TrySendInputLeftClick(expected, request.TargetRowClientX, request.TargetRowClientY,
                        out atClick, out attempted, out sendError);
                else
                    sent = transport.TryPostLeftClick(expected, request.TargetRowClientX, request.TargetRowClientY,
                        out atClick, out attempted, out sendError);
                messageCount += attempted;
                if (!sent)
                {
                    return new TargetedInputResult
                    {
                        Status = messageCount == 0 ? TargetedInputStatus.RejectedBeforeSend : TargetedInputStatus.AbortUncertain,
                        Code = messageCount == 0 ? "CLICK_GATE_REJECTED" : "CLICK_SEQUENCE_UNCERTAIN",
                        Message = sendError,
                        AuthorizationId = request.AuthorizationId,
                        MessageCount = messageCount,
                        GestureCount = attempted > 0 ? 1 : 0
                    };
                }

                delay(300);
                InputUiSnapshot after = observationSource.Capture();
                WindowEnvironment afterClick;
                string captureError;
                bool environmentRead = transport.TryCapture(expected, out afterClick, out captureError);
                bool bindingStable = environmentRead && atClick.TargetMatches(expected) && afterClick.TargetMatches(expected);
                bool foregroundStable = environmentRead && atClick.TargetIsForeground(expected) && afterClick.TargetIsForeground(expected);
                bool stateVerified = ValidateFinalCityMenu(request, after, out error);
                if (!environmentRead && string.IsNullOrEmpty(error)) error = captureError;
                if (!bindingStable || !foregroundStable || !stateVerified)
                {
                    return new TargetedInputResult
                    {
                        Status = TargetedInputStatus.AbortUncertain,
                        Code = "ROW_SELECTION_POSTCONDITION_UNCERTAIN",
                        Message = string.IsNullOrEmpty(error) ? "The row-selection postcondition could not be verified." : error,
                        AuthorizationId = request.AuthorizationId,
                        MessageCount = messageCount,
                        GestureCount = 1,
                        ObservedCityId = after.CityId >= 0 ? (int?)after.CityId : null,
                        BindingStable = bindingStable,
                        ForegroundStable = foregroundStable,
                        StateVerified = stateVerified
                    };
                }

                return new TargetedInputResult
                {
                    Status = TargetedInputStatus.Completed,
                    Code = "FACILITIES_ROW_TARGET_CITY_VERIFIED",
                    Message = "One targeted row click ended at the exact target-city domestic menu.",
                    AuthorizationId = request.AuthorizationId,
                    MessageCount = messageCount,
                    GestureCount = 1,
                    ObservedCityId = after.CityId,
                    BindingStable = true,
                    ForegroundStable = true,
                    StateVerified = true
                };
            }
            catch (Exception exception)
            {
                return new TargetedInputResult
                {
                    Status = messageCount == 0 ? TargetedInputStatus.RejectedBeforeSend : TargetedInputStatus.AbortUncertain,
                    Code = messageCount == 0 ? "ROW_SELECTION_REJECTED" : "ROW_SELECTION_EXCEPTION_AFTER_SEND",
                    Message = exception.GetType().Name + ": " + exception.Message,
                    AuthorizationId = request.AuthorizationId,
                    MessageCount = messageCount,
                    GestureCount = messageCount > 0 ? 1 : 0
                };
            }
            finally
            {
                Interlocked.Exchange(ref globalInFlight, 0);
            }
        }

        private static TargetedHoverAuthorization CreateAuthorization(InputUiSnapshot snapshot, bool requireVerifiedHover)
        {
            string error;
            if (!ValidateMenuSnapshot(snapshot, requireVerifiedHover, out error))
            {
                return new TargetedHoverAuthorization
                {
                    CanAuthorize = false,
                    FailureCode = "AUTHORIZATION_CAPTURE_REJECTED",
                    FailureMessage = error,
                    CapturedUtc = snapshot == null ? DateTimeOffset.UtcNow : snapshot.CapturedUtc
                };
            }

            return new TargetedHoverAuthorization
            {
                CanAuthorize = true,
                CapturedUtc = snapshot.CapturedUtc,
                ProcessId = snapshot.ProcessId,
                ProcessCreationFileTimeUtc = snapshot.ProcessCreationFileTimeUtc,
                MainWindowHandle = snapshot.MainWindowHandle,
                CityId = snapshot.CityId,
                HoveredCommand = snapshot.HoveredCommand,
                WindowObservationToken = snapshot.WindowObservationToken,
                StructuralToken = snapshot.StructuralToken
            };
        }

        private static bool ValidateNavigationSnapshot(
            FacilitiesCitySwitchRequest request,
            InputUiSnapshot snapshot,
            UiLayerKind expectedLayer,
            out string error)
        {
            if (snapshot == null) return Fail("No UI observation was returned.", out error);
            if (!snapshot.ReadSucceeded || !snapshot.StableAbc) return Fail("The UI observation is not stable A/B/C.", out error);
            if (snapshot.HasBlockingIssue) return Fail("The UI observation contains a blocking issue.", out error);
            if (!snapshot.StrategicInput) return Fail("The game is not in verified strategic input.", out error);
            if (snapshot.Layer != expectedLayer) return Fail("The expected navigation layer is not active.", out error);
            if (snapshot.ProcessId != request.ProcessId
                || snapshot.ProcessCreationFileTimeUtc != request.ProcessCreationFileTimeUtc
                || snapshot.MainWindowHandle != request.MainWindowHandle)
                return Fail("PID, process generation, or HWND differs from the authorized source menu.", out error);
            if (string.IsNullOrEmpty(snapshot.WindowObservationToken) || string.IsNullOrEmpty(snapshot.StructuralToken))
                return Fail("The navigation observation token is missing.", out error);
            error = string.Empty;
            return true;
        }

        private static bool ValidateMapSnapshot(InputUiSnapshot snapshot, out string error)
        {
            if (snapshot == null) return Fail("No UI observation was returned.", out error);
            if (!snapshot.ReadSucceeded || !snapshot.StableAbc) return Fail("The UI observation is not stable A/B/C.", out error);
            if (snapshot.HasBlockingIssue) return Fail("The UI observation contains a blocking issue.", out error);
            if (!snapshot.StrategicInput) return Fail("The game is not in verified strategic input.", out error);
            if (snapshot.Layer != UiLayerKind.StrategicMapCandidate) return Fail("The strategic map is not active.", out error);
            if (snapshot.ProcessId <= 0 || snapshot.ProcessCreationFileTimeUtc <= 0 || snapshot.MainWindowHandle <= 0)
                return Fail("The process binding is incomplete.", out error);
            if (string.IsNullOrEmpty(snapshot.WindowObservationToken) || string.IsNullOrEmpty(snapshot.StructuralToken))
                return Fail("The map observation token is missing.", out error);
            error = string.Empty;
            return true;
        }

        private static bool ValidateFinalCityMenu(
            FacilitiesCitySwitchRequest request,
            InputUiSnapshot snapshot,
            out string error)
        {
            if (!ValidateMenuSnapshot(snapshot, false, out error)) return false;
            if (snapshot.ProcessId != request.ProcessId
                || snapshot.ProcessCreationFileTimeUtc != request.ProcessCreationFileTimeUtc
                || snapshot.MainWindowHandle != request.MainWindowHandle)
                return Fail("The process binding changed after the city-row click.", out error);
            if (snapshot.CityId != request.TargetCityId)
                return Fail("The final domestic menu does not belong to the requested target city.", out error);
            error = string.Empty;
            return true;
        }

        private static bool ValidateFinalCityMenu(
            FacilitiesRowSelectionRequest request,
            InputUiSnapshot snapshot,
            out string error)
        {
            if (!ValidateMenuSnapshot(snapshot, false, out error)) return false;
            if (snapshot.ProcessId != request.ProcessId
                || snapshot.ProcessCreationFileTimeUtc != request.ProcessCreationFileTimeUtc
                || snapshot.MainWindowHandle != request.MainWindowHandle)
                return Fail("The process binding changed after the city-row click.", out error);
            if (snapshot.CityId != request.TargetCityId)
                return Fail("The final domestic menu does not belong to the requested target city.", out error);
            error = string.Empty;
            return true;
        }

        private static TargetedInputResult ClickFailure(
            FacilitiesCitySwitchRequest request,
            int messageCount,
            int gestureCount,
            string message)
        {
            return new TargetedInputResult
            {
                Status = messageCount == 0 ? TargetedInputStatus.RejectedBeforeSend : TargetedInputStatus.AbortUncertain,
                Code = messageCount == 0 ? "CLICK_GATE_REJECTED" : "CLICK_SEQUENCE_UNCERTAIN",
                Message = message,
                AuthorizationId = request.AuthorizationId,
                MessageCount = messageCount,
                GestureCount = gestureCount
            };
        }

        private static TargetedInputResult Uncertain(
            FacilitiesCitySwitchRequest request,
            int messageCount,
            int gestureCount,
            string message,
            bool bindingStable,
            bool foregroundStable,
            bool stateVerified,
            int? observedCityId)
        {
            return new TargetedInputResult
            {
                Status = TargetedInputStatus.AbortUncertain,
                Code = "CITY_SWITCH_POSTCONDITION_UNCERTAIN",
                Message = string.IsNullOrEmpty(message) ? "A route postcondition could not be verified." : message,
                AuthorizationId = request.AuthorizationId,
                MessageCount = messageCount,
                GestureCount = gestureCount,
                ObservedCityId = observedCityId,
                BindingStable = bindingStable,
                ForegroundStable = foregroundStable,
                StateVerified = stateVerified
            };
        }

        private static bool ValidateAuthorizable(InputUiSnapshot snapshot, out string error)
        {
            return ValidateMenuSnapshot(snapshot, true, out error);
        }

        private static bool ValidateMenuSnapshot(InputUiSnapshot snapshot, bool requireVerifiedHover, out string error)
        {
            if (snapshot == null) return Fail("No UI observation was returned.", out error);
            if (!snapshot.ReadSucceeded || !snapshot.StableAbc) return Fail("The UI observation is not stable A/B/C.", out error);
            if (snapshot.HasBlockingIssue) return Fail("The UI observation contains a blocking issue.", out error);
            if (snapshot.Layer != UiLayerKind.DomesticCommandMenu) return Fail("The domestic command menu is not active.", out error);
            if (!snapshot.StrategicInput) return Fail("The game is not in verified strategic input.", out error);
            if (requireVerifiedHover && !snapshot.HoveredCommandVerified)
                return Fail("The hovered domestic command is not verified.", out error);
            if (snapshot.ProcessId <= 0 || snapshot.ProcessCreationFileTimeUtc <= 0 || snapshot.MainWindowHandle <= 0)
                return Fail("The process binding is incomplete.", out error);
            if (snapshot.CityId < 0 || snapshot.CityId >= 50) return Fail("The current city is not verified.", out error);
            if (string.IsNullOrEmpty(snapshot.WindowObservationToken)) return Fail("The UI observation token is missing.", out error);
            error = string.Empty;
            return true;
        }

        private static bool ValidateBefore(TargetedHoverRequest request, InputUiSnapshot snapshot, out string error)
        {
            if (!ValidateAuthorizable(snapshot, out error)) return false;
            if (snapshot.ProcessId != request.ProcessId
                || snapshot.ProcessCreationFileTimeUtc != request.ProcessCreationFileTimeUtc
                || snapshot.MainWindowHandle != request.MainWindowHandle)
                return Fail("PID, process generation, or HWND differs from the authorized capture.", out error);
            if (snapshot.CityId != request.CityId) return Fail("The current city differs from the authorized capture.", out error);
            if (snapshot.HoveredCommand != request.SourceCommand) return Fail("The source hover differs from the authorized capture.", out error);
            if (!string.Equals(snapshot.WindowObservationToken, request.PreObservationToken, StringComparison.Ordinal))
                return Fail("The pre-observation token changed after authorization.", out error);
            error = string.Empty;
            return true;
        }

        private static bool ValidateAfter(
            TargetedHoverRequest request,
            InputUiSnapshot before,
            InputUiSnapshot after,
            out string error)
        {
            if (!ValidateAuthorizable(after, out error)) return false;
            if (after.ProcessId != request.ProcessId
                || after.ProcessCreationFileTimeUtc != request.ProcessCreationFileTimeUtc
                || after.MainWindowHandle != request.MainWindowHandle)
                return Fail("The process binding changed after the single message.", out error);
            if (after.CityId != request.CityId) return Fail("The current city changed after the single message.", out error);
            if (after.HoveredCommand != request.TargetCommand) return Fail("The target hover was not observed.", out error);
            if (!string.Equals(before.StructuralToken, after.StructuralToken, StringComparison.Ordinal))
                return Fail("The scene, task, controller, or menu structure changed.", out error);
            error = string.Empty;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private static TargetedInputResult Rejected(TargetedHoverRequest request, string code, string message)
        {
            return new TargetedInputResult
            {
                Status = TargetedInputStatus.RejectedBeforeSend,
                Code = code,
                Message = message,
                AuthorizationId = request.AuthorizationId,
                MessageCount = 0
            };
        }

        private static TargetedInputResult Rejected(Guid authorizationId, string code, string message)
        {
            return new TargetedInputResult
            {
                Status = TargetedInputStatus.RejectedBeforeSend,
                Code = code,
                Message = message,
                AuthorizationId = authorizationId,
                MessageCount = 0,
                GestureCount = 0
            };
        }
    }

    internal interface IInputUiObservationSource
    {
        InputUiSnapshot Capture();
    }

    internal sealed class AdapterInputUiObservationSource : IInputUiObservationSource
    {
        public InputUiSnapshot Capture()
        {
            San9Pk101UiObservationReport report = new San9Pk101Adapter().ReadUiObservation();
            return InputUiSnapshot.FromReport(report);
        }
    }

    internal sealed class InputUiSnapshot
    {
        internal bool ReadSucceeded;
        internal bool StableAbc;
        internal bool HasBlockingIssue;
        internal DateTimeOffset CapturedUtc;
        internal int ProcessId;
        internal long ProcessCreationFileTimeUtc;
        internal long MainWindowHandle;
        internal UiLayerKind Layer;
        internal bool StrategicInput;
        internal bool HoveredCommandVerified;
        internal DomesticHoverCommand HoveredCommand;
        internal int CityId;
        internal string WindowObservationToken;
        internal string StructuralToken;

        internal static InputUiSnapshot FromReport(San9Pk101UiObservationReport report)
        {
            InputUiSnapshot value = new InputUiSnapshot();
            value.WindowObservationToken = string.Empty;
            value.StructuralToken = string.Empty;
            if (report == null) return value;

            value.ReadSucceeded = report.ReadSucceeded;
            value.StableAbc = report.StableAbc;
            value.HasBlockingIssue = report.Issues != null
                && report.Issues.Any(issue => issue != null && issue.Severity == DiagnosticSeverity.Blocking);
            value.CapturedUtc = report.CapturedUtc;
            value.ProcessId = report.ProcessId ?? 0;
            value.ProcessCreationFileTimeUtc = report.ProcessCreationFileTimeUtc ?? 0;
            value.MainWindowHandle = report.MainWindowHandle ?? 0;
            value.Layer = report.Layer;
            value.WindowObservationToken = report.WindowObservationToken ?? string.Empty;

            UiObservationField phase = FindField(report, "StrategicPhase");
            value.StrategicInput = phase != null
                && phase.State == UiObservationState.Verified
                && string.Equals(phase.Value, "StrategicInput", StringComparison.Ordinal);

            UiObservationField city = FindField(report, "VerifiedCurrentCityId");
            int cityId;
            value.CityId = city != null
                && city.State == UiObservationState.Verified
                && int.TryParse(city.Value, NumberStyles.None, CultureInfo.InvariantCulture, out cityId)
                    ? cityId : -1;

            DomesticHoverCommand command = default(DomesticHoverCommand);
            value.HoveredCommandVerified = report.HoveredCommandState == UiObservationState.Verified
                && TryParseHover(report.HoveredCommand, out command);
            value.HoveredCommand = command;
            value.StructuralToken = BuildStructuralToken(report);
            return value;
        }

        private static UiObservationField FindField(San9Pk101UiObservationReport report, string name)
        {
            return report.Fields == null
                ? null
                : report.Fields.FirstOrDefault(field => field != null && string.Equals(field.Name, name, StringComparison.Ordinal));
        }

        private static bool TryParseHover(string value, out DomesticHoverCommand command)
        {
            if (string.Equals(value, "Commerce", StringComparison.Ordinal)) { command = DomesticHoverCommand.Commerce; return true; }
            if (string.Equals(value, "Cultivate", StringComparison.Ordinal)) { command = DomesticHoverCommand.Cultivate; return true; }
            if (string.Equals(value, "Repair", StringComparison.Ordinal)) { command = DomesticHoverCommand.Repair; return true; }
            command = default(DomesticHoverCommand);
            return false;
        }

        private static string BuildStructuralToken(San9Pk101UiObservationReport report)
        {
            StringBuilder builder = new StringBuilder();
            AppendObject(builder, report.Scene);
            AppendObject(builder, report.SchedulerRoot);
            AppendObject(builder, report.DomesticController);
            if (report.CommandMenu != null)
            {
                builder.Append('|').Append(report.CommandMenu.Address)
                    .Append(':').Append(report.CommandMenu.Vtable)
                    .Append(':').Append(report.CommandMenu.WindowHandle);
            }
            builder.Append('|').Append(report.RawSceneMode)
                .Append(':').Append(report.RawSceneSelector)
                .Append(':').Append(report.RawStrategicTime);
            if (report.TaskChain != null)
            {
                foreach (UiTaskNodeObservation node in report.TaskChain)
                    builder.Append('|').Append(node == null ? string.Empty : node.ObservationToken);
            }
            return builder.ToString();
        }

        private static void AppendObject(StringBuilder builder, UiObjectObservation observation)
        {
            builder.Append('|').Append(observation == null ? string.Empty : observation.ObservationToken);
        }
    }
}
