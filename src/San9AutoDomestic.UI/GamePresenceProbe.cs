using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.UI
{
    public enum GamePresenceStatus
    {
        Offline = 0,
        Online = 1,
        ProbeFailed = 2
    }

    public sealed class GamePresenceSnapshot
    {
        private GamePresenceSnapshot(
            GamePresenceStatus status,
            int? processId,
            long? creationFileTimeUtc,
            string imagePath,
            string detail)
        {
            Status = status;
            ProcessId = processId;
            CreationFileTimeUtc = creationFileTimeUtc;
            ImagePath = imagePath ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public GamePresenceStatus Status { get; private set; }

        public int? ProcessId { get; private set; }

        public long? CreationFileTimeUtc { get; private set; }

        public string ImagePath { get; private set; }

        public string Detail { get; private set; }

        public static GamePresenceSnapshot Offline()
        {
            return new GamePresenceSnapshot(
                GamePresenceStatus.Offline,
                null,
                null,
                string.Empty,
                "未发现精确目标游戏；助手仍在运行，启动游戏后会自动检测。");
        }

        public static GamePresenceSnapshot Online(
            int processId,
            long creationFileTimeUtc,
            string imagePath)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }

            if (creationFileTimeUtc <= 0)
            {
                throw new ArgumentOutOfRangeException("creationFileTimeUtc");
            }

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("The image path is required.", "imagePath");
            }

            return new GamePresenceSnapshot(
                GamePresenceStatus.Online,
                processId,
                creationFileTimeUtc,
                imagePath,
                string.Empty);
        }

        public static GamePresenceSnapshot Failed(string detail)
        {
            return new GamePresenceSnapshot(
                GamePresenceStatus.ProbeFailed,
                null,
                null,
                string.Empty,
                string.IsNullOrWhiteSpace(detail)
                    ? "轻量在线检测失败；助手仍在运行，可重新检测。"
                    : detail);
        }

        public bool IsSameGenerationAs(GamePresenceSnapshot other)
        {
            return other != null
                && Status == GamePresenceStatus.Online
                && other.Status == GamePresenceStatus.Online
                && ProcessId == other.ProcessId
                && CreationFileTimeUtc == other.CreationFileTimeUtc
                && string.Equals(ImagePath, other.ImagePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public interface IGamePresenceProbe
    {
        GamePresenceSnapshot Probe();
    }

    internal sealed class ExactTargetGamePresenceProbe : IGamePresenceProbe
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int MaximumPathCharacters = 32768;

        public GamePresenceSnapshot Probe()
        {
            try
            {
                IntPtr window = NativeMethods.FindWindow(
                    San9Pk101Target.ExpectedWindowClass,
                    null);
                if (window == IntPtr.Zero)
                {
                    return GamePresenceSnapshot.Offline();
                }

                uint rawProcessId;
                NativeMethods.GetWindowThreadProcessId(window, out rawProcessId);
                if (rawProcessId == 0 || rawProcessId > int.MaxValue)
                {
                    return GamePresenceSnapshot.Failed(
                        "发现游戏窗口，但无法取得有效 PID；助手不会启动完整检测。");
                }

                using (SafeProcessHandle process = NativeMethods.OpenProcess(
                    ProcessQueryLimitedInformation,
                    false,
                    rawProcessId))
                {
                    if (process == null || process.IsInvalid)
                    {
                        return FailedWin32(
                            "发现游戏窗口，但无法以 QUERY_LIMITED_INFORMATION 查询目标进程");
                    }

                    int capacity = MaximumPathCharacters;
                    StringBuilder path = new StringBuilder(capacity);
                    if (!NativeMethods.QueryFullProcessImageName(
                        process,
                        0,
                        path,
                        ref capacity))
                    {
                        return FailedWin32("无法读取目标进程映像路径");
                    }

                    string exactPath = Path.GetFullPath(path.ToString());
                    string expectedPath = Path.GetFullPath(
                        San9Pk101Target.ExpectedExecutablePath);
                    if (!string.Equals(
                        exactPath,
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return GamePresenceSnapshot.Failed(
                            "检测到同类窗口，但进程路径不是锁定目标；已拒绝自动检测。");
                    }

                    NativeFileTime creation;
                    NativeFileTime exit;
                    NativeFileTime kernel;
                    NativeFileTime user;
                    if (!NativeMethods.GetProcessTimes(
                        process,
                        out creation,
                        out exit,
                        out kernel,
                        out user))
                    {
                        return FailedWin32("无法读取目标进程创建代");
                    }

                    long creationFileTimeUtc = creation.ToInt64();
                    if (creationFileTimeUtc <= 0)
                    {
                        return GamePresenceSnapshot.Failed(
                            "目标进程创建代无效；已拒绝自动检测。");
                    }

                    return GamePresenceSnapshot.Online(
                        (int)rawProcessId,
                        creationFileTimeUtc,
                        exactPath);
                }
            }
            catch (Exception exception)
            {
                return GamePresenceSnapshot.Failed(
                    "轻量在线检测异常；助手仍在运行：" + exception.Message);
            }
        }

        private static GamePresenceSnapshot FailedWin32(string operation)
        {
            int error = Marshal.GetLastWin32Error();
            string detail = error == 0
                ? operation + "；助手不会启动完整检测。"
                : string.Format(
                    "{0}（Win32 {1}: {2}）；助手不会启动完整检测。",
                    operation,
                    error,
                    new Win32Exception(error).Message);
            return GamePresenceSnapshot.Failed(detail);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime
        {
            public uint Low;
            public uint High;

            public long ToInt64()
            {
                return unchecked((long)(((ulong)High << 32) | Low));
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr FindWindow(string className, string windowName);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint GetWindowThreadProcessId(
                IntPtr window,
                out uint processId);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern SafeProcessHandle OpenProcess(
                uint desiredAccess,
                [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
                uint processId);

            [DllImport(
                "kernel32.dll",
                CharSet = CharSet.Unicode,
                SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool QueryFullProcessImageName(
                SafeProcessHandle process,
                int flags,
                StringBuilder imagePath,
                ref int size);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetProcessTimes(
                SafeProcessHandle process,
                out NativeFileTime creationTime,
                out NativeFileTime exitTime,
                out NativeFileTime kernelTime,
                out NativeFileTime userTime);
        }
    }
}
