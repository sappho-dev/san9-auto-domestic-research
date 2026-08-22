using System;

namespace San9AutoDomestic.Input.Win32
{
    public enum DomesticHoverCommand
    {
        Commerce,
        Cultivate,
        Repair
    }

    public enum TargetedInputStatus
    {
        Completed,
        RejectedBeforeSend,
        AbortUncertain
    }

    public enum TargetedClickMode
    {
        PostMessage,
        SendInput,
        SendInputStaged
    }

    public sealed class TargetedHoverAuthorization
    {
        internal TargetedHoverAuthorization()
        {
            FailureCode = string.Empty;
            FailureMessage = string.Empty;
            WindowObservationToken = string.Empty;
            StructuralToken = string.Empty;
        }

        public bool CanAuthorize { get; internal set; }
        public string FailureCode { get; internal set; }
        public string FailureMessage { get; internal set; }
        public DateTimeOffset CapturedUtc { get; internal set; }
        public int ProcessId { get; internal set; }
        public long ProcessCreationFileTimeUtc { get; internal set; }
        public long MainWindowHandle { get; internal set; }
        public int CityId { get; internal set; }
        public DomesticHoverCommand HoveredCommand { get; internal set; }
        public string WindowObservationToken { get; internal set; }
        public string StructuralToken { get; internal set; }
    }

    public sealed class FacilitiesCitySwitchRequest
    {
        public const int FacilitiesButtonClientX = 929;
        public const int FacilitiesButtonClientY = 352;

        public FacilitiesCitySwitchRequest(
            Guid authorizationId,
            TargetedHoverAuthorization sourceMenuAuthorization,
            int targetCityId,
            int targetRowClientX,
            int targetRowClientY)
        {
            if (authorizationId == Guid.Empty) throw new ArgumentException("Authorization ID must be non-empty.", "authorizationId");
            if (sourceMenuAuthorization == null) throw new ArgumentNullException("sourceMenuAuthorization");
            if (!sourceMenuAuthorization.CanAuthorize) throw new ArgumentException("The source menu capture is not authorizable.", "sourceMenuAuthorization");
            if (sourceMenuAuthorization.ProcessId <= 0
                || sourceMenuAuthorization.ProcessCreationFileTimeUtc <= 0
                || sourceMenuAuthorization.MainWindowHandle <= 0)
                throw new ArgumentException("The source menu capture has no exact process binding.", "sourceMenuAuthorization");
            if (sourceMenuAuthorization.CityId < 0 || sourceMenuAuthorization.CityId >= 50)
                throw new ArgumentException("The source menu capture has an invalid city ID.", "sourceMenuAuthorization");
            if (targetCityId < 0 || targetCityId >= 50) throw new ArgumentOutOfRangeException("targetCityId");
            if (targetCityId == sourceMenuAuthorization.CityId)
                throw new ArgumentException("Source and target city must differ.", "targetCityId");
            if (targetRowClientX < 0 || targetRowClientX >= 1024) throw new ArgumentOutOfRangeException("targetRowClientX");
            if (targetRowClientY < 0 || targetRowClientY >= 768) throw new ArgumentOutOfRangeException("targetRowClientY");
            if (string.IsNullOrEmpty(sourceMenuAuthorization.WindowObservationToken)
                || string.IsNullOrEmpty(sourceMenuAuthorization.StructuralToken))
                throw new ArgumentException("The source menu capture has no observation binding.", "sourceMenuAuthorization");

            AuthorizationId = authorizationId;
            ProcessId = sourceMenuAuthorization.ProcessId;
            ProcessCreationFileTimeUtc = sourceMenuAuthorization.ProcessCreationFileTimeUtc;
            MainWindowHandle = sourceMenuAuthorization.MainWindowHandle;
            SourceCityId = sourceMenuAuthorization.CityId;
            TargetCityId = targetCityId;
            TargetRowClientX = targetRowClientX;
            TargetRowClientY = targetRowClientY;
            SourceMenuObservationToken = sourceMenuAuthorization.WindowObservationToken;
            SourceMenuStructuralToken = sourceMenuAuthorization.StructuralToken;
            AuthorizationCapturedUtc = sourceMenuAuthorization.CapturedUtc;
        }

        public Guid AuthorizationId { get; private set; }
        public int ProcessId { get; private set; }
        public long ProcessCreationFileTimeUtc { get; private set; }
        public long MainWindowHandle { get; private set; }
        public int SourceCityId { get; private set; }
        public int TargetCityId { get; private set; }
        public int TargetRowClientX { get; private set; }
        public int TargetRowClientY { get; private set; }
        public string SourceMenuObservationToken { get; private set; }
        public string SourceMenuStructuralToken { get; private set; }
        public DateTimeOffset AuthorizationCapturedUtc { get; private set; }
    }

    public sealed class FacilitiesMapAuthorization
    {
        internal FacilitiesMapAuthorization()
        {
            FailureCode = string.Empty;
            FailureMessage = string.Empty;
            WindowObservationToken = string.Empty;
            StructuralToken = string.Empty;
        }

        public bool CanAuthorize { get; internal set; }
        public string FailureCode { get; internal set; }
        public string FailureMessage { get; internal set; }
        public DateTimeOffset CapturedUtc { get; internal set; }
        public int ProcessId { get; internal set; }
        public long ProcessCreationFileTimeUtc { get; internal set; }
        public long MainWindowHandle { get; internal set; }
        public string WindowObservationToken { get; internal set; }
        public string StructuralToken { get; internal set; }
    }

    public sealed class FacilitiesRowSelectionRequest
    {
        public FacilitiesRowSelectionRequest(
            Guid authorizationId,
            FacilitiesMapAuthorization authorization,
            int targetCityId,
            int targetRowClientX,
            int targetRowClientY)
            : this(authorizationId, authorization, targetCityId, targetRowClientX, targetRowClientY, TargetedClickMode.PostMessage)
        {
        }

        public FacilitiesRowSelectionRequest(
            Guid authorizationId,
            FacilitiesMapAuthorization authorization,
            int targetCityId,
            int targetRowClientX,
            int targetRowClientY,
            TargetedClickMode clickMode)
        {
            if (authorizationId == Guid.Empty) throw new ArgumentException("Authorization ID must be non-empty.", "authorizationId");
            if (authorization == null) throw new ArgumentNullException("authorization");
            if (!authorization.CanAuthorize) throw new ArgumentException("The map capture is not authorizable.", "authorization");
            if (authorization.ProcessId <= 0 || authorization.ProcessCreationFileTimeUtc <= 0 || authorization.MainWindowHandle <= 0)
                throw new ArgumentException("The map capture has no exact process binding.", "authorization");
            if (string.IsNullOrEmpty(authorization.WindowObservationToken) || string.IsNullOrEmpty(authorization.StructuralToken))
                throw new ArgumentException("The map capture has no observation binding.", "authorization");
            if (targetCityId < 0 || targetCityId >= 50) throw new ArgumentOutOfRangeException("targetCityId");
            if (targetRowClientX < 0 || targetRowClientX >= 1024) throw new ArgumentOutOfRangeException("targetRowClientX");
            if (targetRowClientY < 0 || targetRowClientY >= 768) throw new ArgumentOutOfRangeException("targetRowClientY");
            if (!Enum.IsDefined(typeof(TargetedClickMode), clickMode)) throw new ArgumentOutOfRangeException("clickMode");

            AuthorizationId = authorizationId;
            ProcessId = authorization.ProcessId;
            ProcessCreationFileTimeUtc = authorization.ProcessCreationFileTimeUtc;
            MainWindowHandle = authorization.MainWindowHandle;
            TargetCityId = targetCityId;
            TargetRowClientX = targetRowClientX;
            TargetRowClientY = targetRowClientY;
            ClickMode = clickMode;
            PreObservationToken = authorization.WindowObservationToken;
            PreStructuralToken = authorization.StructuralToken;
            AuthorizationCapturedUtc = authorization.CapturedUtc;
        }

        public Guid AuthorizationId { get; private set; }
        public int ProcessId { get; private set; }
        public long ProcessCreationFileTimeUtc { get; private set; }
        public long MainWindowHandle { get; private set; }
        public int TargetCityId { get; private set; }
        public int TargetRowClientX { get; private set; }
        public int TargetRowClientY { get; private set; }
        public TargetedClickMode ClickMode { get; private set; }
        public string PreObservationToken { get; private set; }
        public string PreStructuralToken { get; private set; }
        public DateTimeOffset AuthorizationCapturedUtc { get; private set; }
    }

    public sealed class TargetedHoverRequest
    {
        public TargetedHoverRequest(
            Guid authorizationId,
            TargetedHoverAuthorization authorization,
            DomesticHoverCommand targetCommand,
            int clientX,
            int clientY)
        {
            if (authorizationId == Guid.Empty) throw new ArgumentException("Authorization ID must be non-empty.", "authorizationId");
            if (authorization == null) throw new ArgumentNullException("authorization");
            if (!authorization.CanAuthorize) throw new ArgumentException("The capture is not authorizable.", "authorization");
            if (authorization.ProcessId <= 0 || authorization.ProcessCreationFileTimeUtc <= 0 || authorization.MainWindowHandle <= 0)
                throw new ArgumentException("The capture has no exact process binding.", "authorization");
            if (authorization.CityId < 0 || authorization.CityId >= 50)
                throw new ArgumentException("The capture has an invalid city ID.", "authorization");
            if (string.IsNullOrEmpty(authorization.WindowObservationToken))
                throw new ArgumentException("The capture has no observation token.", "authorization");
            if (authorization.HoveredCommand == targetCommand)
                throw new ArgumentException("Source and target hover commands must differ.", "targetCommand");
            if (!Enum.IsDefined(typeof(DomesticHoverCommand), targetCommand))
                throw new ArgumentOutOfRangeException("targetCommand");
            if (clientX < 0 || clientX >= 1024) throw new ArgumentOutOfRangeException("clientX");
            if (clientY < 0 || clientY >= 768) throw new ArgumentOutOfRangeException("clientY");

            AuthorizationId = authorizationId;
            ProcessId = authorization.ProcessId;
            ProcessCreationFileTimeUtc = authorization.ProcessCreationFileTimeUtc;
            MainWindowHandle = authorization.MainWindowHandle;
            CityId = authorization.CityId;
            SourceCommand = authorization.HoveredCommand;
            TargetCommand = targetCommand;
            ClientX = clientX;
            ClientY = clientY;
            PreObservationToken = authorization.WindowObservationToken;
            AuthorizationCapturedUtc = authorization.CapturedUtc;
        }

        public Guid AuthorizationId { get; private set; }
        public int ProcessId { get; private set; }
        public long ProcessCreationFileTimeUtc { get; private set; }
        public long MainWindowHandle { get; private set; }
        public int CityId { get; private set; }
        public DomesticHoverCommand SourceCommand { get; private set; }
        public DomesticHoverCommand TargetCommand { get; private set; }
        public int ClientX { get; private set; }
        public int ClientY { get; private set; }
        public string PreObservationToken { get; private set; }
        public DateTimeOffset AuthorizationCapturedUtc { get; private set; }
    }

    public sealed class TargetedInputResult
    {
        internal TargetedInputResult()
        {
            Code = string.Empty;
            Message = string.Empty;
        }

        public TargetedInputStatus Status { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public Guid AuthorizationId { get; internal set; }
        public int MessageCount { get; internal set; }
        public int GestureCount { get; internal set; }
        public int? ObservedCityId { get; internal set; }
        public bool BindingStable { get; internal set; }
        public bool ForegroundStable { get; internal set; }
        public bool StateVerified { get; internal set; }
    }

    public sealed class ProgramInputDispatchReport
    {
        internal ProgramInputDispatchReport()
        {
            Source = "ProgramDispatched";
            Error = string.Empty;
        }

        public string Source { get; private set; }
        public DateTimeOffset StartedUtc { get; internal set; }
        public DateTimeOffset FinishedUtc { get; internal set; }
        public long StopwatchFrequency { get; internal set; }
        public long StartTimestamp { get; internal set; }
        public long? MoveTimestamp { get; internal set; }
        public long? DownTimestamp { get; internal set; }
        public long? UpTimestamp { get; internal set; }
        public int ProcessId { get; internal set; }
        public long ProcessCreationFileTimeUtc { get; internal set; }
        public long MainWindowHandle { get; internal set; }
        public long ForegroundWindowHandleAtStart { get; internal set; }
        public int ForegroundProcessIdAtStart { get; internal set; }
        public long ForegroundWindowHandleAtDown { get; internal set; }
        public int ForegroundProcessIdAtDown { get; internal set; }
        public int ClientX { get; internal set; }
        public int ClientY { get; internal set; }
        public int ScreenX { get; internal set; }
        public int ScreenY { get; internal set; }
        public bool MoveInserted { get; internal set; }
        public bool DownInserted { get; internal set; }
        public bool UpInserted { get; internal set; }
        public int AttemptedInputCount { get; internal set; }
        public string Error { get; internal set; }
    }
}
