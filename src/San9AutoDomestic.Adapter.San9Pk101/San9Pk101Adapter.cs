using System;
using System.Collections.Generic;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public sealed class San9Pk101Adapter
    {
        private readonly San9Pk101TargetValidator targetValidator;
        private readonly San9Pk101ProcessDiscovery processDiscovery;
        private readonly San9Pk101ConflictDetector conflictDetector;
        private readonly San9Pk101UiObservationReader uiObservationReader;
        private readonly San9Pk101EasyRuntimeInspector easyRuntimeInspector;

        public San9Pk101Adapter()
        {
            targetValidator = new San9Pk101TargetValidator();
            processDiscovery = new San9Pk101ProcessDiscovery();
            conflictDetector = new San9Pk101ConflictDetector();
            uiObservationReader = new San9Pk101UiObservationReader();
            easyRuntimeInspector = new San9Pk101EasyRuntimeInspector(EasyHookEpochTracker.Shared);
        }

        public San9Pk101DiagnosticReport Diagnose()
        {
            ReadOnlyProcessConnection connection;
            San9Pk101DiagnosticReport report = Diagnose(out connection);
            if (connection != null)
            {
                connection.Dispose();
            }

            return report;
        }

        public San9Pk101DiagnosticReport Diagnose(out ReadOnlyProcessConnection connection)
        {
            connection = null;
            San9Pk101DiagnosticReport report = new San9Pk101DiagnosticReport();
            List<DiagnosticIssue> allIssues = new List<DiagnosticIssue>();

            report.FileValidation = targetValidator.Validate();
            allIssues.AddRange(report.FileValidation.Issues);

            report.ProcessDiscovery = processDiscovery.Discover();
            allIssues.AddRange(report.ProcessDiscovery.Issues);

            ReadOnlyConnectionDiagnostic connectionDiagnostic = new ReadOnlyConnectionDiagnostic();
            if (report.FileValidation.IsValid
                && report.ProcessDiscovery.Status == ProcessDiscoveryStatus.Unique
                && report.ProcessDiscovery.SelectedProcessId.HasValue)
            {
                connection = ReadOnlyProcessConnection.TryOpen(
                    report.ProcessDiscovery.SelectedProcessId.Value,
                    San9Pk101Target.ExpectedExecutablePath,
                    out connectionDiagnostic);
                if (!connectionDiagnostic.Connected)
                {
                    allIssues.Add(new DiagnosticIssue(
                        "READ_ONLY_CONNECTION_FAILED",
                        DiagnosticSeverity.Blocking,
                        connectionDiagnostic.Error ?? "The read-only connection failed."));
                }
            }

            report.ReadOnlyConnection = connectionDiagnostic;
            if (connection != null && connection.IsConnected)
            {
                report.ReadOnlyConnectionBindingId = connection.DiagnosticBindingId;
            }
            report.ConflictScan = conflictDetector.Scan(
                report.ProcessDiscovery.SelectedProcessId,
                San9Pk101Target.ExpectedExecutablePath);
            allIssues.AddRange(report.ConflictScan.Issues);

            report.EasyCompatibility = easyRuntimeInspector.Inspect(connection, report);
            allIssues.AddRange(report.EasyCompatibility.Issues);

            report.ExecutionAllowed = San9Pk101DiagnosticGate.ComputeExecutionAllowed(
                report,
                allIssues);

            allIssues.Add(new DiagnosticIssue(
                "V0_READ_ONLY",
                DiagnosticSeverity.Information,
                "P0 validates exact Easy ownership read-only; no native execution is authorized."));
            if (!report.ExecutionAllowed)
            {
                allIssues.Add(new DiagnosticIssue(
                    "EXECUTION_GATE_BLOCKED",
                    DiagnosticSeverity.Blocking,
                    "A future execution mode must remain disabled for this diagnostic snapshot."));
            }

            report.Issues = allIssues.ToArray();
            return report;
        }

        public San9Pk101ReadReport ReadSnapshot()
        {
            ReadOnlyProcessConnection connection = null;
            San9Pk101DiagnosticReport baseline = Diagnose(out connection);
            if (connection == null || !connection.IsConnected)
            {
                San9Pk101ReadReport unavailable = new San9Pk101ReadReport();
                unavailable.Baseline = baseline;
                unavailable.Issues = new[]
                {
                    new ReadInvariantDiagnostic(
                        "READ_ONLY_CONNECTION_UNAVAILABLE",
                        DiagnosticSeverity.Blocking,
                        ReadEntityKind.Snapshot,
                        null,
                        "A validated unique read-only game connection is required for V1-read.")
                };
                return unavailable;
            }

            try
            {
                return new San9Pk101SnapshotReader().Read(connection, baseline);
            }
            finally
            {
                connection.Dispose();
            }
        }

        public San9Pk101AvailabilityReport ReadAvailability()
        {
            ReadOnlyProcessConnection connection = null;
            San9Pk101DiagnosticReport baseline = Diagnose(out connection);
            if (connection == null || !connection.IsConnected)
            {
                San9Pk101AvailabilityReport unavailable = new San9Pk101AvailabilityReport();
                unavailable.Baseline = baseline;
                unavailable.ObservationBlocked = true;
                unavailable.Issues = new[]
                {
                    new AvailabilityIssue(
                        "READ_ONLY_CONNECTION_UNAVAILABLE",
                        DiagnosticSeverity.Blocking,
                        "A validated unique read-only game connection is required for V2 observation.")
                };
                return unavailable;
            }

            try
            {
                return new San9Pk101AvailabilityReader().Read(
                    connection,
                    baseline,
                    San9Pk101Target.ExpectedExecutablePath);
            }
            finally
            {
                connection.Dispose();
            }
        }

        public San9Pk101RootTraceSample ReadRootTraceSample(
            ReadOnlyProcessConnection connection,
            San9Pk101DiagnosticReport baseline)
        {
            string gateError;
            if (!San9Pk101DiagnosticGate.TryValidateExactConflictFreeConnection(
                connection,
                baseline,
                out gateError))
            {
                return new San9Pk101RootTraceSample
                {
                    ReadSucceeded = false,
                    FailureCode = "V4_OBSERVATION_GATE_BLOCKED",
                    FailureMessage = gateError
                };
            }

            return new San9Pk101RootTraceReader().Read(connection);
        }

        public San9Pk101UiObservationReport ReadUiObservation()
        {
            ReadOnlyProcessConnection connection = null;
            San9Pk101DiagnosticReport baseline = Diagnose(out connection);
            if (connection == null || !connection.IsConnected)
            {
                return UnavailableUiObservation(
                    baseline,
                    "UI_READ_ONLY_CONNECTION_UNAVAILABLE",
                    "A validated unique read-only game connection is required for UI observation.");
            }

            try
            {
                string gateError;
                if (!San9Pk101DiagnosticGate.TryValidateExactConflictFreeConnection(
                    connection,
                    baseline,
                    out gateError))
                {
                    return UnavailableUiObservation(
                        baseline,
                        "UI_OBSERVATION_GATE_BLOCKED",
                        gateError);
                }

                return uiObservationReader.Read(connection, baseline);
            }
            finally
            {
                connection.Dispose();
            }
        }

        private static San9Pk101UiObservationReport UnavailableUiObservation(
            San9Pk101DiagnosticReport baseline,
            string code,
            string message)
        {
            return new San9Pk101UiObservationReport
            {
                Baseline = baseline,
                ReadSucceeded = false,
                StableAbc = false,
                CapturedUtc = DateTimeOffset.UtcNow,
                WindowObservationToken = string.Empty,
                Layer = UiLayerKind.Unknown,
                Issues = new[] { new AvailabilityIssue(code, DiagnosticSeverity.Blocking, message) }
            };
        }
    }
}
