using System;
using System.Collections.Generic;
using System.Linq;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal static class San9Pk101DiagnosticGate
    {
        internal static bool ComputeExecutionAllowed(
            San9Pk101DiagnosticReport report,
            IEnumerable<DiagnosticIssue> issues)
        {
            return report != null
                && report.FileValidation != null
                && report.FileValidation.IsValid
                && report.ProcessDiscovery != null
                && report.ProcessDiscovery.Status == ProcessDiscoveryStatus.Unique
                && report.ProcessDiscovery.SelectedProcessId.HasValue
                && report.ProcessDiscovery.SelectedProcessId.Value > 0
                && San9Pk101TargetValidator.PathsEqual(
                    report.ProcessDiscovery.SelectedImagePath,
                    San9Pk101Target.ExpectedExecutablePath)
                && report.ReadOnlyConnection != null
                && report.ReadOnlyConnection.Connected
                && report.ReadOnlyConnection.ProcessId == report.ProcessDiscovery.SelectedProcessId.Value
                && San9Pk101TargetValidator.PathsEqual(
                    report.ReadOnlyConnection.ImagePath,
                    San9Pk101Target.ExpectedExecutablePath)
                && report.ReadOnlyConnection.ProcessCreationFileTimeUtc.HasValue
                && report.ReadOnlyConnection.ProcessCreationFileTimeUtc.Value > 0
                && report.ReadOnlyConnection.MainModuleBaseAddress == San9Pk101Target.ExpectedImageBase
                && report.ReadOnlyConnection.MainModuleSize == San9Pk101Target.ExpectedSizeOfImage
                && report.ConflictScan != null
                && report.ConflictScan.ProcessScanSucceeded
                && report.ConflictScan.ModuleScanAttempted
                && report.ConflictScan.ModuleScanSucceeded
                && !report.ConflictScan.HasBlockingConflicts
                && San9Pk101ConflictDetector.ContainsOnlyExactCompatibleEasy(report.ConflictScan)
                && HasExactEasyTicketBinding(report)
                && !HasBlockingIssue(issues);
        }

        internal static bool TryValidateExactConflictFreeReport(
            San9Pk101DiagnosticReport report,
            out string error)
        {
            error = null;
            if (report == null
                || report.FileValidation == null
                || !report.FileValidation.IsValid)
            {
                error = "The exact target file-validation gate is not valid.";
                return false;
            }

            if (report.ProcessDiscovery == null
                || report.ProcessDiscovery.Status != ProcessDiscoveryStatus.Unique
                || !report.ProcessDiscovery.SelectedProcessId.HasValue
                || report.ProcessDiscovery.SelectedProcessId.Value <= 0
                || !San9Pk101TargetValidator.PathsEqual(
                    report.ProcessDiscovery.SelectedImagePath,
                    San9Pk101Target.ExpectedExecutablePath))
            {
                error = "Exactly one matching target process is required.";
                return false;
            }

            if (report.ReadOnlyConnection == null
                || !report.ReadOnlyConnection.Connected
                || report.ReadOnlyConnection.ProcessId != report.ProcessDiscovery.SelectedProcessId.Value
                || !report.ReadOnlyConnection.ProcessCreationFileTimeUtc.HasValue
                || report.ReadOnlyConnection.ProcessCreationFileTimeUtc.Value <= 0
                || !report.ReadOnlyConnection.MainModuleBaseAddress.HasValue
                || report.ReadOnlyConnection.MainModuleBaseAddress.Value != San9Pk101Target.ExpectedImageBase
                || !report.ReadOnlyConnection.MainModuleSize.HasValue
                || report.ReadOnlyConnection.MainModuleSize.Value != San9Pk101Target.ExpectedSizeOfImage
                || !San9Pk101TargetValidator.PathsEqual(
                    report.ReadOnlyConnection.ImagePath,
                    San9Pk101Target.ExpectedExecutablePath))
            {
                error = "A complete query/read-only process binding is required.";
                return false;
            }

            if (report.ConflictScan == null
                || !report.ConflictScan.ProcessScanSucceeded
                || !report.ConflictScan.ModuleScanAttempted
                || !report.ConflictScan.ModuleScanSucceeded
                || report.ConflictScan.HasBlockingConflicts
                || !San9Pk101ConflictDetector.ContainsOnlyExactCompatibleEasy(report.ConflictScan))
            {
                error = "The conflict scan is incomplete or found something outside the exact Easy compatibility profile.";
                return false;
            }

            if (!HasExactEasyTicketBinding(report))
            {
                error = "The exact stable installed Easy runtime ownership ticket is absent or restart-latched.";
                return false;
            }

            if (report.Issues == null)
            {
                error = "The aggregate diagnostic issue set is absent.";
                return false;
            }

            if (HasBlockingIssue(report.Issues))
            {
                error = "The aggregate diagnostic report contains a blocking issue.";
                return false;
            }

            if (HasBlockingIssue(report.FileValidation.Issues)
                || HasBlockingIssue(report.ProcessDiscovery.Issues)
                || HasBlockingIssue(report.ConflictScan.Issues))
            {
                error = "A nested diagnostic component contains a blocking or incomplete issue set.";
                return false;
            }

            if (!report.ExecutionAllowed)
            {
                error = "The compatibility gate is blocked; V4 observation remains disabled.";
                return false;
            }

            return true;
        }

        internal static bool HasExactEasyTicketBinding(San9Pk101DiagnosticReport report)
        {
            if (report == null || report.ProcessDiscovery == null
                || report.ReadOnlyConnection == null || report.ConflictScan == null
                || report.EasyCompatibility == null)
                return false;
            EasyRuntimeCompatibilityReport compatibility = report.EasyCompatibility;
            EasyCompatibilityTicket ticket = compatibility.Ticket;
            if (compatibility.State != EasyRuntimeCompatibilityState.Installed
                || !compatibility.StableSnapshot
                || !compatibility.CompatibleForFutureBridge
                || compatibility.RestartRequired
                || compatibility.ExecutionAuthorized
                || HasBlockingIssue(compatibility.Issues)
                || compatibility.InstalledRedirectCount != EasyCompatibilityManifest.Redirects.Length
                || compatibility.OriginalRedirectCount != 0
                || compatibility.UnknownRedirectCount != 0
                || !string.Equals(compatibility.ManifestSha256,
                    EasyCompatibilityManifest.ManifestSha256, StringComparison.Ordinal)
                || ticket == null || ticket.ExecutionAuthorized || ticket.IsSecurityCapability
                || !string.Equals(ticket.ManifestSha256,
                    EasyCompatibilityManifest.ManifestSha256, StringComparison.Ordinal)
                || !IsUpperHexSha256(ticket.HookEpochId)
                || !IsUpperHexSha256(ticket.SnapshotFingerprintSha256))
                return false;

            int gameProcessId = report.ProcessDiscovery.SelectedProcessId.GetValueOrDefault();
            if (gameProcessId <= 0
                || ticket.GameProcessId != gameProcessId
                || report.ReadOnlyConnection.ProcessId != gameProcessId
                || ticket.GameProcessCreationFileTimeUtc
                    != report.ReadOnlyConnection.ProcessCreationFileTimeUtc.GetValueOrDefault())
                return false;
            ProcessCandidateDiagnostic[] candidates = (report.ProcessDiscovery.Candidates
                    ?? new ProcessCandidateDiagnostic[0])
                .Where(item => item != null && item.ProcessId == gameProcessId)
                .ToArray();
            if (candidates.Length != 1) return false;
            ProcessCandidateDiagnostic candidate = candidates[0];
            long[] windows = candidate == null ? new long[0] : (candidate.WindowHandles ?? new long[0]);
            if (windows.Length != 1 || ticket.GameWindowHandle != windows[0])
                return false;

            ConflictDiagnostic[] processes = (report.ConflictScan.Conflicts ?? new ConflictDiagnostic[0])
                .Where(item => item != null
                    && string.Equals(item.Code, "COMPATIBLE_EASY_PROCESS", StringComparison.Ordinal))
                .ToArray();
            ConflictDiagnostic[] modules = (report.ConflictScan.Conflicts ?? new ConflictDiagnostic[0])
                .Where(item => item != null
                    && string.Equals(item.Code, "COMPATIBLE_EASY_MODULE", StringComparison.Ordinal))
                .ToArray();
            if (processes.Length != 1 || modules.Length != 1) return false;
            ConflictDiagnostic process = processes[0];
            ConflictDiagnostic module = modules[0];
            return process != null && module != null
                && ticket.EasyLoaderProcessId == process.ProcessId.GetValueOrDefault()
                && ticket.EasyLoaderCreationFileTimeUtc == process.ProcessCreationFileTimeUtc.GetValueOrDefault()
                && San9Pk101TargetValidator.PathsEqual(ticket.EasyLoaderPath, process.Path)
                && ticket.EasyLoaderFileSize == process.FileSize.GetValueOrDefault()
                && string.Equals(ticket.EasyLoaderFileSha256, process.FileSha256, StringComparison.OrdinalIgnoreCase)
                && ticket.EasyModuleBaseAddress == module.ModuleBaseAddress.GetValueOrDefault()
                && ticket.EasyModuleImageSize == module.ModuleImageSize.GetValueOrDefault()
                && San9Pk101TargetValidator.PathsEqual(ticket.EasyModulePath, module.Path)
                && ticket.EasyModuleFileSize == module.FileSize.GetValueOrDefault()
                && string.Equals(ticket.EasyModuleFileSha256, module.FileSha256, StringComparison.OrdinalIgnoreCase)
                && module.ProcessId == gameProcessId;
        }

        private static bool IsUpperHexSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9')
                    || (character >= 'A' && character <= 'F')))
                    return false;
            }
            return true;
        }

        internal static bool TryValidateExactConflictFreeConnection(
            ReadOnlyProcessConnection connection,
            San9Pk101DiagnosticReport report,
            out string error)
        {
            if (!TryValidateExactConflictFreeReport(report, out error))
            {
                return false;
            }

            if (connection == null
                || !connection.IsConnected
                || connection.ProcessId != report.ProcessDiscovery.SelectedProcessId.Value
                || !report.ReadOnlyConnectionBindingId.HasValue
                || report.ReadOnlyConnectionBindingId.Value != connection.DiagnosticBindingId)
            {
                error = "The root trace requires the read-only connection created by the same diagnostic gate.";
                return false;
            }

            return true;
        }

        internal static bool HasBlockingIssue(IEnumerable<DiagnosticIssue> issues)
        {
            if (issues == null)
            {
                return true;
            }

            foreach (DiagnosticIssue issue in issues)
            {
                if (issue == null || issue.Severity == DiagnosticSeverity.Blocking)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
