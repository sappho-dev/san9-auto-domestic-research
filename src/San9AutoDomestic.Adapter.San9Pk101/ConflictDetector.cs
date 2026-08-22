using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public sealed class San9Pk101ConflictDetector
    {
        internal const string CompatibleEasyProcessPath = @"D:\三国志9\10101749\San9PKEasy.exe";
        internal const long CompatibleEasyProcessSize = 24576;
        internal const string CompatibleEasyProcessSha256 = "CDACA1477EDB5A3BD79BDA8540E19837FC3F9170965D972E8E48FB24CAE21C07";
        internal const string CompatibleEasyModulePath = @"D:\三国志9\10101749\Easy.dll";
        internal const long CompatibleEasyModuleFileSize = 32768;
        internal const uint CompatibleEasyModuleImageSize = 36864;
        internal const string CompatibleEasyModuleSha256 = "E8BA3A603F6B0E7AF8A246DA0FE5CBDBA86AD9B77C5A507DD86159EF6C74A3F0";

        private static readonly string[] KnownConflictProcessNames =
        {
            "San9PKEasy",
            "San9PKHard",
            "SanIXPKCheat"
        };

        private static readonly string[] KnownConflictModuleNames =
        {
            "Easy.dll",
            "SanIXSpy.dll",
            "San9Common.dll"
        };

        private static readonly string[] LocalProxyModuleNames =
        {
            "version.dll",
            "dinput.dll",
            "dinput8.dll",
            "winmm.dll",
            "dsound.dll"
        };

        public ConflictScanDiagnostic Scan(int? targetProcessId, string targetExecutablePath)
        {
            ConflictScanDiagnostic result = new ConflictScanDiagnostic();
            List<ConflictDiagnostic> conflicts = new List<ConflictDiagnostic>();
            List<DiagnosticIssue> issues = new List<DiagnosticIssue>();

            ScanKnownProcesses(conflicts, issues, result);

            if (targetProcessId.HasValue)
            {
                result.ModuleScanAttempted = true;
                ScanTargetModules(
                    targetProcessId.Value,
                    targetExecutablePath,
                    conflicts,
                    issues,
                    result);
            }

            result.Conflicts = conflicts
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProcessId)
                .ToArray();
            result.HasBlockingConflicts = result.Conflicts.Any(item => item.IsBlocking);
            result.Issues = issues.ToArray();
            return result;
        }

        internal static bool ContainsOnlyExactCompatibleEasy(ConflictScanDiagnostic scan)
        {
            if (scan == null || scan.Conflicts == null || scan.HasBlockingConflicts) return false;
            if (scan.Conflicts.Length != 2) return false;

            ConflictDiagnostic process = scan.Conflicts.SingleOrDefault(item =>
                item != null
                && !item.IsBlocking
                && string.Equals(item.Code, "COMPATIBLE_EASY_PROCESS", StringComparison.Ordinal)
                && string.Equals(item.Name, "San9PKEasy.exe", StringComparison.OrdinalIgnoreCase)
                && item.ProcessId.HasValue && item.ProcessId.Value > 0
                && item.ProcessCreationFileTimeUtc.HasValue && item.ProcessCreationFileTimeUtc.Value > 0
                && item.FileSize == CompatibleEasyProcessSize
                && string.Equals(item.FileSha256, CompatibleEasyProcessSha256, StringComparison.OrdinalIgnoreCase)
                && San9Pk101TargetValidator.PathsEqual(item.Path, CompatibleEasyProcessPath));
            ConflictDiagnostic module = scan.Conflicts.SingleOrDefault(item =>
                item != null
                && !item.IsBlocking
                && string.Equals(item.Code, "COMPATIBLE_EASY_MODULE", StringComparison.Ordinal)
                && string.Equals(item.Name, "Easy.dll", StringComparison.OrdinalIgnoreCase)
                && item.ProcessId.HasValue && item.ProcessId.Value > 0
                && item.ModuleBaseAddress.HasValue && item.ModuleBaseAddress.Value > 0
                && item.ModuleImageSize == CompatibleEasyModuleImageSize
                && item.FileSize == CompatibleEasyModuleFileSize
                && string.Equals(item.FileSha256, CompatibleEasyModuleSha256, StringComparison.OrdinalIgnoreCase)
                && San9Pk101TargetValidator.PathsEqual(item.Path, CompatibleEasyModulePath));
            return process != null && module != null;
        }

        private static void ScanKnownProcesses(
            List<ConflictDiagnostic> conflicts,
            List<DiagnosticIssue> issues,
            ConflictScanDiagnostic result)
        {
            try
            {
                foreach (string processName in KnownConflictProcessNames)
                {
                    Process[] processes = Process.GetProcessesByName(processName);
                    foreach (Process process in processes)
                    {
                        using (process)
                        {
                            string imagePath;
                            int nativeError;
                            NativeMethods.TryGetProcessImagePath(
                                process.Id,
                                out imagePath,
                                out nativeError);

                            string compatibleReason;
                            if (string.Equals(processName, "San9PKEasy", StringComparison.OrdinalIgnoreCase)
                                && IsExactCompatibleEasyFile(
                                    imagePath,
                                    CompatibleEasyProcessPath,
                                    CompatibleEasyProcessSize,
                                    CompatibleEasyProcessSha256,
                                    out compatibleReason))
                            {
                                long creationFileTimeUtc;
                                string creationError;
                                if (!TryGetExactProcessCreation(
                                    process.Id,
                                    imagePath,
                                    out creationFileTimeUtc,
                                    out creationError))
                                {
                                    conflicts.Add(new ConflictDiagnostic
                                    {
                                        Kind = ConflictKind.InspectionFailure,
                                        Code = "EASY_LOADER_IDENTITY_FAILED",
                                        Name = processName + ".exe",
                                        ProcessId = process.Id,
                                        Path = imagePath,
                                        Reason = creationError,
                                        IsBlocking = true
                                    });
                                    continue;
                                }

                                conflicts.Add(new ConflictDiagnostic
                                {
                                    Kind = ConflictKind.KnownProcess,
                                    Code = "COMPATIBLE_EASY_PROCESS",
                                    Name = processName + ".exe",
                                    ProcessId = process.Id,
                                    ProcessCreationFileTimeUtc = creationFileTimeUtc,
                                    Path = imagePath,
                                    FileSize = CompatibleEasyProcessSize,
                                    FileSha256 = CompatibleEasyProcessSha256,
                                    Reason = "Exact allowlisted San9PKEasy 1.1.0.5 process: " + compatibleReason,
                                    IsBlocking = false
                                });
                                continue;
                            }

                            conflicts.Add(new ConflictDiagnostic
                            {
                                Kind = ConflictKind.KnownProcess,
                                Code = "KNOWN_CONFLICT_PROCESS",
                                Name = processName + ".exe",
                                ProcessId = process.Id,
                                Path = imagePath,
                                Reason = "A known San9 memory modifier is running.",
                                IsBlocking = true
                            });
                        }
                    }
                }

                result.ProcessScanSucceeded = true;
            }
            catch (Exception exception)
            {
                result.ProcessScanSucceeded = false;
                conflicts.Add(new ConflictDiagnostic
                {
                    Kind = ConflictKind.InspectionFailure,
                    Code = "CONFLICT_PROCESS_SCAN_FAILED",
                    Name = "process scan",
                    Reason = exception.Message,
                    IsBlocking = true
                });
                issues.Add(new DiagnosticIssue(
                    "CONFLICT_PROCESS_SCAN_FAILED",
                    DiagnosticSeverity.Blocking,
                    exception.Message));
            }
        }

        private static void ScanTargetModules(
            int processId,
            string targetExecutablePath,
            List<ConflictDiagnostic> conflicts,
            List<DiagnosticIssue> issues,
            ConflictScanDiagnostic result)
        {
            SafeNativeHandle snapshot = null;
            int snapshotError = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                snapshot = NativeMethods.CreateToolhelp32Snapshot(
                    NativeMethods.SnapshotModules | NativeMethods.SnapshotModules32,
                    unchecked((uint)processId));
                if (snapshot != null && !snapshot.IsInvalid)
                {
                    break;
                }

                snapshotError = Marshal.GetLastWin32Error();
                if (snapshot != null)
                {
                    snapshot.Dispose();
                    snapshot = null;
                }

                if (snapshotError != NativeMethods.ErrorBadLength)
                {
                    break;
                }
            }

            if (snapshot == null || snapshot.IsInvalid)
            {
                AddModuleInspectionFailure(snapshotError, conflicts, issues, result);
                return;
            }

            using (snapshot)
            {
                ModuleEntry32 entry = new ModuleEntry32();
                entry.Size = unchecked((uint)Marshal.SizeOf(typeof(ModuleEntry32)));
                if (!NativeMethods.Module32First(snapshot, ref entry))
                {
                    int firstError = Marshal.GetLastWin32Error();
                    AddModuleInspectionFailure(firstError, conflicts, issues, result);
                    return;
                }

                string targetDirectory = null;
                try
                {
                    targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetExecutablePath));
                }
                catch
                {
                }

                do
                {
                    result.InspectedModuleCount++;
                    EvaluateModule(
                        processId,
                        entry.ModuleName,
                        entry.ExePath,
                        entry.BaseAddress,
                        entry.BaseSize,
                        targetDirectory,
                        conflicts);
                    entry.Size = unchecked((uint)Marshal.SizeOf(typeof(ModuleEntry32)));
                }
                while (NativeMethods.Module32Next(snapshot, ref entry));

                int nextError = Marshal.GetLastWin32Error();
                if (nextError != 0 && nextError != NativeMethods.ErrorNoMoreFiles)
                {
                    AddModuleInspectionFailure(nextError, conflicts, issues, result);
                    return;
                }

                result.ModuleScanSucceeded = true;
            }
        }

        private static void EvaluateModule(
            int processId,
            string moduleName,
            string modulePath,
            IntPtr moduleBaseAddress,
            uint moduleImageSize,
            string targetDirectory,
            List<ConflictDiagnostic> conflicts)
        {
            string effectiveName = string.IsNullOrEmpty(moduleName)
                ? Path.GetFileName(modulePath)
                : moduleName;

            if (KnownConflictModuleNames.Any(name => string.Equals(
                name,
                effectiveName,
                StringComparison.OrdinalIgnoreCase)))
            {
                string compatibleReason;
                if (string.Equals(effectiveName, "Easy.dll", StringComparison.OrdinalIgnoreCase)
                    && moduleImageSize == CompatibleEasyModuleImageSize
                    && IsExactCompatibleEasyFile(
                        modulePath,
                        CompatibleEasyModulePath,
                        CompatibleEasyModuleFileSize,
                        CompatibleEasyModuleSha256,
                        out compatibleReason))
                {
                    conflicts.Add(new ConflictDiagnostic
                    {
                        Kind = ConflictKind.KnownModule,
                        Code = "COMPATIBLE_EASY_MODULE",
                        Name = effectiveName,
                        ProcessId = processId,
                        Path = modulePath,
                        FileSize = CompatibleEasyModuleFileSize,
                        FileSha256 = CompatibleEasyModuleSha256,
                        ModuleBaseAddress = unchecked((uint)moduleBaseAddress.ToInt64()),
                        ModuleImageSize = moduleImageSize,
                        Reason = "Exact allowlisted Easy.dll image: " + compatibleReason,
                        IsBlocking = false
                    });
                    return;
                }

                conflicts.Add(new ConflictDiagnostic
                {
                    Kind = ConflictKind.KnownModule,
                    Code = "KNOWN_CONFLICT_MODULE",
                    Name = effectiveName,
                    ProcessId = processId,
                    Path = modulePath,
                    Reason = "A known San9 patch module is loaded in the game process.",
                    IsBlocking = true
                });
                return;
            }

            if (LocalProxyModuleNames.Any(name => string.Equals(
                    name,
                    effectiveName,
                    StringComparison.OrdinalIgnoreCase))
                && IsInDirectory(modulePath, targetDirectory))
            {
                conflicts.Add(new ConflictDiagnostic
                {
                    Kind = ConflictKind.LocalProxyModule,
                    Code = "LOCAL_PROXY_MODULE",
                    Name = effectiveName,
                    ProcessId = processId,
                    Path = modulePath,
                    Reason = "A proxy-style system DLL is loaded from the game directory.",
                    IsBlocking = true
                });
            }
        }

        internal static bool IsExactCompatibleEasyFile(
            string actualPath,
            string expectedPath,
            long expectedSize,
            string expectedSha256,
            out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(actualPath)
                || !San9Pk101TargetValidator.PathsEqual(actualPath, expectedPath))
            {
                reason = "path mismatch or unavailable";
                return false;
            }

            try
            {
                FileInfo file = new FileInfo(actualPath);
                if (!file.Exists || file.Length != expectedSize)
                {
                    reason = "file missing or size mismatch";
                    return false;
                }

                string hash;
                using (FileStream stream = new FileStream(
                    actualPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (SHA256 algorithm = SHA256.Create())
                {
                    hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
                }
                if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "SHA-256 mismatch";
                    return false;
                }

                reason = "path, size and SHA-256 match";
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        internal static bool TryGetExactProcessCreation(
            int processId,
            string expectedPath,
            out long creationFileTimeUtc,
            out string error)
        {
            creationFileTimeUtc = 0;
            error = null;
            using (SafeNativeHandle handle = NativeMethods.OpenProcess(
                ProcessAccessFlags.QueryLimitedInformation,
                false,
                processId))
            {
                if (handle == null || handle.IsInvalid)
                {
                    error = string.Format(
                        "The Easy loader process could not be opened (Win32 error {0}).",
                        Marshal.GetLastWin32Error());
                    return false;
                }

                string actualPath;
                int pathError;
                if (!NativeMethods.TryGetProcessImagePath(handle, out actualPath, out pathError)
                    || !San9Pk101TargetValidator.PathsEqual(actualPath, expectedPath))
                {
                    error = string.Format(
                        "The Easy loader path/generation probe failed (Win32 error {0}).",
                        pathError);
                    return false;
                }

                NativeFileTime creation;
                NativeFileTime exit;
                NativeFileTime kernel;
                NativeFileTime user;
                if (!NativeMethods.GetProcessTimes(handle, out creation, out exit, out kernel, out user)
                    || creation.ToInt64() <= 0)
                {
                    error = string.Format(
                        "The Easy loader creation time could not be captured (Win32 error {0}).",
                        Marshal.GetLastWin32Error());
                    return false;
                }

                creationFileTimeUtc = creation.ToInt64();
                return true;
            }
        }

        private static bool IsInDirectory(string filePath, string directoryPath)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(directoryPath))
            {
                return false;
            }

            try
            {
                string fileDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
                return San9Pk101TargetValidator.PathsEqual(fileDirectory, directoryPath);
            }
            catch
            {
                return false;
            }
        }

        private static void AddModuleInspectionFailure(
            int nativeError,
            List<ConflictDiagnostic> conflicts,
            List<DiagnosticIssue> issues,
            ConflictScanDiagnostic result)
        {
            result.ModuleScanSucceeded = false;
            string message = string.Format(
                "The game module list could not be inspected (Win32 error {0}).",
                nativeError);
            conflicts.Add(new ConflictDiagnostic
            {
                Kind = ConflictKind.InspectionFailure,
                Code = "CONFLICT_MODULE_SCAN_FAILED",
                Name = "module scan",
                Reason = message,
                IsBlocking = true
            });
            issues.Add(new DiagnosticIssue(
                "CONFLICT_MODULE_SCAN_FAILED",
                DiagnosticSeverity.Blocking,
                message));
        }
    }
}
