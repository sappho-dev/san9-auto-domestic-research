using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal sealed class MemoryRegionSnapshot
    {
        internal uint BaseAddress;
        internal uint RegionSize;
        internal uint State;
        internal uint Protect;
    }

    public sealed class ReadOnlyProcessConnection : IDisposable, IReadOnlyProcessMemory
    {
        private const int MaximumSingleRead = 1024 * 1024;
        private SafeNativeHandle handle;
        private bool disposed;

        private ReadOnlyProcessConnection(
            SafeNativeHandle processHandle,
            int processId,
            string imagePath,
            ProcessIdentitySnapshot initialIdentity)
        {
            handle = processHandle;
            ProcessId = processId;
            ImagePath = imagePath;
            InitialIdentity = initialIdentity;
            DiagnosticBindingId = Guid.NewGuid();
        }

        public int ProcessId { get; private set; }
        public string ImagePath { get; private set; }

        ProcessIdentitySnapshot IReadOnlyProcessMemory.InitialIdentity
        {
            get { return InitialIdentity; }
        }

        internal ProcessIdentitySnapshot InitialIdentity { get; private set; }
        internal Guid DiagnosticBindingId { get; private set; }

        public bool IsConnected
        {
            get { return !disposed && handle != null && !handle.IsInvalid && !handle.IsClosed; }
        }

        public byte[] ReadBytes(long address, int count)
        {
            ThrowIfDisposed();
            IntPtr nativeAddress = ValidateReadRangeAndCreatePointer(address, count);

            byte[] buffer = new byte[count];
            UIntPtr bytesRead;
            bool succeeded = NativeMethods.ReadProcessMemory(
                handle,
                nativeAddress,
                buffer,
                new UIntPtr(unchecked((uint)count)),
                out bytesRead);

            ulong actual = bytesRead.ToUInt64();
            if (!succeeded || actual != unchecked((ulong)count))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                    error,
                    string.Format(
                        "ReadProcessMemory at 0x{0:X8} requested {1} bytes and returned {2} bytes.",
                        address,
                        count,
                        actual));
            }

            return buffer;
        }

        internal static IntPtr ValidateReadRangeAndCreatePointer(long address, int count)
        {
            if (address <= 0 || address > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    "address",
                    "The x86 address must be in the range 1..0xFFFFFFFF.");
            }

            if (count <= 0 || count > MaximumSingleRead)
            {
                throw new ArgumentOutOfRangeException(
                    "count",
                    string.Format("A single read must be in the range 1..{0} bytes.", MaximumSingleRead));
            }

            ulong endExclusive = unchecked((ulong)address) + unchecked((uint)count);
            if (endExclusive > (ulong)uint.MaxValue + 1UL)
            {
                throw new ArgumentOutOfRangeException(
                    "count",
                    "The read range crosses the end of the 32-bit address space.");
            }

            return IntPtr.Size == 4
                ? new IntPtr(unchecked((int)(uint)address))
                : new IntPtr(unchecked((long)(uint)address));
        }

        bool IReadOnlyProcessMemory.TryCaptureIdentity(
            out ProcessIdentitySnapshot identity,
            out string error)
        {
            return TryCaptureIdentity(out identity, out error);
        }

        internal bool TryCaptureIdentity(out ProcessIdentitySnapshot identity, out string error)
        {
            ThrowIfDisposed();
            return ProcessIdentitySnapshot.TryCapture(
                handle,
                ProcessId,
                ImagePath,
                out identity,
                out error);
        }

        internal bool TryQueryMemory(uint address, out MemoryRegionSnapshot region, out string error)
        {
            ThrowIfDisposed();
            region = null;
            error = null;
            MemoryBasicInformation32 native;
            UIntPtr expected = new UIntPtr(unchecked((uint)Marshal.SizeOf(typeof(MemoryBasicInformation32))));
            UIntPtr actual = NativeMethods.VirtualQueryEx(
                handle,
                ValidateReadRangeAndCreatePointer(address, 1),
                out native,
                expected);
            if (actual.ToUInt64() != expected.ToUInt64())
            {
                error = string.Format(
                    "VirtualQueryEx at 0x{0:X8} returned {1} bytes (Win32 error {2}).",
                    address,
                    actual.ToUInt64(),
                    Marshal.GetLastWin32Error());
                return false;
            }

            region = new MemoryRegionSnapshot
            {
                BaseAddress = native.BaseAddress,
                RegionSize = native.RegionSize,
                State = native.State,
                Protect = native.Protect
            };
            return true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (handle != null)
            {
                handle.Dispose();
                handle = null;
            }
        }

        internal static ReadOnlyProcessConnection TryOpen(
            int processId,
            string expectedPath,
            out ReadOnlyConnectionDiagnostic diagnostic)
        {
            diagnostic = new ReadOnlyConnectionDiagnostic();
            diagnostic.Attempted = true;
            diagnostic.ProcessId = processId;

            SafeNativeHandle processHandle = NativeMethods.OpenProcess(
                ProcessAccessFlags.QueryLimitedInformation | ProcessAccessFlags.VmRead,
                false,
                processId);
            if (processHandle == null || processHandle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                if (processHandle != null)
                {
                    processHandle.Dispose();
                }

                diagnostic.NativeError = error;
                diagnostic.Error = new Win32Exception(error).Message;
                return null;
            }

            string actualPath;
            int queryError;
            if (!NativeMethods.TryGetProcessImagePath(processHandle, out actualPath, out queryError))
            {
                processHandle.Dispose();
                diagnostic.NativeError = queryError;
                diagnostic.Error = "The process image path could not be revalidated after opening the handle.";
                return null;
            }

            diagnostic.ImagePath = actualPath;
            if (!San9Pk101TargetValidator.PathsEqual(actualPath, expectedPath))
            {
                processHandle.Dispose();
                diagnostic.Error = string.Format(
                    "PID reuse/path race detected. Expected '{0}', got '{1}'.",
                    expectedPath,
                    actualPath);
                return null;
            }

            ProcessIdentitySnapshot identity;
            string identityError;
            if (!ProcessIdentitySnapshot.TryCapture(
                processHandle,
                processId,
                actualPath,
                out identity,
                out identityError))
            {
                processHandle.Dispose();
                diagnostic.Error = identityError;
                return null;
            }

            diagnostic.Connected = true;
            diagnostic.ProcessCreationFileTimeUtc = identity.CreationFileTimeUtc;
            diagnostic.MainModuleBaseAddress = identity.MainModuleBaseAddress;
            diagnostic.MainModuleSize = identity.MainModuleSize;
            return new ReadOnlyProcessConnection(processHandle, processId, actualPath, identity);
        }

        private void ThrowIfDisposed()
        {
            if (disposed || handle == null || handle.IsInvalid || handle.IsClosed)
            {
                throw new ObjectDisposedException("ReadOnlyProcessConnection");
            }
        }
    }
}
