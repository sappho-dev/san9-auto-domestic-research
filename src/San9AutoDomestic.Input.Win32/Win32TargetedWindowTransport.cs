using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace San9AutoDomestic.Input.Win32
{
    internal interface ITargetedWindowTransport
    {
        bool TryPostMouseMove(
            WindowBinding expected,
            int clientX,
            int clientY,
            out WindowEnvironment environment,
            out bool attempted,
            out string error);

        bool TryPostLeftClick(
            WindowBinding expected,
            int clientX,
            int clientY,
            out WindowEnvironment environment,
            out int attemptedMessageCount,
            out string error);

        bool TrySendInputLeftClick(
            WindowBinding expected,
            int clientX,
            int clientY,
            out WindowEnvironment environment,
            out int attemptedInputCount,
            out string error);

        bool TrySendInputStagedLeftClick(
            WindowBinding expected,
            int clientX,
            int clientY,
            out WindowEnvironment environment,
            out int attemptedInputCount,
            out string error);

        bool TryCapture(WindowBinding expected, out WindowEnvironment environment, out string error);
    }

    internal interface IProgramInputDispatchTraceSource
    {
        ProgramInputDispatchReport LastProgramDispatch { get; }
    }

    internal sealed class WindowBinding
    {
        internal const string ExpectedExecutablePath = @"D:\三国志9\10101749\San9PK.exe";
        internal int ProcessId;
        internal long ProcessCreationFileTimeUtc;
        internal long MainWindowHandle;

        internal static WindowBinding FromRequest(TargetedHoverRequest request)
        {
            return new WindowBinding
            {
                ProcessId = request.ProcessId,
                ProcessCreationFileTimeUtc = request.ProcessCreationFileTimeUtc,
                MainWindowHandle = request.MainWindowHandle
            };
        }

        internal static WindowBinding FromRequest(FacilitiesCitySwitchRequest request)
        {
            return new WindowBinding
            {
                ProcessId = request.ProcessId,
                ProcessCreationFileTimeUtc = request.ProcessCreationFileTimeUtc,
                MainWindowHandle = request.MainWindowHandle
            };
        }

        internal static WindowBinding FromRequest(FacilitiesRowSelectionRequest request)
        {
            return new WindowBinding
            {
                ProcessId = request.ProcessId,
                ProcessCreationFileTimeUtc = request.ProcessCreationFileTimeUtc,
                MainWindowHandle = request.MainWindowHandle
            };
        }
    }

    internal sealed class WindowEnvironment
    {
        internal int TargetProcessId;
        internal long TargetProcessCreationFileTimeUtc;
        internal long TargetWindowHandle;
        internal string TargetPath;
        internal long ForegroundWindowHandle;
        internal int ForegroundProcessId;
        internal int ClientWidth;
        internal int ClientHeight;

        internal bool TargetMatches(WindowBinding expected)
        {
            return expected != null
                && TargetProcessId == expected.ProcessId
                && TargetProcessCreationFileTimeUtc == expected.ProcessCreationFileTimeUtc
                && TargetWindowHandle == expected.MainWindowHandle
                && ClientWidth == 1024
                && ClientHeight == 768
                && string.Equals(TargetPath, WindowBinding.ExpectedExecutablePath, StringComparison.OrdinalIgnoreCase);
        }


        internal bool TargetIsForeground(WindowBinding expected)
        {
            return expected != null
                && ForegroundWindowHandle == expected.MainWindowHandle
                && ForegroundProcessId == expected.ProcessId;
        }
    }

    internal sealed class Win32TargetedWindowTransport : ITargetedWindowTransport, IProgramInputDispatchTraceSource
    {
        private const uint QueryLimitedInformation = 0x1000;
        private const uint WmMouseMove = 0x0200;
        private const uint WmLeftButtonDown = 0x0201;
        private const uint WmLeftButtonUp = 0x0202;
        private static readonly UIntPtr MouseKeyLeftButton = new UIntPtr(1);
        private const uint InputMouse = 0;
        private const uint MouseMove = 0x0001;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint MouseVirtualDesk = 0x4000;
        private const uint MouseAbsolute = 0x8000;

        public ProgramInputDispatchReport LastProgramDispatch { get; private set; }

        public bool TryPostMouseMove(
            WindowBinding expected,
            int clientX,
            int clientY,
            out WindowEnvironment environment,
            out bool attempted,
            out string error)
        {
            attempted = false;
            if (!TryCapture(expected, out environment, out error)) return false;
            if (!environment.TargetMatches(expected))
            {
                error = "The exact PID, generation, HWND, or executable path changed before PostMessage.";
                return false;
            }

            int packed = (clientY << 16) | (clientX & 0xFFFF);
            attempted = true;
            if (!NativeMethods.PostMessage(new IntPtr(expected.MainWindowHandle), WmMouseMove, UIntPtr.Zero, new IntPtr(packed)))
            {
                error = "PostMessage(WM_MOUSEMOVE) failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryPostLeftClick(
            WindowBinding expected,
            int clientX,
            int clientY,
            out WindowEnvironment environment,
            out int attemptedMessageCount,
            out string error)
        {
            attemptedMessageCount = 0;
            if (!TryCapture(expected, out environment, out error)) return false;
            if (!environment.TargetMatches(expected))
            {
                error = "The exact PID, generation, HWND, executable path, or 1024x768 client size changed before the click.";
                return false;
            }
            if (!environment.TargetIsForeground(expected))
            {
                error = "The exact game window is not foreground at the click boundary.";
                return false;
            }

            int packed = (clientY << 16) | (clientX & 0xFFFF);
            if (!TryPost(expected, WmMouseMove, UIntPtr.Zero, packed, ref attemptedMessageCount, out error)) return false;
            if (!TryPost(expected, WmLeftButtonDown, MouseKeyLeftButton, packed, ref attemptedMessageCount, out error)) return false;
            if (!TryPost(expected, WmLeftButtonUp, UIntPtr.Zero, packed, ref attemptedMessageCount, out error)) return false;
            return true;
        }

        public bool TrySendInputLeftClick(
            WindowBinding expected,
            int clientX,
            int clientY,
            out WindowEnvironment environment,
            out int attemptedInputCount,
            out string error)
        {
            attemptedInputCount = 0;
            if (!TryCapture(expected, out environment, out error)) return false;
            if (!environment.TargetMatches(expected))
            {
                error = "The exact PID, generation, HWND, executable path, or 1024x768 client size changed before SendInput.";
                return false;
            }
            if (!environment.TargetIsForeground(expected))
            {
                error = "The exact game window is not foreground at the SendInput boundary.";
                return false;
            }
            if (!ModifiersAndButtonsAreReleased())
            {
                error = "A keyboard modifier or mouse button is currently held.";
                return false;
            }

            NativeMethods.Point point = new NativeMethods.Point { X = clientX, Y = clientY };
            if (!NativeMethods.ClientToScreen(new IntPtr(expected.MainWindowHandle), ref point))
            {
                error = "ClientToScreen failed.";
                return false;
            }
            int left = NativeMethods.GetSystemMetrics(76);
            int top = NativeMethods.GetSystemMetrics(77);
            int width = NativeMethods.GetSystemMetrics(78);
            int height = NativeMethods.GetSystemMetrics(79);
            if (width <= 1 || height <= 1
                || point.X < left || point.X >= left + width
                || point.Y < top || point.Y >= top + height)
            {
                error = "The target point is outside the virtual desktop.";
                return false;
            }

            int absoluteX = checked((int)Math.Round((point.X - left) * 65535.0 / (width - 1)));
            int absoluteY = checked((int)Math.Round((point.Y - top) * 65535.0 / (height - 1)));
            NativeMethods.Input[] inputs = new[]
            {
                NativeMethods.Mouse(absoluteX, absoluteY, MouseMove | MouseAbsolute | MouseVirtualDesk),
                NativeMethods.Mouse(0, 0, MouseLeftDown),
                NativeMethods.Mouse(0, 0, MouseLeftUp)
            };
            attemptedInputCount = inputs.Length;
            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeMethods.Input)));
            if (sent != inputs.Length)
            {
                error = "SendInput inserted " + sent + " of " + inputs.Length + " events; Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public bool TrySendInputStagedLeftClick(
            WindowBinding expected,
            int clientX,
            int clientY,
            out WindowEnvironment environment,
            out int attemptedInputCount,
            out string error)
        {
            attemptedInputCount = 0;
            error = string.Empty;
            environment = new WindowEnvironment();
            ProgramInputDispatchReport report = new ProgramInputDispatchReport
            {
                StartedUtc = DateTimeOffset.UtcNow,
                StopwatchFrequency = Stopwatch.Frequency,
                StartTimestamp = Stopwatch.GetTimestamp(),
                ProcessId = expected == null ? 0 : expected.ProcessId,
                ProcessCreationFileTimeUtc = expected == null ? 0 : expected.ProcessCreationFileTimeUtc,
                MainWindowHandle = expected == null ? 0 : expected.MainWindowHandle,
                ClientX = clientX,
                ClientY = clientY
            };
            LastProgramDispatch = report;
            try
            {
                if (!TryCapture(expected, out environment, out error)) return false;
                report.ForegroundWindowHandleAtStart = environment.ForegroundWindowHandle;
                report.ForegroundProcessIdAtStart = environment.ForegroundProcessId;
                if (!environment.TargetMatches(expected) || !environment.TargetIsForeground(expected))
                {
                    error = "The exact game binding or foreground changed before staged SendInput.";
                    return false;
                }
                if (!ModifiersAndButtonsAreReleased())
                {
                    error = "A keyboard modifier or mouse button is currently held.";
                    return false;
                }

                NativeMethods.Point point = new NativeMethods.Point { X = clientX, Y = clientY };
                if (!NativeMethods.ClientToScreen(new IntPtr(expected.MainWindowHandle), ref point))
                {
                    error = "ClientToScreen failed.";
                    return false;
                }
                report.ScreenX = point.X;
                report.ScreenY = point.Y;
                int left = NativeMethods.GetSystemMetrics(76);
                int top = NativeMethods.GetSystemMetrics(77);
                int width = NativeMethods.GetSystemMetrics(78);
                int height = NativeMethods.GetSystemMetrics(79);
                if (width <= 1 || height <= 1
                    || point.X < left || point.X >= left + width
                    || point.Y < top || point.Y >= top + height)
                {
                    error = "The target point is outside the virtual desktop.";
                    return false;
                }
                int absoluteX = checked((int)Math.Round((point.X - left) * 65535.0 / (width - 1)));
                int absoluteY = checked((int)Math.Round((point.Y - top) * 65535.0 / (height - 1)));

                NativeMethods.Input[] move = { NativeMethods.Mouse(absoluteX, absoluteY, MouseMove | MouseAbsolute | MouseVirtualDesk) };
                attemptedInputCount = 1;
                report.AttemptedInputCount = attemptedInputCount;
                if (NativeMethods.SendInput(1, move, Marshal.SizeOf(typeof(NativeMethods.Input))) != 1)
                {
                    error = "The staged SendInput move was not inserted.";
                    return false;
                }
                report.MoveInserted = true;
                report.MoveTimestamp = Stopwatch.GetTimestamp();
                Thread.Sleep(100);

                WindowEnvironment atDown;
                if (!TryCapture(expected, out atDown, out error)
                    || !atDown.TargetMatches(expected)
                    || !atDown.TargetIsForeground(expected)
                    || !ModifiersAndButtonsAreReleased())
                {
                    if (string.IsNullOrEmpty(error)) error = "The exact foreground or clean-input gate changed before mouse down.";
                    return false;
                }
                environment = atDown;
                report.ForegroundWindowHandleAtDown = atDown.ForegroundWindowHandle;
                report.ForegroundProcessIdAtDown = atDown.ForegroundProcessId;

                NativeMethods.Input[] down = { NativeMethods.Mouse(0, 0, MouseLeftDown) };
                attemptedInputCount = 2;
                report.AttemptedInputCount = attemptedInputCount;
                if (NativeMethods.SendInput(1, down, Marshal.SizeOf(typeof(NativeMethods.Input))) != 1)
                {
                    error = "The staged SendInput mouse-down was not inserted.";
                    return false;
                }
                report.DownInserted = true;
                report.DownTimestamp = Stopwatch.GetTimestamp();

                bool released = false;
                try
                {
                    Thread.Sleep(80);
                }
                finally
                {
                    NativeMethods.Input[] up = { NativeMethods.Mouse(0, 0, MouseLeftUp) };
                    attemptedInputCount = 3;
                    report.AttemptedInputCount = attemptedInputCount;
                    report.UpTimestamp = Stopwatch.GetTimestamp();
                    released = NativeMethods.SendInput(1, up, Marshal.SizeOf(typeof(NativeMethods.Input))) == 1;
                    report.UpInserted = released;
                }
                if (!released)
                {
                    error = "The staged SendInput mouse-up was not inserted; execution is uncertain.";
                    return false;
                }
                return true;
            }
            finally
            {
                report.AttemptedInputCount = attemptedInputCount;
                report.Error = error ?? string.Empty;
                report.FinishedUtc = DateTimeOffset.UtcNow;
            }
        }

        public bool TryCapture(WindowBinding expected, out WindowEnvironment environment, out string error)
        {
            environment = new WindowEnvironment();
            error = string.Empty;
            if (expected == null || expected.ProcessId <= 0 || expected.MainWindowHandle <= 0)
            {
                error = "The expected window binding is invalid.";
                return false;
            }

            uint windowProcessId;
            NativeMethods.GetWindowThreadProcessId(new IntPtr(expected.MainWindowHandle), out windowProcessId);
            if (windowProcessId == 0)
            {
                error = "The target HWND no longer resolves to a process.";
                return false;
            }

            IntPtr process = NativeMethods.OpenProcess(QueryLimitedInformation, false, windowProcessId);
            if (process == IntPtr.Zero)
            {
                error = "OpenProcess(QUERY_LIMITED_INFORMATION) failed.";
                return false;
            }

            try
            {
                long creation;
                if (!TryGetCreationTime(process, out creation))
                {
                    error = "GetProcessTimes failed.";
                    return false;
                }

                StringBuilder path = new StringBuilder(1024);
                int capacity = path.Capacity;
                if (!NativeMethods.QueryFullProcessImageName(process, 0, path, ref capacity))
                {
                    error = "QueryFullProcessImageName failed.";
                    return false;
                }

                IntPtr foreground = NativeMethods.GetForegroundWindow();
                uint foregroundProcessId;
                NativeMethods.GetWindowThreadProcessId(foreground, out foregroundProcessId);
                environment.TargetProcessId = checked((int)windowProcessId);
                environment.TargetProcessCreationFileTimeUtc = creation;
                environment.TargetWindowHandle = expected.MainWindowHandle;
                environment.TargetPath = path.ToString();
                environment.ForegroundWindowHandle = foreground.ToInt64();
                environment.ForegroundProcessId = checked((int)foregroundProcessId);
                NativeMethods.Rect client;
                if (!NativeMethods.GetClientRect(new IntPtr(expected.MainWindowHandle), out client))
                {
                    error = "GetClientRect failed.";
                    return false;
                }
                environment.ClientWidth = checked(client.Right - client.Left);
                environment.ClientHeight = checked(client.Bottom - client.Top);
                return true;
            }
            finally
            {
                NativeMethods.CloseHandle(process);
            }
        }

        private static bool TryPost(
            WindowBinding expected,
            uint message,
            UIntPtr wParam,
            int packed,
            ref int attemptedMessageCount,
            out string error)
        {
            attemptedMessageCount++;
            if (!NativeMethods.PostMessage(new IntPtr(expected.MainWindowHandle), message, wParam, new IntPtr(packed)))
            {
                error = "PostMessage(0x" + message.ToString("X") + ") failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool ModifiersAndButtonsAreReleased()
        {
            int[] keys = { 0x10, 0x11, 0x12, 0x01, 0x02, 0x04, 0x05, 0x06 };
            for (int index = 0; index < keys.Length; index++)
            {
                if ((NativeMethods.GetAsyncKeyState(keys[index]) & 0x8000) != 0) return false;
            }
            return true;
        }

        private static bool TryGetCreationTime(IntPtr process, out long creation)
        {
            NativeMethods.FileTime created;
            NativeMethods.FileTime exited;
            NativeMethods.FileTime kernel;
            NativeMethods.FileTime user;
            if (!NativeMethods.GetProcessTimes(process, out created, out exited, out kernel, out user))
            {
                creation = 0;
                return false;
            }
            creation = ((long)created.High << 32) | created.Low;
            return true;
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct FileTime
            {
                internal uint Low;
                internal uint High;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct Rect
            {
                internal int Left;
                internal int Top;
                internal int Right;
                internal int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct Point
            {
                internal int X;
                internal int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct MouseInput
            {
                internal int X;
                internal int Y;
                internal uint MouseData;
                internal uint Flags;
                internal uint Time;
                internal IntPtr ExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct Input
            {
                internal uint Type;
                internal MouseInput MouseData;
            }

            internal static Input Mouse(int x, int y, uint flags)
            {
                return new Input
                {
                    Type = InputMouse,
                    MouseData = new MouseInput { X = x, Y = y, Flags = flags }
                };
            }

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetClientRect(IntPtr window, out Rect rectangle);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ClientToScreen(IntPtr window, ref Point point);

            [DllImport("user32.dll")]
            internal static extern int GetSystemMetrics(int index);

            [DllImport("user32.dll")]
            internal static extern short GetAsyncKeyState(int virtualKey);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint SendInput(uint count, Input[] inputs, int size);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetProcessTimes(
                IntPtr process,
                out FileTime creation,
                out FileTime exit,
                out FileTime kernel,
                out FileTime user);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool QueryFullProcessImageName(
                IntPtr process,
                uint flags,
                StringBuilder executablePath,
                ref int size);

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr handle);
        }
    }
}
