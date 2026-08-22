using System;
using System.Text;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.V0Diagnostics
{
    internal static class Program
    {
        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            San9Pk101Adapter adapter = new San9Pk101Adapter();
            ReadOnlyProcessConnection connection = null;

            try
            {
                San9Pk101DiagnosticReport report = adapter.Diagnose(out connection);
                PrintReport(report, connection);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("V0 diagnostics failed: {0}", exception);
                return 1;
            }
            finally
            {
                if (connection != null)
                {
                    connection.Dispose();
                }
            }
        }

        private static void PrintReport(
            San9Pk101DiagnosticReport report,
            ReadOnlyProcessConnection connection)
        {
            Console.WriteLine("San9AutoDomestic V0 diagnostic snapshot");
            Console.WriteLine("UTC: {0:O}", report.CreatedUtc);
            Console.WriteLine();

            FileValidationDiagnostic file = report.FileValidation;
            Console.WriteLine("[Target]");
            Console.WriteLine("Path: {0}", file.CanonicalPath ?? file.RequestedPath ?? "<none>");
            Console.WriteLine("Exists: {0}; exact path: {1}; size: {2}; version: {3}; SHA-256: {4}; x86: {5}",
                file.Exists,
                file.PathMatches,
                file.SizeMatches,
                file.VersionMatches,
                file.Sha256Matches,
                file.ArchitectureMatches);
            Console.WriteLine("Valid exact target: {0}", file.IsValid);
            Console.WriteLine("Actual size/version/hash/machine: {0} / {1} / {2} / {3}",
                file.ActualSize.HasValue ? file.ActualSize.Value.ToString() : "<unknown>",
                file.ActualFileVersion ?? "<unknown>",
                file.ActualSha256 ?? "<unknown>",
                file.ActualPeMachine.HasValue
                    ? string.Format("0x{0:X4}", file.ActualPeMachine.Value)
                    : "<unknown>");
            Console.WriteLine();

            ProcessDiscoveryDiagnostic discovery = report.ProcessDiscovery;
            Console.WriteLine("[Process discovery]");
            Console.WriteLine("Status: {0}; selected PID: {1}",
                discovery.Status,
                discovery.SelectedProcessId.HasValue
                    ? discovery.SelectedProcessId.Value.ToString()
                    : "<none>");
            foreach (ProcessCandidateDiagnostic candidate in discovery.Candidates)
            {
                Console.WriteLine("PID {0}: window={1}, exactPath={2}, path={3}",
                    candidate.ProcessId,
                    candidate.HasExpectedWindowClass,
                    candidate.PathMatches,
                    candidate.ImagePath ?? "<unavailable>");
            }
            Console.WriteLine();

            Console.WriteLine("[Read-only connection]");
            Console.WriteLine("Attempted: {0}; connected: {1}; access: {2}",
                report.ReadOnlyConnection.Attempted,
                report.ReadOnlyConnection.Connected,
                report.ReadOnlyConnection.RequestedAccess);
            Console.WriteLine("Live handle retained by this tool: {0}",
                connection != null && connection.IsConnected);
            if (!string.IsNullOrEmpty(report.ReadOnlyConnection.Error))
            {
                Console.WriteLine("Connection error: {0}", report.ReadOnlyConnection.Error);
            }
            Console.WriteLine();

            ConflictScanDiagnostic scan = report.ConflictScan;
            Console.WriteLine("[Conflicts]");
            Console.WriteLine("Process scan: {0}; module scan: attempted={1}, succeeded={2}, modules={3}",
                scan.ProcessScanSucceeded,
                scan.ModuleScanAttempted,
                scan.ModuleScanSucceeded,
                scan.InspectedModuleCount);
            if (scan.Conflicts.Length == 0)
            {
                Console.WriteLine("None detected.");
            }
            else
            {
                foreach (ConflictDiagnostic conflict in scan.Conflicts)
                {
                    Console.WriteLine("BLOCK={0} {1}/{2}: {3}; PID={4}; path={5}",
                        conflict.IsBlocking,
                        conflict.Kind,
                        conflict.Name,
                        conflict.Reason,
                        conflict.ProcessId.HasValue ? conflict.ProcessId.Value.ToString() : "<none>",
                        conflict.Path ?? "<unavailable>");
                }
            }
            Console.WriteLine();

            Console.WriteLine("Future execution gate: {0}",
                report.ExecutionAllowed ? "ALLOWED" : "BLOCKED");
            Console.WriteLine("V0 itself is always read-only and never submits game commands.");

            foreach (DiagnosticIssue issue in report.Issues)
            {
                if (issue.Severity != DiagnosticSeverity.Information)
                {
                    Console.WriteLine(issue.ToString());
                }
            }
        }
    }
}
