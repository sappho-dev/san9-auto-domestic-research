using System;
using System.Runtime.InteropServices;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.UI
{
    internal sealed class NativeGameFocusHandoffResult
    {
        internal bool Succeeded { get; set; }

        internal IntPtr WindowHandle { get; set; }

        internal int ProcessId { get; set; }

        internal string Error { get; set; }
    }

    internal interface INativeGameFocusHandoff
    {
        NativeGameFocusHandoffResult TryHandoff(GamePresenceSnapshot expectedGeneration);
    }

    internal interface INativeGameWindowApi
    {
        IntPtr FindExactGameWindow();

        bool IsWindow(IntPtr window);

        uint GetWindowProcessId(IntPtr window, out uint processId);

        void RestoreWindow(IntPtr window);

        bool SetExactForegroundWindow(IntPtr window);

        IntPtr GetForegroundWindow();
    }

    internal sealed class NativeGameFocusHandoff : INativeGameFocusHandoff
    {
        private readonly IGamePresenceProbe presenceProbe;
        private readonly INativeGameWindowApi nativeApi;

        internal NativeGameFocusHandoff(IGamePresenceProbe presenceProbe)
            : this(presenceProbe, new NativeGameWindowApi())
        {
        }

        internal NativeGameFocusHandoff(
            IGamePresenceProbe presenceProbe,
            INativeGameWindowApi nativeApi)
        {
            if (presenceProbe == null)
            {
                throw new ArgumentNullException("presenceProbe");
            }
            if (nativeApi == null)
            {
                throw new ArgumentNullException("nativeApi");
            }

            this.presenceProbe = presenceProbe;
            this.nativeApi = nativeApi;
        }

        public NativeGameFocusHandoffResult TryHandoff(
            GamePresenceSnapshot expectedGeneration)
        {
            if (expectedGeneration == null
                || expectedGeneration.Status != GamePresenceStatus.Online
                || !expectedGeneration.ProcessId.HasValue
                || !expectedGeneration.CreationFileTimeUtc.HasValue)
            {
                return Failed("没有可用于焦点交接的精确游戏进程代。", IntPtr.Zero, 0);
            }

            GamePresenceSnapshot before = presenceProbe.Probe();
            if (!expectedGeneration.IsSameGenerationAs(before))
            {
                return Failed("焦点交接前游戏进程代已变化。", IntPtr.Zero, 0);
            }

            IntPtr window = nativeApi.FindExactGameWindow();
            uint processId;
            if (!ExactWindowIdentity(window, expectedGeneration.ProcessId.Value, out processId))
            {
                return Failed("找不到与本次检测同 PID 的精确游戏窗口。", window, processId);
            }

            nativeApi.RestoreWindow(window);
            if (!ExactWindowIdentity(window, expectedGeneration.ProcessId.Value, out processId))
            {
                return Failed("恢复窗口时游戏 HWND/PID 已变化。", window, processId);
            }
            if (!nativeApi.SetExactForegroundWindow(window)
                || nativeApi.GetForegroundWindow() != window)
            {
                return Failed("Windows 未允许把精确游戏窗口切到前台。", window, processId);
            }

            GamePresenceSnapshot after = presenceProbe.Probe();
            if (!expectedGeneration.IsSameGenerationAs(after)
                || !ExactWindowIdentity(window, expectedGeneration.ProcessId.Value, out processId))
            {
                return Failed("焦点交接后游戏 HWND/PID/创建代发生变化。", window, processId);
            }

            return new NativeGameFocusHandoffResult
            {
                Succeeded = true,
                WindowHandle = window,
                ProcessId = (int)processId,
                Error = string.Empty
            };
        }

        private bool ExactWindowIdentity(
            IntPtr window,
            int expectedProcessId,
            out uint processId)
        {
            processId = 0;
            return window != IntPtr.Zero
                && nativeApi.IsWindow(window)
                && nativeApi.GetWindowProcessId(window, out processId) != 0u
                && processId == (uint)expectedProcessId;
        }

        private static NativeGameFocusHandoffResult Failed(
            string error,
            IntPtr window,
            uint processId)
        {
            return new NativeGameFocusHandoffResult
            {
                Succeeded = false,
                WindowHandle = window,
                ProcessId = processId <= int.MaxValue ? (int)processId : 0,
                Error = error ?? "游戏窗口焦点交接失败。"
            };
        }

        private sealed class NativeGameWindowApi : INativeGameWindowApi
        {
            private const int RestoreWindowCommand = 9;

            public IntPtr FindExactGameWindow()
            {
                return NativeMethods.FindWindow(
                    San9Pk101Target.ExpectedWindowClass,
                    null);
            }

            public bool IsWindow(IntPtr window)
            {
                return NativeMethods.IsWindow(window);
            }

            public uint GetWindowProcessId(IntPtr window, out uint processId)
            {
                return NativeMethods.GetWindowThreadProcessId(window, out processId);
            }

            public void RestoreWindow(IntPtr window)
            {
                NativeMethods.ShowWindow(window, RestoreWindowCommand);
            }

            public bool SetExactForegroundWindow(IntPtr window)
            {
                return NativeMethods.SetForegroundWindow(window);
            }

            public IntPtr GetForegroundWindow()
            {
                return NativeMethods.GetForegroundWindow();
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr FindWindow(string className, string windowName);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindow(IntPtr window);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint GetWindowThreadProcessId(
                IntPtr window,
                out uint processId);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ShowWindow(IntPtr window, int command);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetForegroundWindow(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetForegroundWindow();
        }
    }
}
