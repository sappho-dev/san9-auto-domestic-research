using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public sealed class San9Pk101ProcessDiscovery
    {
        private sealed class WindowHit
        {
            internal int ProcessId;
            internal long WindowHandle;
        }

        public ProcessDiscoveryDiagnostic Discover()
        {
            ProcessDiscoveryDiagnostic result = new ProcessDiscoveryDiagnostic();
            List<DiagnosticIssue> issues = new List<DiagnosticIssue>();
            List<WindowHit> windowHits = new List<WindowHit>();
            bool windowEnumerationFailed = false;

            NativeMethods.EnumWindowsCallback callback = delegate(IntPtr windowHandle, IntPtr parameter)
            {
                StringBuilder className = new StringBuilder(256);
                int length = NativeMethods.GetClassName(windowHandle, className, className.Capacity);
                if (length <= 0 || !string.Equals(
                    className.ToString(),
                    San9Pk101Target.ExpectedWindowClass,
                    StringComparison.Ordinal))
                {
                    return true;
                }

                uint processId;
                NativeMethods.GetWindowThreadProcessId(windowHandle, out processId);
                if (processId != 0)
                {
                    windowHits.Add(new WindowHit
                    {
                        ProcessId = unchecked((int)processId),
                        WindowHandle = windowHandle.ToInt64()
                    });
                }

                return true;
            };

            try
            {
                if (!NativeMethods.EnumWindows(callback, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    windowEnumerationFailed = true;
                    issues.Add(new DiagnosticIssue(
                        "WINDOW_ENUMERATION_FAILED",
                        DiagnosticSeverity.Blocking,
                        string.Format("EnumWindows failed with Win32 error {0}.", error)));
                }
            }
            catch (Exception exception)
            {
                windowEnumerationFailed = true;
                issues.Add(new DiagnosticIssue(
                    "WINDOW_ENUMERATION_EXCEPTION",
                    DiagnosticSeverity.Blocking,
                    exception.Message));
            }

            HashSet<int> candidateIds = new HashSet<int>(windowHits.Select(hit => hit.ProcessId));
            try
            {
                Process[] namedProcesses = Process.GetProcessesByName(San9Pk101Target.ExpectedProcessName);
                foreach (Process process in namedProcesses)
                {
                    using (process)
                    {
                        candidateIds.Add(process.Id);
                    }
                }
            }
            catch (Exception exception)
            {
                issues.Add(new DiagnosticIssue(
                    "PROCESS_ENUMERATION_FAILED",
                    DiagnosticSeverity.Blocking,
                    exception.Message));
                windowEnumerationFailed = true;
            }

            List<ProcessCandidateDiagnostic> candidates = new List<ProcessCandidateDiagnostic>();
            foreach (int processId in candidateIds.OrderBy(value => value))
            {
                ProcessCandidateDiagnostic candidate = new ProcessCandidateDiagnostic();
                candidate.ProcessId = processId;
                candidate.WindowHandles = windowHits
                    .Where(hit => hit.ProcessId == processId)
                    .Select(hit => hit.WindowHandle)
                    .Distinct()
                    .OrderBy(handle => handle)
                    .ToArray();
                candidate.HasExpectedWindowClass = candidate.WindowHandles.Length > 0;

                try
                {
                    using (Process process = Process.GetProcessById(processId))
                    {
                        candidate.ProcessName = process.ProcessName;
                    }
                }
                catch (Exception exception)
                {
                    candidate.QueryError = exception.Message;
                }

                string imagePath;
                int nativeError;
                candidate.ImagePathQuerySucceeded = NativeMethods.TryGetProcessImagePath(
                    processId,
                    out imagePath,
                    out nativeError);
                candidate.ImagePath = imagePath;
                candidate.PathMatches = candidate.ImagePathQuerySucceeded
                    && San9Pk101TargetValidator.PathsEqual(
                        imagePath,
                        San9Pk101Target.ExpectedExecutablePath);
                if (!candidate.ImagePathQuerySucceeded)
                {
                    string queryMessage = string.Format(
                        "Could not query PID {0} image path (Win32 error {1}).",
                        processId,
                        nativeError);
                    candidate.QueryError = string.IsNullOrEmpty(candidate.QueryError)
                        ? queryMessage
                        : candidate.QueryError + " " + queryMessage;
                }

                candidates.Add(candidate);
            }

            result.Candidates = candidates.ToArray();
            ProcessCandidateDiagnostic[] exactWindowCandidates = candidates
                .Where(candidate => candidate.HasExpectedWindowClass && candidate.PathMatches)
                .ToArray();

            foreach (ProcessCandidateDiagnostic candidate in candidates.Where(
                item => item.HasExpectedWindowClass && !item.PathMatches))
            {
                issues.Add(new DiagnosticIssue(
                    "WINDOW_CLASS_PATH_MISMATCH",
                    DiagnosticSeverity.Blocking,
                    string.Format(
                        "PID {0} owns a '{1}' window but its image path is '{2}'.",
                        candidate.ProcessId,
                        San9Pk101Target.ExpectedWindowClass,
                        candidate.ImagePath ?? "<unavailable>")));
            }

            if (windowEnumerationFailed)
            {
                result.Status = ProcessDiscoveryStatus.Failed;
            }
            else if (exactWindowCandidates.Length == 1)
            {
                result.Status = ProcessDiscoveryStatus.Unique;
                result.SelectedProcessId = exactWindowCandidates[0].ProcessId;
                result.SelectedImagePath = exactWindowCandidates[0].ImagePath;
            }
            else if (exactWindowCandidates.Length > 1)
            {
                result.Status = ProcessDiscoveryStatus.Ambiguous;
                issues.Add(new DiagnosticIssue(
                    "MULTIPLE_TARGET_PROCESSES",
                    DiagnosticSeverity.Blocking,
                    "More than one exact-path San9PK process owns the expected window class."));
            }
            else if (candidates.Any(candidate => candidate.PathMatches))
            {
                result.Status = ProcessDiscoveryStatus.ExpectedProcessWithoutWindow;
                issues.Add(new DiagnosticIssue(
                    "TARGET_WINDOW_NOT_READY",
                    DiagnosticSeverity.Blocking,
                    "The exact San9PK process exists but has no KOEI_SAN9WINDOW top-level window."));
            }
            else
            {
                result.Status = ProcessDiscoveryStatus.NotFound;
                issues.Add(new DiagnosticIssue(
                    "TARGET_PROCESS_NOT_FOUND",
                    DiagnosticSeverity.Blocking,
                    "No exact-path San9PK process with a KOEI_SAN9WINDOW window was found."));
            }

            result.Issues = issues.ToArray();
            return result;
        }
    }
}
