using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.Input.Win32
{
    public enum ManualInputTraceStatus
    {
        Accepted,
        Rejected
    }

    public enum ManualInputTraceEventKind
    {
        ArmObserved = 0,
        Armed = 1,
        PointerMoved = 2,
        LeftButtonDown = 3,
        LeftButtonUp = 4,
        OtherInput = 5,
        ForegroundChanged = 6,
        PollFailure = 7
    }

    public sealed class ManualInputTraceOptions
    {
        public ManualInputTraceOptions(int expectedTargetCityId, string outputDirectory)
            : this(expectedTargetCityId, outputDirectory, 30000, 500, 15000, 500, 1)
        {
        }

        public ManualInputTraceOptions(
            int expectedTargetCityId,
            string outputDirectory,
            int armTimeoutMilliseconds,
            int cleanArmMilliseconds,
            int recordTimeoutMilliseconds,
            int postClickQuietMilliseconds,
            int pollDelayMilliseconds)
        {
            if (expectedTargetCityId < 0 || expectedTargetCityId >= 50)
                throw new ArgumentOutOfRangeException("expectedTargetCityId");
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.", "outputDirectory");
            if (armTimeoutMilliseconds < 100 || armTimeoutMilliseconds > 120000)
                throw new ArgumentOutOfRangeException("armTimeoutMilliseconds");
            if (cleanArmMilliseconds < 1 || cleanArmMilliseconds > armTimeoutMilliseconds)
                throw new ArgumentOutOfRangeException("cleanArmMilliseconds");
            if (recordTimeoutMilliseconds < 100 || recordTimeoutMilliseconds > 120000)
                throw new ArgumentOutOfRangeException("recordTimeoutMilliseconds");
            if (postClickQuietMilliseconds < 1 || postClickQuietMilliseconds > recordTimeoutMilliseconds)
                throw new ArgumentOutOfRangeException("postClickQuietMilliseconds");
            if (pollDelayMilliseconds < 1 || pollDelayMilliseconds > 20)
                throw new ArgumentOutOfRangeException("pollDelayMilliseconds");

            ExpectedTargetCityId = expectedTargetCityId;
            OutputDirectory = Path.GetFullPath(outputDirectory);
            ArmTimeoutMilliseconds = armTimeoutMilliseconds;
            CleanArmMilliseconds = cleanArmMilliseconds;
            RecordTimeoutMilliseconds = recordTimeoutMilliseconds;
            PostClickQuietMilliseconds = postClickQuietMilliseconds;
            PollDelayMilliseconds = pollDelayMilliseconds;
        }

        public int ExpectedTargetCityId { get; private set; }
        public string OutputDirectory { get; private set; }
        public int ArmTimeoutMilliseconds { get; private set; }
        public int CleanArmMilliseconds { get; private set; }
        public int RecordTimeoutMilliseconds { get; private set; }
        public int PostClickQuietMilliseconds { get; private set; }
        public int PollDelayMilliseconds { get; private set; }
    }

    public sealed class ManualInputTraceResult
    {
        internal ManualInputTraceResult()
        {
            Code = string.Empty;
            Message = string.Empty;
            EvidencePath = string.Empty;
        }

        public ManualInputTraceStatus Status { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public Guid SessionId { get; internal set; }
        public string EvidencePath { get; internal set; }
        public bool ReplayAuthorized { get; internal set; }
        public int EventCount { get; internal set; }
        public int LeftClickCount { get; internal set; }
        public int? ObservedTargetCityId { get; internal set; }
    }

    [Flags]
    internal enum ManualMouseButtons
    {
        None = 0,
        Left = 1,
        Right = 2,
        Middle = 4,
        X1 = 8,
        X2 = 16
    }

    [Flags]
    internal enum ManualModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Windows = 8
    }

    internal sealed class ManualInputPollSample
    {
        internal int ScreenX;
        internal int ScreenY;
        internal int ClientX;
        internal int ClientY;
        internal long ForegroundWindowHandle;
        internal int ForegroundProcessId;
        internal ManualMouseButtons Buttons;
        internal ManualModifiers Modifiers;

        internal ManualInputPollSample Clone()
        {
            return (ManualInputPollSample)MemberwiseClone();
        }
    }

    internal sealed class ManualInputTraceEvent
    {
        internal int Sequence;
        internal ManualInputTraceEventKind Kind;
        internal long ElapsedTicks;
        internal ManualInputPollSample Sample;
    }

    internal sealed class ManualTraceDocument
    {
        internal Guid SessionId;
        internal bool Accepted;
        internal bool ReplayAuthorized;
        internal string Code = string.Empty;
        internal string Message = string.Empty;
        internal int ExpectedTargetCityId;
        internal long StopwatchFrequency;
        internal DateTimeOffset StartedUtc;
        internal DateTimeOffset FinishedUtc;
        internal int ProcessId;
        internal long ProcessCreationFileTimeUtc;
        internal long MainWindowHandle;
        internal InputUiSnapshot StartSnapshot;
        internal InputUiSnapshot FinalSnapshot;
        internal long? LeftDownTicks;
        internal long? LeftUpTicks;
        internal int? ClickClientX;
        internal int? ClickClientY;
        internal double? HoverDwellMilliseconds;
        internal double? ButtonDownMilliseconds;
        internal readonly List<ManualInputTraceEvent> Events = new List<ManualInputTraceEvent>();
    }

    internal interface IManualInputPollSource
    {
        bool TryPoll(WindowBinding expected, out ManualInputPollSample sample, out string error);
    }

    internal interface IManualTraceClock
    {
        long Frequency { get; }
        long Timestamp { get; }
        DateTimeOffset UtcNow { get; }
        void Delay(int milliseconds);
    }

    internal interface IManualTraceEvidenceSink
    {
        string Write(ManualTraceDocument document, string outputDirectory);
    }

    public sealed class San9ManualInputTraceRecorder
    {
        private const int MaximumEvents = 30000;
        private static int globalInFlight;
        private readonly IInputUiObservationSource observationSource;
        private readonly IManualInputPollSource pollSource;
        private readonly IManualTraceClock clock;
        private readonly IManualTraceEvidenceSink evidenceSink;

        public San9ManualInputTraceRecorder()
            : this(
                new AdapterInputUiObservationSource(),
                new Win32ManualInputPollSource(),
                new StopwatchManualTraceClock(),
                new JsonManualTraceEvidenceSink())
        {
        }

        internal San9ManualInputTraceRecorder(
            IInputUiObservationSource observationSource,
            IManualInputPollSource pollSource,
            IManualTraceClock clock,
            IManualTraceEvidenceSink evidenceSink)
        {
            if (observationSource == null) throw new ArgumentNullException("observationSource");
            if (pollSource == null) throw new ArgumentNullException("pollSource");
            if (clock == null) throw new ArgumentNullException("clock");
            if (evidenceSink == null) throw new ArgumentNullException("evidenceSink");
            this.observationSource = observationSource;
            this.pollSource = pollSource;
            this.clock = clock;
            this.evidenceSink = evidenceSink;
        }

        public ManualInputTraceResult Record(ManualInputTraceOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");

            ManualTraceDocument document = new ManualTraceDocument
            {
                SessionId = Guid.NewGuid(),
                ExpectedTargetCityId = options.ExpectedTargetCityId,
                StopwatchFrequency = clock.Frequency,
                StartedUtc = clock.UtcNow
            };

            if (Interlocked.CompareExchange(ref globalInFlight, 1, 0) != 0)
            {
                Reject(document, "RECORDER_BUSY", "Another manual trace recorder is already active.");
                return Finish(document, options.OutputDirectory);
            }

            try
            {
                try
                {
                    Capture(document, options);
                }
                catch (Exception exception)
                {
                    Reject(document, "RECORDER_EXCEPTION", exception.GetType().Name + ": " + exception.Message);
                }
                return Finish(document, options.OutputDirectory);
            }
            finally
            {
                Interlocked.Exchange(ref globalInFlight, 0);
            }
        }

        private void Capture(ManualTraceDocument document, ManualInputTraceOptions options)
        {
            InputUiSnapshot start = observationSource.Capture();
            document.StartSnapshot = start;
            string error;
            if (!ValidateStart(start, out error))
            {
                Reject(document, "START_STATE_REJECTED", error);
                return;
            }

            document.ProcessId = start.ProcessId;
            document.ProcessCreationFileTimeUtc = start.ProcessCreationFileTimeUtc;
            document.MainWindowHandle = start.MainWindowHandle;
            WindowBinding binding = new WindowBinding
            {
                ProcessId = start.ProcessId,
                ProcessCreationFileTimeUtc = start.ProcessCreationFileTimeUtc,
                MainWindowHandle = start.MainWindowHandle
            };

            long captureOrigin = clock.Timestamp;
            long armDeadline = AddMilliseconds(captureOrigin, options.ArmTimeoutMilliseconds);
            long? cleanSince = null;
            ManualInputPollSample armedSample = null;
            ManualInputPollSample lastArmObservation = null;
            long armedAt = 0;

            while (clock.Timestamp < armDeadline)
            {
                ManualInputPollSample sample;
                if (!pollSource.TryPoll(binding, out sample, out error))
                {
                    Reject(document, "ARM_POLL_FAILED", error);
                    return;
                }

                long now = clock.Timestamp;
                if (lastArmObservation == null || ArmStateChanged(lastArmObservation, sample))
                {
                    AddEvent(document, ManualInputTraceEventKind.ArmObserved, now - captureOrigin, sample);
                    lastArmObservation = sample.Clone();
                }
                bool clean = IsExpectedForeground(sample, binding)
                    && sample.Buttons == ManualMouseButtons.None
                    && sample.Modifiers == ManualModifiers.None;
                if (!clean)
                {
                    cleanSince = null;
                }
                else
                {
                    if (!cleanSince.HasValue) cleanSince = now;
                    if (ElapsedMilliseconds(cleanSince.Value, now) >= options.CleanArmMilliseconds)
                    {
                        armedSample = sample.Clone();
                        armedAt = now;
                        AddEvent(document, ManualInputTraceEventKind.Armed, now - captureOrigin, sample);
                        break;
                    }
                }
                clock.Delay(options.PollDelayMilliseconds);
            }

            if (armedSample == null)
            {
                Reject(document, "ARM_TIMEOUT", "The exact game window did not remain foreground with all buttons and modifiers released.");
                return;
            }

            long recordDeadline = AddMilliseconds(armedAt, options.RecordTimeoutMilliseconds);
            ManualInputPollSample previous = armedSample;
            long lastMovementAt = armedAt;
            bool leftDownSeen = false;
            bool leftUpSeen = false;
            long quietSince = 0;

            while (clock.Timestamp < recordDeadline)
            {
                ManualInputPollSample sample;
                if (!pollSource.TryPoll(binding, out sample, out error))
                {
                    AddEvent(document, ManualInputTraceEventKind.PollFailure, clock.Timestamp - captureOrigin, previous);
                    Reject(document, "RECORD_POLL_FAILED", error);
                    return;
                }

                long now = clock.Timestamp;
                if (!IsExpectedForeground(sample, binding))
                {
                    AddEvent(document, ManualInputTraceEventKind.ForegroundChanged, now - captureOrigin, sample);
                    Reject(document, "FOREGROUND_CHANGED", "The foreground HWND or PID changed after the recorder armed.");
                    return;
                }

                ManualMouseButtons forbiddenButtons = sample.Buttons & ~ManualMouseButtons.Left;
                if (forbiddenButtons != ManualMouseButtons.None || sample.Modifiers != ManualModifiers.None)
                {
                    AddEvent(document, ManualInputTraceEventKind.OtherInput, now - captureOrigin, sample);
                    Reject(document, "OTHER_INPUT_OBSERVED", "A non-left mouse button or keyboard modifier was observed after arming.");
                    return;
                }

                if (sample.ScreenX != previous.ScreenX || sample.ScreenY != previous.ScreenY)
                {
                    AddEvent(document, ManualInputTraceEventKind.PointerMoved, now - captureOrigin, sample);
                    lastMovementAt = now;
                }

                bool previousLeft = (previous.Buttons & ManualMouseButtons.Left) != 0;
                bool currentLeft = (sample.Buttons & ManualMouseButtons.Left) != 0;
                if (!previousLeft && currentLeft)
                {
                    if (leftDownSeen || leftUpSeen)
                    {
                        AddEvent(document, ManualInputTraceEventKind.LeftButtonDown, now - captureOrigin, sample);
                        Reject(document, "EXTRA_LEFT_CLICK", "More than one left-button gesture was observed.");
                        return;
                    }
                    leftDownSeen = true;
                    document.LeftDownTicks = now - captureOrigin;
                    document.ClickClientX = sample.ClientX;
                    document.ClickClientY = sample.ClientY;
                    document.HoverDwellMilliseconds = ElapsedMilliseconds(lastMovementAt, now);
                    AddEvent(document, ManualInputTraceEventKind.LeftButtonDown, now - captureOrigin, sample);
                }
                else if (previousLeft && !currentLeft)
                {
                    if (!leftDownSeen || leftUpSeen)
                    {
                        AddEvent(document, ManualInputTraceEventKind.LeftButtonUp, now - captureOrigin, sample);
                        Reject(document, "LEFT_SEQUENCE_INVALID", "A left-button up edge was observed without one matching down edge.");
                        return;
                    }
                    leftUpSeen = true;
                    document.LeftUpTicks = now - captureOrigin;
                    document.ButtonDownMilliseconds = document.LeftDownTicks.HasValue
                        ? ElapsedMilliseconds(document.LeftDownTicks.Value, document.LeftUpTicks.Value) : (double?)null;
                    quietSince = now;
                    AddEvent(document, ManualInputTraceEventKind.LeftButtonUp, now - captureOrigin, sample);
                }

                if (document.Events.Count > MaximumEvents)
                {
                    Reject(document, "EVENT_LIMIT_EXCEEDED", "The bounded trace event limit was exceeded.");
                    return;
                }

                previous = sample;
                if (leftUpSeen && ElapsedMilliseconds(quietSince, now) >= options.PostClickQuietMilliseconds)
                    break;
                clock.Delay(options.PollDelayMilliseconds);
            }

            if (!leftDownSeen || !leftUpSeen)
            {
                Reject(document, leftDownSeen ? "LEFT_CLICK_INCOMPLETE" : "RECORD_TIMEOUT",
                    leftDownSeen ? "The left button was not released before the trace deadline." : "No complete left click was observed before the trace deadline.");
                return;
            }

            InputUiSnapshot final = observationSource.Capture();
            document.FinalSnapshot = final;
            if (!ValidateFinal(final, start, options.ExpectedTargetCityId, out error))
            {
                Reject(document, "FINAL_STATE_REJECTED", error);
                return;
            }

            document.Accepted = true;
            document.ReplayAuthorized = true;
            document.Code = "TRACE_ACCEPTED";
            document.Message = "One clean manual left click reached the exact requested city menu.";
        }

        private ManualInputTraceResult Finish(ManualTraceDocument document, string outputDirectory)
        {
            document.FinishedUtc = clock.UtcNow;
            string path;
            try
            {
                path = evidenceSink.Write(document, outputDirectory);
            }
            catch (Exception exception)
            {
                document.Accepted = false;
                document.ReplayAuthorized = false;
                return new ManualInputTraceResult
                {
                    Status = ManualInputTraceStatus.Rejected,
                    Code = "EVIDENCE_WRITE_FAILED",
                    Message = exception.GetType().Name + ": " + exception.Message,
                    SessionId = document.SessionId,
                    ReplayAuthorized = false,
                    EventCount = document.Events.Count,
                    LeftClickCount = CountClicks(document),
                    ObservedTargetCityId = document.FinalSnapshot == null ? (int?)null : document.FinalSnapshot.CityId
                };
            }

            return new ManualInputTraceResult
            {
                Status = document.Accepted ? ManualInputTraceStatus.Accepted : ManualInputTraceStatus.Rejected,
                Code = document.Code,
                Message = document.Message,
                SessionId = document.SessionId,
                EvidencePath = path,
                ReplayAuthorized = document.ReplayAuthorized,
                EventCount = document.Events.Count,
                LeftClickCount = CountClicks(document),
                ObservedTargetCityId = document.FinalSnapshot == null ? (int?)null : document.FinalSnapshot.CityId
            };
        }

        private static int CountClicks(ManualTraceDocument document)
        {
            int downs = 0;
            int ups = 0;
            foreach (ManualInputTraceEvent item in document.Events)
            {
                if (item.Kind == ManualInputTraceEventKind.LeftButtonDown) downs++;
                if (item.Kind == ManualInputTraceEventKind.LeftButtonUp) ups++;
            }
            return Math.Min(downs, ups);
        }

        private static void Reject(ManualTraceDocument document, string code, string message)
        {
            document.Accepted = false;
            document.ReplayAuthorized = false;
            document.Code = code;
            document.Message = message;
        }

        private static bool ValidateStart(InputUiSnapshot snapshot, out string error)
        {
            if (snapshot == null) return Fail("No start UI observation was returned.", out error);
            if (!snapshot.ReadSucceeded || !snapshot.StableAbc) return Fail("The start observation is not stable A/B/C.", out error);
            if (snapshot.HasBlockingIssue) return Fail("The start observation contains a blocking issue.", out error);
            if (!snapshot.StrategicInput) return Fail("The game is not in verified strategic input.", out error);
            if (snapshot.Layer != UiLayerKind.StrategicMapCandidate) return Fail("Open the strategic-map facilities list before recording.", out error);
            if (snapshot.ProcessId <= 0 || snapshot.ProcessCreationFileTimeUtc <= 0 || snapshot.MainWindowHandle <= 0)
                return Fail("The start process binding is incomplete.", out error);
            if (string.IsNullOrEmpty(snapshot.WindowObservationToken) || string.IsNullOrEmpty(snapshot.StructuralToken))
                return Fail("The start observation token is missing.", out error);
            error = string.Empty;
            return true;
        }

        private static bool ValidateFinal(InputUiSnapshot final, InputUiSnapshot start, int expectedCityId, out string error)
        {
            if (final == null) return Fail("No final UI observation was returned.", out error);
            if (!final.ReadSucceeded || !final.StableAbc) return Fail("The final observation is not stable A/B/C.", out error);
            if (final.HasBlockingIssue) return Fail("The final observation contains a blocking issue.", out error);
            if (!final.StrategicInput) return Fail("The game left verified strategic input.", out error);
            if (final.Layer != UiLayerKind.DomesticCommandMenu) return Fail("The final layer is not the domestic command menu.", out error);
            if (final.ProcessId != start.ProcessId
                || final.ProcessCreationFileTimeUtc != start.ProcessCreationFileTimeUtc
                || final.MainWindowHandle != start.MainWindowHandle)
                return Fail("PID, process generation, or HWND changed during the manual trace.", out error);
            if (final.CityId != expectedCityId) return Fail("The final domestic menu is not the requested target city.", out error);
            if (string.IsNullOrEmpty(final.WindowObservationToken) || string.IsNullOrEmpty(final.StructuralToken))
                return Fail("The final observation token is missing.", out error);
            error = string.Empty;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private static bool IsExpectedForeground(ManualInputPollSample sample, WindowBinding binding)
        {
            return sample != null
                && sample.ForegroundWindowHandle == binding.MainWindowHandle
                && sample.ForegroundProcessId == binding.ProcessId;
        }

        private static bool ArmStateChanged(ManualInputPollSample before, ManualInputPollSample after)
        {
            return before.ForegroundWindowHandle != after.ForegroundWindowHandle
                || before.ForegroundProcessId != after.ForegroundProcessId
                || before.Buttons != after.Buttons
                || before.Modifiers != after.Modifiers;
        }

        private static void AddEvent(ManualTraceDocument document, ManualInputTraceEventKind kind, long elapsedTicks, ManualInputPollSample sample)
        {
            document.Events.Add(new ManualInputTraceEvent
            {
                Sequence = document.Events.Count,
                Kind = kind,
                ElapsedTicks = elapsedTicks,
                Sample = sample == null ? new ManualInputPollSample() : sample.Clone()
            });
        }

        private long AddMilliseconds(long timestamp, int milliseconds)
        {
            decimal ticks = ((decimal)milliseconds * clock.Frequency) / 1000m;
            if (ticks > long.MaxValue - timestamp) return long.MaxValue;
            return timestamp + (long)ticks;
        }

        private double ElapsedMilliseconds(long start, long end)
        {
            if (end <= start) return 0d;
            return ((double)(end - start) * 1000d) / clock.Frequency;
        }
    }

    internal sealed class StopwatchManualTraceClock : IManualTraceClock
    {
        public long Frequency { get { return Stopwatch.Frequency; } }
        public long Timestamp { get { return Stopwatch.GetTimestamp(); } }
        public DateTimeOffset UtcNow { get { return DateTimeOffset.UtcNow; } }
        public void Delay(int milliseconds) { Thread.Sleep(milliseconds); }
    }

    internal sealed class Win32ManualInputPollSource : IManualInputPollSource
    {
        private const int VkLButton = 0x01;
        private const int VkRButton = 0x02;
        private const int VkMButton = 0x04;
        private const int VkXButton1 = 0x05;
        private const int VkXButton2 = 0x06;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkLWin = 0x5B;
        private const int VkRWin = 0x5C;

        public bool TryPoll(WindowBinding expected, out ManualInputPollSample sample, out string error)
        {
            sample = null;
            if (expected == null || expected.MainWindowHandle <= 0)
            {
                error = "The expected window binding is missing.";
                return false;
            }

            NativePoint screen;
            if (!GetCursorPos(out screen))
            {
                error = "GetCursorPos failed with Win32 error " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }
            NativePoint client = screen;
            if (!ScreenToClient(new IntPtr(expected.MainWindowHandle), ref client))
            {
                error = "ScreenToClient failed with Win32 error " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            IntPtr foreground = GetForegroundWindow();
            uint foregroundPid;
            GetWindowThreadProcessId(foreground, out foregroundPid);
            sample = new ManualInputPollSample
            {
                ScreenX = screen.X,
                ScreenY = screen.Y,
                ClientX = client.X,
                ClientY = client.Y,
                ForegroundWindowHandle = foreground.ToInt64(),
                ForegroundProcessId = unchecked((int)foregroundPid),
                Buttons = ReadButtons(),
                Modifiers = ReadModifiers()
            };
            error = string.Empty;
            return true;
        }

        private static ManualMouseButtons ReadButtons()
        {
            ManualMouseButtons value = ManualMouseButtons.None;
            if (IsDown(VkLButton)) value |= ManualMouseButtons.Left;
            if (IsDown(VkRButton)) value |= ManualMouseButtons.Right;
            if (IsDown(VkMButton)) value |= ManualMouseButtons.Middle;
            if (IsDown(VkXButton1)) value |= ManualMouseButtons.X1;
            if (IsDown(VkXButton2)) value |= ManualMouseButtons.X2;
            return value;
        }

        private static ManualModifiers ReadModifiers()
        {
            ManualModifiers value = ManualModifiers.None;
            if (IsDown(VkShift)) value |= ManualModifiers.Shift;
            if (IsDown(VkControl)) value |= ManualModifiers.Control;
            if (IsDown(VkMenu)) value |= ManualModifiers.Alt;
            if (IsDown(VkLWin) || IsDown(VkRWin)) value |= ManualModifiers.Windows;
            return value;
        }

        private static bool IsDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);
    }

    internal sealed class JsonManualTraceEvidenceSink : IManualTraceEvidenceSink
    {
        public string Write(ManualTraceDocument document, string outputDirectory)
        {
            if (document == null) throw new ArgumentNullException("document");
            string directory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "S9IT-" + document.SessionId.ToString("N") + ".json");
            string json = Serialize(document);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            return path;
        }

        internal static string Serialize(ManualTraceDocument document)
        {
            string eventPayload = BuildCanonicalEventPayload(document.Events);
            string eventHash = Sha256(eventPayload);
            StringBuilder builder = new StringBuilder(4096 + document.Events.Count * 180);
            builder.Append('{');
            Property(builder, "schema", "san9-manual-input-trace-v1", true);
            Property(builder, "source", "ManualObserved", false);
            Property(builder, "sessionId", document.SessionId.ToString("D"), false);
            Property(builder, "accepted", document.Accepted, false);
            Property(builder, "replayAuthorized", document.ReplayAuthorized, false);
            Property(builder, "code", document.Code, false);
            Property(builder, "message", document.Message, false);
            Property(builder, "expectedTargetCityId", document.ExpectedTargetCityId, false);
            Property(builder, "stopwatchFrequency", document.StopwatchFrequency, false);
            Property(builder, "startedUtc", document.StartedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), false);
            Property(builder, "finishedUtc", document.FinishedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), false);
            Property(builder, "processId", document.ProcessId, false);
            Property(builder, "processCreationFileTimeUtc", document.ProcessCreationFileTimeUtc, false);
            Property(builder, "mainWindowHandle", document.MainWindowHandle, false);
            builder.Append(",\"start\":"); Snapshot(builder, document.StartSnapshot);
            builder.Append(",\"final\":"); Snapshot(builder, document.FinalSnapshot);
            builder.Append(",\"derived\":{");
            NullableNumber(builder, "leftDownTicks", document.LeftDownTicks, true);
            NullableNumber(builder, "leftUpTicks", document.LeftUpTicks, false);
            NullableNumber(builder, "clickClientX", document.ClickClientX, false);
            NullableNumber(builder, "clickClientY", document.ClickClientY, false);
            NullableNumber(builder, "hoverDwellMilliseconds", document.HoverDwellMilliseconds, false);
            NullableNumber(builder, "buttonDownMilliseconds", document.ButtonDownMilliseconds, false);
            builder.Append('}');
            Property(builder, "eventsPayloadSha256", eventHash, false);
            builder.Append(",\"events\":[");
            for (int index = 0; index < document.Events.Count; index++)
            {
                if (index != 0) builder.Append(',');
                Event(builder, document.Events[index]);
            }
            builder.Append("]}");
            return builder.ToString();
        }

        private static void Snapshot(StringBuilder builder, InputUiSnapshot snapshot)
        {
            if (snapshot == null) { builder.Append("null"); return; }
            builder.Append('{');
            Property(builder, "readSucceeded", snapshot.ReadSucceeded, true);
            Property(builder, "stableAbc", snapshot.StableAbc, false);
            Property(builder, "hasBlockingIssue", snapshot.HasBlockingIssue, false);
            Property(builder, "capturedUtc", snapshot.CapturedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), false);
            Property(builder, "processId", snapshot.ProcessId, false);
            Property(builder, "processCreationFileTimeUtc", snapshot.ProcessCreationFileTimeUtc, false);
            Property(builder, "mainWindowHandle", snapshot.MainWindowHandle, false);
            Property(builder, "layer", snapshot.Layer.ToString(), false);
            Property(builder, "strategicInput", snapshot.StrategicInput, false);
            Property(builder, "cityId", snapshot.CityId, false);
            Property(builder, "windowObservationToken", snapshot.WindowObservationToken ?? string.Empty, false);
            Property(builder, "structuralToken", snapshot.StructuralToken ?? string.Empty, false);
            builder.Append('}');
        }

        private static void Event(StringBuilder builder, ManualInputTraceEvent item)
        {
            ManualInputPollSample sample = item.Sample ?? new ManualInputPollSample();
            builder.Append('{');
            Property(builder, "sequence", item.Sequence, true);
            Property(builder, "kind", item.Kind.ToString(), false);
            Property(builder, "elapsedTicks", item.ElapsedTicks, false);
            Property(builder, "screenX", sample.ScreenX, false);
            Property(builder, "screenY", sample.ScreenY, false);
            Property(builder, "clientX", sample.ClientX, false);
            Property(builder, "clientY", sample.ClientY, false);
            Property(builder, "foregroundWindowHandle", sample.ForegroundWindowHandle, false);
            Property(builder, "foregroundProcessId", sample.ForegroundProcessId, false);
            Property(builder, "buttons", sample.Buttons.ToString(), false);
            Property(builder, "modifiers", sample.Modifiers.ToString(), false);
            builder.Append('}');
        }

        internal static string BuildCanonicalEventPayload(IEnumerable<ManualInputTraceEvent> events)
        {
            StringBuilder builder = new StringBuilder();
            foreach (ManualInputTraceEvent item in events)
            {
                ManualInputPollSample sample = item.Sample ?? new ManualInputPollSample();
                builder.Append(item.Sequence).Append('|').Append((int)item.Kind).Append('|').Append(item.ElapsedTicks)
                    .Append('|').Append(sample.ScreenX).Append('|').Append(sample.ScreenY)
                    .Append('|').Append(sample.ClientX).Append('|').Append(sample.ClientY)
                    .Append('|').Append(sample.ForegroundWindowHandle).Append('|').Append(sample.ForegroundProcessId)
                    .Append('|').Append((int)sample.Buttons).Append('|').Append((int)sample.Modifiers).Append('\n');
            }
            return builder.ToString();
        }

        internal static string Sha256(string value)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(value);
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(64);
                foreach (byte item in digest) builder.Append(item.ToString("X2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static void Property(StringBuilder builder, string name, string value, bool first)
        {
            if (!first) builder.Append(',');
            JsonString(builder, name); builder.Append(':'); JsonString(builder, value ?? string.Empty);
        }

        private static void Property(StringBuilder builder, string name, bool value, bool first)
        {
            if (!first) builder.Append(',');
            JsonString(builder, name); builder.Append(':').Append(value ? "true" : "false");
        }

        private static void Property(StringBuilder builder, string name, int value, bool first)
        {
            if (!first) builder.Append(',');
            JsonString(builder, name); builder.Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Property(StringBuilder builder, string name, long value, bool first)
        {
            if (!first) builder.Append(',');
            JsonString(builder, name); builder.Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void NullableNumber(StringBuilder builder, string name, long? value, bool first)
        {
            if (!first) builder.Append(',');
            JsonString(builder, name); builder.Append(':');
            if (value.HasValue) builder.Append(value.Value.ToString(CultureInfo.InvariantCulture)); else builder.Append("null");
        }

        private static void NullableNumber(StringBuilder builder, string name, int? value, bool first)
        {
            if (!first) builder.Append(',');
            JsonString(builder, name); builder.Append(':');
            if (value.HasValue) builder.Append(value.Value.ToString(CultureInfo.InvariantCulture)); else builder.Append("null");
        }

        private static void NullableNumber(StringBuilder builder, string name, double? value, bool first)
        {
            if (!first) builder.Append(',');
            JsonString(builder, name); builder.Append(':');
            if (value.HasValue) builder.Append(value.Value.ToString("R", CultureInfo.InvariantCulture)); else builder.Append("null");
        }

        private static void JsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20) builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
