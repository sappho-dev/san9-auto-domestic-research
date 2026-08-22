using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace San9AutoDomestic.Input.Win32
{
    internal interface IDomesticActionTransport
    {
        bool TryActivate(WindowBinding expected, out string error);
        bool TryCaptureClientPng(WindowBinding expected, string path, out string error);
        bool TryHover(WindowBinding expected, int clientX, int clientY, out bool attempted, out string error);
        bool TryClick(WindowBinding expected, int clientX, int clientY, out bool attempted, out string error);
        bool TryPressReturn(WindowBinding expected, out bool attempted, out string error);
        bool TryPressEscape(WindowBinding expected, out bool attempted, out string error);
    }

    internal sealed class Win32DomesticActionTransport : IDomesticActionTransport
    {
        private const uint InputKeyboard = 1;
        private const uint KeyUp = 0x0002;
        private const ushort VirtualKeyReturn = 0x0D;
        private const ushort VirtualKeyEscape = 0x1B;
        private readonly ITargetedWindowTransport mouse;
        private readonly IProgramInputDispatchTraceSource dispatchTrace;

        internal Win32DomesticActionTransport()
            : this(new Win32TargetedWindowTransport())
        {
        }

        internal Win32DomesticActionTransport(ITargetedWindowTransport mouse)
        {
            if (mouse == null) throw new ArgumentNullException("mouse");
            this.mouse = mouse;
            dispatchTrace = mouse as IProgramInputDispatchTraceSource;
        }

        public bool TryActivate(WindowBinding expected, out string error)
        {
            error = string.Empty;
            try
            {
            WindowEnvironment before;
            if (!mouse.TryCapture(expected, out before, out error)) return false;
            if (!before.TargetMatches(expected))
            {
                error = "The exact PID, generation, HWND, path, or client size changed before activation.";
                return false;
            }
            if (!before.TargetIsForeground(expected))
            {
                NativeMethods.ShowWindow(new IntPtr(expected.MainWindowHandle), 9);
                NativeMethods.BringWindowToTop(new IntPtr(expected.MainWindowHandle));
                NativeMethods.SetForegroundWindow(new IntPtr(expected.MainWindowHandle));
                Thread.Sleep(200);
            }

            WindowEnvironment after;
            if (!mouse.TryCapture(expected, out after, out error)) return false;
            if (!after.TargetMatches(expected) || !after.TargetIsForeground(expected))
            {
                error = "The exact game window could not be made foreground.";
                return false;
            }
            error = string.Empty;
            return true;
            }
            catch (Exception exception)
            {
                error = TransportException("activation", exception);
                return false;
            }
        }

        public bool TryCaptureClientPng(WindowBinding expected, string path, out string error)
        {
            error = string.Empty;
            try
            {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "The screenshot path is empty.";
                return false;
            }
            WindowEnvironment environment;
            if (!mouse.TryCapture(expected, out environment, out error)) return false;
            if (!environment.TargetMatches(expected) || !environment.TargetIsForeground(expected))
            {
                error = "The exact game window is not foreground at the screenshot boundary.";
                return false;
            }

            IntPtr previousDpiContext = NativeMethods.SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                NativeMethods.Rect client;
                NativeMethods.Point origin = new NativeMethods.Point { X = 0, Y = 0 };
                if (!NativeMethods.GetClientRect(new IntPtr(expected.MainWindowHandle), out client)
                    || !NativeMethods.ClientToScreen(new IntPtr(expected.MainWindowHandle), ref origin))
                {
                    error = "Physical client geometry could not be read before screenshot capture.";
                    return false;
                }
                int width = checked(client.Right - client.Left);
                int height = checked(client.Bottom - client.Top);
                if (width < 1024 || height < 768 || width > 4096 || height > 4096)
                {
                    error = "Physical client geometry is outside the supported evidence range.";
                    return false;
                }
                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
                using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                    bitmap.Save(path, ImageFormat.Png);
                }
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = "Screenshot capture failed: " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
            finally
            {
                if (previousDpiContext != IntPtr.Zero)
                    NativeMethods.SetThreadDpiAwarenessContext(previousDpiContext);
            }
            }
            catch (Exception exception)
            {
                error = TransportException("client screenshot", exception);
                return false;
            }
        }

        public bool TryClick(WindowBinding expected, int clientX, int clientY, out bool attempted, out string error)
        {
            attempted = false;
            error = string.Empty;
            ProgramInputDispatchReport previous = dispatchTrace == null ? null : dispatchTrace.LastProgramDispatch;
            try
            {
                WindowEnvironment environment;
                int count;
                bool result = mouse.TrySendInputStagedLeftClick(
                    expected,
                    clientX,
                    clientY,
                    out environment,
                    out count,
                    out error);
                attempted = count > 0;
                return result;
            }
            catch (Exception exception)
            {
                attempted = HasNewAttemptedDispatch(previous, expected, clientX, clientY);
                error = TransportException("staged click", exception);
                return false;
            }
        }

        public bool TryHover(WindowBinding expected, int clientX, int clientY, out bool attempted, out string error)
        {
            attempted = false;
            error = string.Empty;
            try
            {
                WindowEnvironment before;
                if (!mouse.TryCapture(expected, out before, out error)) return false;
                if (!before.TargetMatches(expected) || !before.TargetIsForeground(expected))
                {
                    error = "The exact game window is not the matching foreground target at the hover boundary.";
                    return false;
                }
                WindowEnvironment environment;
                return mouse.TryPostMouseMove(
                    expected,
                    clientX,
                    clientY,
                    out environment,
                    out attempted,
                    out error);
            }
            catch (Exception exception)
            {
                error = TransportException("hover", exception);
                return false;
            }
        }

        public bool TryPressReturn(WindowBinding expected, out bool attempted, out string error)
        {
            return TryPressKey(expected, VirtualKeyReturn, "Return", out attempted, out error);
        }

        public bool TryPressEscape(WindowBinding expected, out bool attempted, out string error)
        {
            return TryPressKey(expected, VirtualKeyEscape, "Escape", out attempted, out error);
        }

        private bool TryPressKey(
            WindowBinding expected,
            ushort virtualKey,
            string keyName,
            out bool attempted,
            out string error)
        {
            attempted = false;
            error = string.Empty;
            try
            {
                WindowEnvironment environment;
                if (!mouse.TryCapture(expected, out environment, out error)) return false;
                if (!environment.TargetMatches(expected) || !environment.TargetIsForeground(expected))
                {
                    error = "The exact game window is not foreground at the " + keyName + "-key boundary.";
                    return false;
                }
                if (!ModifiersAndButtonsAreReleased())
                {
                    error = "A keyboard modifier or mouse button is currently held.";
                    return false;
                }

                NativeMethods.Input down = NativeMethods.Keyboard(virtualKey, 0);
                NativeMethods.Input up = NativeMethods.Keyboard(virtualKey, KeyUp);
                attempted = true;
                if (NativeMethods.SendInput(1, new[] { down }, Marshal.SizeOf(typeof(NativeMethods.Input))) != 1)
                {
                    error = "SendInput " + keyName + " key-down was not inserted.";
                    return false;
                }
                bool released = false;
                try
                {
                    Thread.Sleep(50);
                }
                finally
                {
                    released = NativeMethods.SendInput(1, new[] { up }, Marshal.SizeOf(typeof(NativeMethods.Input))) == 1;
                }
                if (!released)
                {
                    error = "SendInput " + keyName + " key-up was not inserted; execution is uncertain.";
                    return false;
                }
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = TransportException(keyName + " key", exception);
                return false;
            }
        }

        private bool HasNewAttemptedDispatch(
            ProgramInputDispatchReport previous,
            WindowBinding expected,
            int clientX,
            int clientY)
        {
            ProgramInputDispatchReport current = dispatchTrace == null ? null : dispatchTrace.LastProgramDispatch;
            return current != null
                && !object.ReferenceEquals(previous, current)
                && expected != null
                && current.ProcessId == expected.ProcessId
                && current.ProcessCreationFileTimeUtc == expected.ProcessCreationFileTimeUtc
                && current.MainWindowHandle == expected.MainWindowHandle
                && current.ClientX == clientX
                && current.ClientY == clientY
                && current.AttemptedInputCount > 0;
        }

        private static string TransportException(string operation, Exception exception)
        {
            return "The " + operation + " transport threw " + exception.GetType().Name + ": " + exception.Message;
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

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct Point
            {
                internal int X;
                internal int Y;
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
            internal struct KeyboardInput
            {
                internal ushort VirtualKey;
                internal ushort ScanCode;
                internal uint Flags;
                internal uint Time;
                internal IntPtr ExtraInfo;
            }

            [StructLayout(LayoutKind.Explicit, Size = 28)]
            internal struct Input
            {
                [FieldOffset(0)] internal uint Type;
                [FieldOffset(4)] internal KeyboardInput KeyboardData;
            }

            internal static Input Keyboard(ushort virtualKey, uint flags)
            {
                return new Input
                {
                    Type = InputKeyboard,
                    KeyboardData = new KeyboardInput { VirtualKey = virtualKey, Flags = flags }
                };
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetForegroundWindow(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool BringWindowToTop(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ShowWindow(IntPtr window, int command);

            [DllImport("user32.dll")]
            internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetClientRect(IntPtr window, out Rect rectangle);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ClientToScreen(IntPtr window, ref Point point);

            [DllImport("user32.dll")]
            internal static extern short GetAsyncKeyState(int virtualKey);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint SendInput(uint count, Input[] inputs, int size);
        }
    }
}
