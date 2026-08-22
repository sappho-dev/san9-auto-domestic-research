using System;
using System.Runtime.InteropServices;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal interface IReadOnlyProcessMemory
    {
        int ProcessId { get; }
        string ImagePath { get; }
        ProcessIdentitySnapshot InitialIdentity { get; }
        byte[] ReadBytes(long address, int count);
        bool TryCaptureIdentity(out ProcessIdentitySnapshot identity, out string error);
    }

    internal sealed class ProcessIdentitySnapshot
    {
        internal int ProcessId;
        internal string ImagePath;
        internal long CreationFileTimeUtc;
        internal uint MainModuleBaseAddress;
        internal uint MainModuleSize;

        internal bool SameGenerationAndImage(ProcessIdentitySnapshot other)
        {
            return other != null
                && ProcessId == other.ProcessId
                && CreationFileTimeUtc == other.CreationFileTimeUtc
                && MainModuleBaseAddress == other.MainModuleBaseAddress
                && MainModuleSize == other.MainModuleSize
                && San9Pk101TargetValidator.PathsEqual(ImagePath, other.ImagePath);
        }

        internal static bool TryCapture(
            SafeNativeHandle processHandle,
            int processId,
            string expectedImagePath,
            out ProcessIdentitySnapshot identity,
            out string error)
        {
            identity = null;
            error = null;

            NativeFileTime creationBefore;
            NativeFileTime exitBefore;
            NativeFileTime kernelBefore;
            NativeFileTime userBefore;
            if (!NativeMethods.GetProcessTimes(
                processHandle,
                out creationBefore,
                out exitBefore,
                out kernelBefore,
                out userBefore))
            {
                error = string.Format(
                    "GetProcessTimes failed with Win32 error {0}.",
                    Marshal.GetLastWin32Error());
                return false;
            }

            string imagePath;
            int pathError;
            if (!NativeMethods.TryGetProcessImagePath(processHandle, out imagePath, out pathError)
                || !San9Pk101TargetValidator.PathsEqual(imagePath, expectedImagePath))
            {
                error = string.Format(
                    "Process image revalidation failed (Win32 error {0}); path='{1}'.",
                    pathError,
                    imagePath ?? "<unavailable>");
                return false;
            }

            uint moduleBase;
            uint moduleSize;
            string modulePath;
            int moduleError;
            if (!TryGetExactMainModule(
                processId,
                imagePath,
                out moduleBase,
                out moduleSize,
                out modulePath,
                out moduleError))
            {
                error = string.Format(
                    "The exact main module could not be revalidated (Win32 error {0}).",
                    moduleError);
                return false;
            }

            NativeFileTime creationAfter;
            NativeFileTime exitAfter;
            NativeFileTime kernelAfter;
            NativeFileTime userAfter;
            if (!NativeMethods.GetProcessTimes(
                processHandle,
                out creationAfter,
                out exitAfter,
                out kernelAfter,
                out userAfter))
            {
                error = string.Format(
                    "GetProcessTimes post-check failed with Win32 error {0}.",
                    Marshal.GetLastWin32Error());
                return false;
            }

            long creationValue = creationBefore.ToInt64();
            if (creationValue == 0 || creationValue != creationAfter.ToInt64())
            {
                error = "The process creation time changed while its module identity was inspected.";
                return false;
            }

            identity = new ProcessIdentitySnapshot
            {
                ProcessId = processId,
                ImagePath = modulePath,
                CreationFileTimeUtc = creationValue,
                MainModuleBaseAddress = moduleBase,
                MainModuleSize = moduleSize
            };
            return true;
        }

        private static bool TryGetExactMainModule(
            int processId,
            string expectedImagePath,
            out uint baseAddress,
            out uint moduleSize,
            out string modulePath,
            out int nativeError)
        {
            baseAddress = 0;
            moduleSize = 0;
            modulePath = null;
            nativeError = 0;
            SafeNativeHandle snapshot = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                snapshot = NativeMethods.CreateToolhelp32Snapshot(
                    NativeMethods.SnapshotModules | NativeMethods.SnapshotModules32,
                    unchecked((uint)processId));
                if (snapshot != null && !snapshot.IsInvalid)
                {
                    break;
                }

                nativeError = Marshal.GetLastWin32Error();
                if (snapshot != null)
                {
                    snapshot.Dispose();
                    snapshot = null;
                }

                if (nativeError != NativeMethods.ErrorBadLength)
                {
                    break;
                }
            }

            if (snapshot == null || snapshot.IsInvalid)
            {
                return false;
            }

            using (snapshot)
            {
                ModuleEntry32 entry = new ModuleEntry32();
                entry.Size = unchecked((uint)Marshal.SizeOf(typeof(ModuleEntry32)));
                if (!NativeMethods.Module32First(snapshot, ref entry))
                {
                    nativeError = Marshal.GetLastWin32Error();
                    return false;
                }

                do
                {
                    if (San9Pk101TargetValidator.PathsEqual(entry.ExePath, expectedImagePath))
                    {
                        long candidateBase = entry.BaseAddress.ToInt64();
                        ulong end = candidateBase < 0
                            ? ulong.MaxValue
                            : unchecked((ulong)candidateBase) + entry.BaseSize;
                        if (candidateBase <= 0
                            || unchecked((ulong)candidateBase) > uint.MaxValue
                            || end > (ulong)uint.MaxValue + 1UL
                            || entry.BaseSize == 0)
                        {
                            nativeError = 87;
                            return false;
                        }

                        baseAddress = unchecked((uint)candidateBase);
                        moduleSize = entry.BaseSize;
                        modulePath = entry.ExePath;
                        return true;
                    }

                    entry.Size = unchecked((uint)Marshal.SizeOf(typeof(ModuleEntry32)));
                }
                while (NativeMethods.Module32Next(snapshot, ref entry));

                nativeError = Marshal.GetLastWin32Error();
                if (nativeError == NativeMethods.ErrorNoMoreFiles)
                {
                    nativeError = 0;
                }

                return false;
            }
        }
    }
}
