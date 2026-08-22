using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    [Flags]
    internal enum ProcessAccessFlags : uint
    {
        VmRead = 0x0010,
        QueryLimitedInformation = 0x1000
    }

    internal sealed class SafeNativeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeNativeHandle()
            : base(true)
        {
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ModuleEntry32
    {
        public uint Size;
        public uint ModuleId;
        public uint ProcessId;
        public uint GlobalUsageCount;
        public uint ProcessUsageCount;
        public IntPtr BaseAddress;
        public uint BaseSize;
        public IntPtr ModuleHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ModuleName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        internal long ToInt64()
        {
            return unchecked(((long)HighDateTime << 32) | LowDateTime);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation32
    {
        public uint BaseAddress;
        public uint AllocationBase;
        public uint AllocationProtect;
        public uint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    internal static class NativeMethods
    {
        internal const uint SnapshotModules = 0x00000008;
        internal const uint SnapshotModules32 = 0x00000010;
        internal const int ErrorBadLength = 24;
        internal const int ErrorNoMoreFiles = 18;

        internal delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maximumCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeNativeHandle OpenProcess(
            ProcessAccessFlags desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(
            SafeNativeHandle process,
            uint flags,
            StringBuilder imagePath,
            ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadProcessMemory(
            SafeNativeHandle process,
            IntPtr baseAddress,
            [Out] byte[] buffer,
            UIntPtr size,
            out UIntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern UIntPtr VirtualQueryEx(
            SafeNativeHandle process,
            IntPtr address,
            out MemoryBasicInformation32 buffer,
            UIntPtr length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(
            SafeNativeHandle process,
            out NativeFileTime creationTime,
            out NativeFileTime exitTime,
            out NativeFileTime kernelTime,
            out NativeFileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeNativeHandle CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Module32First(SafeNativeHandle snapshot, ref ModuleEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Module32Next(SafeNativeHandle snapshot, ref ModuleEntry32 entry);

        internal static bool TryGetProcessImagePath(int processId, out string path, out int nativeError)
        {
            path = null;
            nativeError = 0;

            using (SafeNativeHandle handle = OpenProcess(
                ProcessAccessFlags.QueryLimitedInformation,
                false,
                processId))
            {
                if (handle == null || handle.IsInvalid)
                {
                    nativeError = Marshal.GetLastWin32Error();
                    return false;
                }

                return TryGetProcessImagePath(handle, out path, out nativeError);
            }
        }

        internal static bool TryGetProcessImagePath(
            SafeNativeHandle process,
            out string path,
            out int nativeError)
        {
            path = null;
            nativeError = 0;
            StringBuilder buffer = new StringBuilder(32768);
            uint length = (uint)buffer.Capacity;

            if (!QueryFullProcessImageName(process, 0, buffer, ref length))
            {
                nativeError = Marshal.GetLastWin32Error();
                return false;
            }

            path = buffer.ToString(0, (int)length);
            return true;
        }
    }
}
