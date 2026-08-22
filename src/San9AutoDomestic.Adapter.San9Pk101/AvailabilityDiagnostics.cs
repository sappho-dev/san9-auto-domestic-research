using System;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public enum DomesticCommandKind
    {
        Patrol = 0,
        Commerce = 1,
        Cultivate = 2,
        Repair = 3,
        Train = 5
    }

    public enum AvailabilityEvidence
    {
        ConfirmedRawEquivalent,
        ConfirmedStaticRule,
        DerivedRawCandidate,
        Unknown
    }

    public sealed class AvailabilityIssue
    {
        internal AvailabilityIssue(string code, DiagnosticSeverity severity, string message)
        {
            Code = code ?? string.Empty;
            Severity = severity;
            Message = message ?? string.Empty;
        }

        public string Code { get; private set; }
        public DiagnosticSeverity Severity { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class AvailabilityCondition
    {
        internal AvailabilityCondition(
            string code,
            bool? passed,
            AvailabilityEvidence evidence,
            string detail)
        {
            Code = code ?? string.Empty;
            Passed = passed;
            Evidence = evidence;
            Detail = detail ?? string.Empty;
        }

        public string Code { get; private set; }
        public bool? Passed { get; private set; }
        public AvailabilityEvidence Evidence { get; private set; }
        public string Detail { get; private set; }
    }

    public sealed class CodeAnchorObservation
    {
        internal CodeAnchorObservation()
        {
        }

        public string Name { get; internal set; }
        public uint Address { get; internal set; }
        public int Length { get; internal set; }
        public string ExpectedSha256 { get; internal set; }
        public string DiskSha256 { get; internal set; }
        public string LiveSha256 { get; internal set; }
        public bool DiskMatchesExpected { get; internal set; }
        public bool LiveWasStable { get; internal set; }
        public bool LiveMatchesDisk { get; internal set; }
        public string Error { get; internal set; }
    }

    public sealed class RawContextToken
    {
        internal RawContextToken()
        {
        }

        public uint CurrentCityPointerCandidate { get; internal set; }
        public uint RawValue2480 { get; internal set; }
        public uint PhaseCandidate { get; internal set; }
        public uint StrategicTimeCounterCandidate { get; internal set; }
        public bool IsStrategicInputPhaseCandidate { get; internal set; }
        public bool PhaseSemanticsVerified { get { return false; } }
        public string StableRawSha256 { get; internal set; }
        public string TokenSha256 { get; internal set; }
    }

    public sealed class OfficerRankingPreview
    {
        internal OfficerRankingPreview()
        {
        }

        public int PersonId { get; internal set; }
        public string Name { get; internal set; }
        public int SourceListIndex { get; internal set; }
        public int Score { get; internal set; }
    }

    public sealed class CommandAvailabilityObservation
    {
        internal CommandAvailabilityObservation()
        {
            ReadyCandidatesInSourceOrder = new OfficerRankingPreview[0];
            RankedCandidates = new OfficerRankingPreview[0];
            Conditions = new AvailabilityCondition[0];
        }

        public DomesticCommandKind Command { get; internal set; }
        public int ReadyCandidateCount { get; internal set; }
        public OfficerRankingPreview[] ReadyCandidatesInSourceOrder { get; internal set; }
        public OfficerRankingPreview[] RankedCandidates { get; internal set; }
        public int ProvisionalCostPerOfficer { get; internal set; }
        public int? NativeOneOfficerCostCandidate { get; internal set; }
        public bool KnownStaticSubsetWouldPass { get; internal set; }
        [Obsolete("No native entry point is called. Use KnownStaticSubsetWouldPass and NativeEntryResultVerified.")]
        public bool NativeEntryWouldEnable { get { return false; } }
        public bool NativeEntryResultVerified { get { return false; } }
        public AvailabilityCondition[] Conditions { get; internal set; }
    }

    public sealed class CityAvailabilityObservation
    {
        internal CityAvailabilityObservation()
        {
            Commands = new CommandAvailabilityObservation[0];
        }

        public int CityId { get; internal set; }
        public string CityName { get; internal set; }
        public int? CorpsId { get; internal set; }
        public int CorpsMoney { get; internal set; }
        public uint CorpsFlags { get; internal set; }
        public bool IsDirectlyControlled { get; internal set; }
        public CommandAvailabilityObservation[] Commands { get; internal set; }
    }

    public sealed class San9Pk101AvailabilityReport
    {
        internal San9Pk101AvailabilityReport()
        {
            CodeAnchors = new CodeAnchorObservation[0];
            Cities = new CityAvailabilityObservation[0];
            Issues = new AvailabilityIssue[0];
        }

        public San9Pk101DiagnosticReport Baseline { get; internal set; }
        public San9Pk101ReadReport StructureBefore { get; internal set; }
        public San9Pk101ReadReport StructureAfter { get; internal set; }
        public RawContextToken ContextToken { get; internal set; }
        public CodeAnchorObservation[] CodeAnchors { get; internal set; }
        public CityAvailabilityObservation[] Cities { get; internal set; }
        public AvailabilityIssue[] Issues { get; internal set; }
        public bool RawFieldsWereStable { get; internal set; }
        public bool CodeAnchorsWereStableAndExact { get; internal set; }
        public bool StructureWasRevalidated { get; internal set; }
        public bool ProcessIdentityRevalidatedAfterObservation { get; internal set; }
        public bool DataReadSucceeded { get; internal set; }
        public bool ObservationBlocked { get; internal set; }
        public bool PlanningReady { get { return false; } }
        public bool VerifiedNativeCapability { get { return false; } }
        public DateTimeOffset? ReadCompletedUtc { get; internal set; }
    }
}
