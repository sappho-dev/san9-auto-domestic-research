using System;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public enum UiObservationState
    {
        Verified,
        Unknown,
        Contradictory
    }

    public enum UiLayerKind
    {
        Unknown,
        StrategicMapCandidate,
        DomesticCommandMenu,
        DomesticOuterDialog,
        OfficerSelector,
        DomesticConfirmation
    }

    public sealed class UiObservationField
    {
        internal UiObservationField(string name, UiObservationState state, string value, string evidence)
        {
            Name = name ?? string.Empty;
            State = state;
            Value = value ?? string.Empty;
            Evidence = evidence ?? string.Empty;
        }

        public string Name { get; private set; }
        public UiObservationState State { get; private set; }
        public string Value { get; private set; }
        public string Evidence { get; private set; }
    }

    public sealed class UiObjectObservation
    {
        internal UiObjectObservation()
        {
            ObservationToken = string.Empty;
        }

        public uint Address { get; internal set; }
        public uint Vtable { get; internal set; }
        public uint WindowHandle { get; internal set; }
        public string ObservationToken { get; internal set; }
    }

    public sealed class UiTaskNodeObservation
    {
        internal UiTaskNodeObservation()
        {
            ObservationToken = string.Empty;
        }

        public int Depth { get; internal set; }
        public uint Address { get; internal set; }
        public uint Vtable { get; internal set; }
        public uint ChildPointer { get; internal set; }
        public uint PendingPointer { get; internal set; }
        public string ObservationToken { get; internal set; }
    }

    public sealed class San9Pk101UiObservationReport
    {
        internal San9Pk101UiObservationReport()
        {
            Fields = new UiObservationField[0];
            TaskChain = new UiTaskNodeObservation[0];
            CandidateOfficerIds = new int[0];
            SelectedOfficerIds = new int[0];
            WorkingOfficerIds = new int[0];
            CommittedOfficerIds = new int[0];
            Issues = new AvailabilityIssue[0];
            HoveredCommand = string.Empty;
        }

        public San9Pk101DiagnosticReport Baseline { get; internal set; }
        public bool ReadSucceeded { get; internal set; }
        public bool StableAbc { get; internal set; }
        public int StabilityAttemptCount { get; internal set; }
        public int StableDurationMilliseconds { get; internal set; }
        public DateTimeOffset CapturedUtc { get; internal set; }
        public int? ProcessId { get; internal set; }
        public long? ProcessCreationFileTimeUtc { get; internal set; }
        public long? MainWindowHandle { get; internal set; }
        public string WindowObservationToken { get; internal set; }
        public uint RawSceneMode { get; internal set; }
        public uint RawSceneSelector { get; internal set; }
        public uint RawStrategicTime { get; internal set; }
        public UiLayerKind Layer { get; internal set; }
        public UiObservationState HoveredCommandState { get; internal set; }
        public string HoveredCommand { get; internal set; }
        public UiObjectObservation Scene { get; internal set; }
        public UiObjectObservation SchedulerRoot { get; internal set; }
        public UiObjectObservation DomesticController { get; internal set; }
        public UiObjectObservation CommandMenu { get; internal set; }
        public UiObjectObservation OuterDialog { get; internal set; }
        public UiObjectObservation Selector { get; internal set; }
        public UiTaskNodeObservation[] TaskChain { get; internal set; }
        public int[] CandidateOfficerIds { get; internal set; }
        public int[] SelectedOfficerIds { get; internal set; }
        public int[] WorkingOfficerIds { get; internal set; }
        public int[] CommittedOfficerIds { get; internal set; }
        public UiObservationField[] Fields { get; internal set; }
        public AvailabilityIssue[] Issues { get; internal set; }

        public bool PlanningReady { get { return false; } }
        public bool VerifiedNativeCapability { get { return false; } }
        public bool IsActionable { get { return false; } }
        public bool CommitAuthorized { get { return false; } }
    }
}
