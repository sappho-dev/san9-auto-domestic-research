using System;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public enum DiagnosticSeverity
    {
        Information,
        Warning,
        Blocking
    }

    public sealed class DiagnosticIssue
    {
        internal DiagnosticIssue(string code, DiagnosticSeverity severity, string message)
        {
            Code = code ?? string.Empty;
            Severity = severity;
            Message = message ?? string.Empty;
        }

        public string Code { get; private set; }
        public DiagnosticSeverity Severity { get; private set; }
        public string Message { get; private set; }

        public override string ToString()
        {
            return string.Format("[{0}] {1}: {2}", Severity, Code, Message);
        }
    }

    public sealed class FileValidationDiagnostic
    {
        internal FileValidationDiagnostic()
        {
            Issues = new DiagnosticIssue[0];
        }

        public string RequestedPath { get; internal set; }
        public string CanonicalPath { get; internal set; }
        public bool Exists { get; internal set; }
        public bool PathMatches { get; internal set; }
        public long? ActualSize { get; internal set; }
        public bool SizeMatches { get; internal set; }
        public string ActualFileVersion { get; internal set; }
        public string RawFileVersion { get; internal set; }
        public bool VersionMatches { get; internal set; }
        public string ActualSha256 { get; internal set; }
        public bool Sha256Matches { get; internal set; }
        public ushort? ActualPeMachine { get; internal set; }
        public bool ArchitectureMatches { get; internal set; }
        public bool IsValid { get; internal set; }
        public DiagnosticIssue[] Issues { get; internal set; }
    }

    public enum ProcessDiscoveryStatus
    {
        NotFound,
        ExpectedProcessWithoutWindow,
        Unique,
        Ambiguous,
        Failed
    }

    public sealed class ProcessCandidateDiagnostic
    {
        internal ProcessCandidateDiagnostic()
        {
            WindowHandles = new long[0];
        }

        public int ProcessId { get; internal set; }
        public string ProcessName { get; internal set; }
        public string ImagePath { get; internal set; }
        public bool ImagePathQuerySucceeded { get; internal set; }
        public bool PathMatches { get; internal set; }
        public bool HasExpectedWindowClass { get; internal set; }
        public long[] WindowHandles { get; internal set; }
        public string QueryError { get; internal set; }
    }

    public sealed class ProcessDiscoveryDiagnostic
    {
        internal ProcessDiscoveryDiagnostic()
        {
            Candidates = new ProcessCandidateDiagnostic[0];
            Issues = new DiagnosticIssue[0];
        }

        public ProcessDiscoveryStatus Status { get; internal set; }
        public ProcessCandidateDiagnostic[] Candidates { get; internal set; }
        public int? SelectedProcessId { get; internal set; }
        public string SelectedImagePath { get; internal set; }
        public DiagnosticIssue[] Issues { get; internal set; }
    }

    public enum ConflictKind
    {
        KnownProcess,
        KnownModule,
        LocalProxyModule,
        InspectionFailure
    }

    public sealed class ConflictDiagnostic
    {
        internal ConflictDiagnostic()
        {
        }

        public ConflictKind Kind { get; internal set; }
        public string Code { get; internal set; }
        public string Name { get; internal set; }
        public int? ProcessId { get; internal set; }
        public long? ProcessCreationFileTimeUtc { get; internal set; }
        public string Path { get; internal set; }
        public long? FileSize { get; internal set; }
        public string FileSha256 { get; internal set; }
        public uint? ModuleBaseAddress { get; internal set; }
        public uint? ModuleImageSize { get; internal set; }
        public string Reason { get; internal set; }
        public bool IsBlocking { get; internal set; }
    }

    public sealed class ConflictScanDiagnostic
    {
        internal ConflictScanDiagnostic()
        {
            Conflicts = new ConflictDiagnostic[0];
            Issues = new DiagnosticIssue[0];
        }

        public bool ProcessScanSucceeded { get; internal set; }
        public bool ModuleScanAttempted { get; internal set; }
        public bool ModuleScanSucceeded { get; internal set; }
        public int InspectedModuleCount { get; internal set; }
        public bool HasBlockingConflicts { get; internal set; }
        public ConflictDiagnostic[] Conflicts { get; internal set; }
        public DiagnosticIssue[] Issues { get; internal set; }
    }

    public sealed class ReadOnlyConnectionDiagnostic
    {
        internal ReadOnlyConnectionDiagnostic()
        {
            RequestedAccess = "PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ";
        }

        public bool Attempted { get; internal set; }
        public bool Connected { get; internal set; }
        public int? ProcessId { get; internal set; }
        public string ImagePath { get; internal set; }
        public string RequestedAccess { get; private set; }
        public int NativeError { get; internal set; }
        public string Error { get; internal set; }
        public long? ProcessCreationFileTimeUtc { get; internal set; }
        public uint? MainModuleBaseAddress { get; internal set; }
        public uint? MainModuleSize { get; internal set; }
    }

    public sealed class San9Pk101DiagnosticReport
    {
        internal San9Pk101DiagnosticReport()
        {
            CreatedUtc = DateTimeOffset.UtcNow;
            Issues = new DiagnosticIssue[0];
        }

        public DateTimeOffset CreatedUtc { get; internal set; }
        public FileValidationDiagnostic FileValidation { get; internal set; }
        public ProcessDiscoveryDiagnostic ProcessDiscovery { get; internal set; }
        public ReadOnlyConnectionDiagnostic ReadOnlyConnection { get; internal set; }
        public ConflictScanDiagnostic ConflictScan { get; internal set; }
        public EasyRuntimeCompatibilityReport EasyCompatibility { get; internal set; }
        public bool ExecutionAllowed { get; internal set; }
        public DiagnosticIssue[] Issues { get; internal set; }
        internal Guid? ReadOnlyConnectionBindingId { get; set; }
    }
}
