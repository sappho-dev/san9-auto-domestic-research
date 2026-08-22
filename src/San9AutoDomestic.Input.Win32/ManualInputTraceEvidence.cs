using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace San9AutoDomestic.Input.Win32
{
    public sealed class ManualInputReplayEvidence
    {
        internal ManualInputReplayEvidence()
        {
            EvidencePath = string.Empty;
            EventsPayloadSha256 = string.Empty;
            StartWindowObservationToken = string.Empty;
            StartStructuralToken = string.Empty;
        }

        public bool ReplayAuthorized { get { return true; } }
        public string EvidencePath { get; internal set; }
        public Guid SessionId { get; internal set; }
        public int TargetCityId { get; internal set; }
        public int ProcessId { get; internal set; }
        public long ProcessCreationFileTimeUtc { get; internal set; }
        public long MainWindowHandle { get; internal set; }
        public int ClientX { get; internal set; }
        public int ClientY { get; internal set; }
        public double HoverDwellMilliseconds { get; internal set; }
        public double ButtonDownMilliseconds { get; internal set; }
        public string EventsPayloadSha256 { get; internal set; }
        public string StartWindowObservationToken { get; internal set; }
        public string StartStructuralToken { get; internal set; }
    }

    public sealed class ManualInputTraceEvidenceLoadResult
    {
        internal ManualInputTraceEvidenceLoadResult()
        {
            Code = string.Empty;
            Message = string.Empty;
        }

        public bool IsValid { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public ManualInputReplayEvidence Evidence { get; internal set; }
    }

    public static class ManualInputTraceEvidenceReader
    {
        private const int MaximumEvidenceBytes = 4 * 1024 * 1024;
        private static readonly string[] RootKeys =
        {
            "schema", "source", "sessionId", "accepted", "replayAuthorized", "code", "message",
            "expectedTargetCityId", "stopwatchFrequency", "startedUtc", "finishedUtc", "processId",
            "processCreationFileTimeUtc", "mainWindowHandle", "start", "final", "derived",
            "eventsPayloadSha256", "events"
        };
        private static readonly string[] SnapshotKeys =
        {
            "readSucceeded", "stableAbc", "hasBlockingIssue", "capturedUtc", "processId",
            "processCreationFileTimeUtc", "mainWindowHandle", "layer", "strategicInput", "cityId",
            "windowObservationToken", "structuralToken"
        };
        private static readonly string[] DerivedKeys =
        {
            "leftDownTicks", "leftUpTicks", "clickClientX", "clickClientY",
            "hoverDwellMilliseconds", "buttonDownMilliseconds"
        };
        private static readonly string[] EventKeys =
        {
            "sequence", "kind", "elapsedTicks", "screenX", "screenY", "clientX", "clientY",
            "foregroundWindowHandle", "foregroundProcessId", "buttons", "modifiers"
        };

        public static ManualInputTraceEvidenceLoadResult Load(string evidencePath)
        {
            if (string.IsNullOrWhiteSpace(evidencePath))
                return Invalid("EVIDENCE_PATH_INVALID", "Evidence path is required.");

            string fullPath;
            string json;
            try
            {
                fullPath = Path.GetFullPath(evidencePath);
                if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                    return Invalid("EVIDENCE_PATH_INVALID", "Evidence must be a JSON file.");
                using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length <= 0 || stream.Length > MaximumEvidenceBytes)
                        return Invalid("EVIDENCE_SIZE_INVALID", "Evidence length is outside the bounded range.");
                    byte[] bytes = new byte[checked((int)stream.Length)];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) return Invalid("EVIDENCE_READ_FAILED", "Evidence ended before its declared file length.");
                        offset += read;
                    }
                    if (stream.ReadByte() != -1) return Invalid("EVIDENCE_READ_FAILED", "Evidence changed while it was being read.");
                    json = new UTF8Encoding(false, true).GetString(bytes);
                }
            }
            catch (Exception exception)
            {
                return Invalid("EVIDENCE_READ_FAILED", exception.GetType().Name + ": " + exception.Message);
            }

            return Parse(fullPath, json);
        }

        internal static ManualInputTraceEvidenceLoadResult ParseForTesting(string json)
        {
            return Parse("synthetic.json", json);
        }

        private static ManualInputTraceEvidenceLoadResult Parse(string fullPath, string json)
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumEvidenceBytes, RecursionLimit = 16 };
                Dictionary<string, object> root = Dictionary(serializer.DeserializeObject(json), "root");
                RequireExactKeys(root, RootKeys, "root");
                Require(String(root, "schema") == "san9-manual-input-trace-v1", "Unexpected trace schema.");
                Require(String(root, "source") == "ManualObserved", "Only manually observed traces may authorize replay.");
                Require(Boolean(root, "accepted"), "The trace was not accepted.");
                Require(Boolean(root, "replayAuthorized"), "The trace does not authorize replay.");
                Require(String(root, "code") == "TRACE_ACCEPTED", "The trace result code is not TRACE_ACCEPTED.");

                Guid sessionId;
                Require(Guid.TryParse(String(root, "sessionId"), out sessionId) && sessionId != Guid.Empty, "Session ID is invalid.");
                int targetCityId = Integer(root, "expectedTargetCityId");
                Require(targetCityId >= 0 && targetCityId < 50, "Target city ID is invalid.");
                long frequency = Long(root, "stopwatchFrequency");
                Require(frequency > 0, "Stopwatch frequency is invalid.");
                DateTimeOffset started;
                DateTimeOffset finished;
                Require(DateTimeOffset.TryParseExact(String(root, "startedUtc"), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started), "Start timestamp is invalid.");
                Require(DateTimeOffset.TryParseExact(String(root, "finishedUtc"), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out finished), "Finish timestamp is invalid.");
                Require(finished >= started, "Trace timestamps are reversed.");

                int processId = Integer(root, "processId");
                long generation = Long(root, "processCreationFileTimeUtc");
                long window = Long(root, "mainWindowHandle");
                Require(processId > 0 && generation > 0 && window > 0, "Process binding is incomplete.");

                Dictionary<string, object> start = Dictionary(Value(root, "start"), "start");
                Dictionary<string, object> final = Dictionary(Value(root, "final"), "final");
                ValidateSnapshot(start, "StrategicMapCandidate", -1, processId, generation, window, false);
                ValidateSnapshot(final, "DomesticCommandMenu", targetCityId, processId, generation, window, true);

                Dictionary<string, object> derived = Dictionary(Value(root, "derived"), "derived");
                RequireExactKeys(derived, DerivedKeys, "derived");
                long downTicks = Long(derived, "leftDownTicks");
                long upTicks = Long(derived, "leftUpTicks");
                int clientX = Integer(derived, "clickClientX");
                int clientY = Integer(derived, "clickClientY");
                double hoverDwell = Double(derived, "hoverDwellMilliseconds");
                double buttonDown = Double(derived, "buttonDownMilliseconds");
                Require(downTicks >= 0 && upTicks > downTicks, "Left-click timing is invalid.");
                Require(clientX >= 0 && clientX < 1024 && clientY >= 0 && clientY < 768, "Click coordinate is outside the exact client area.");
                Require(IsFinite(hoverDwell) && hoverDwell >= 0d && hoverDwell <= 120000d, "Hover dwell is invalid.");
                Require(IsFinite(buttonDown) && buttonDown > 0d && buttonDown <= 2000d, "Button-down duration is invalid.");

                object[] rawEvents = Array(Value(root, "events"), "events");
                Require(rawEvents.Length >= 4 && rawEvents.Length <= 30000, "Event count is outside the bounded range.");
                List<ManualInputTraceEvent> events = new List<ManualInputTraceEvent>(rawEvents.Length);
                int armedIndex = -1;
                int downIndex = -1;
                int upIndex = -1;
                long previousElapsed = -1;
                for (int index = 0; index < rawEvents.Length; index++)
                {
                    Dictionary<string, object> raw = Dictionary(rawEvents[index], "event");
                    RequireExactKeys(raw, EventKeys, "event");
                    Require(Integer(raw, "sequence") == index, "Event sequence is not canonical.");
                    long elapsed = Long(raw, "elapsedTicks");
                    Require(elapsed >= previousElapsed, "Event timestamps are not monotonic.");
                    previousElapsed = elapsed;
                    ManualInputTraceEventKind kind;
                    Require(Enum.TryParse(String(raw, "kind"), false, out kind) && Enum.IsDefined(typeof(ManualInputTraceEventKind), kind), "Event kind is invalid.");
                    ManualMouseButtons buttons;
                    ManualModifiers modifiers;
                    Require(Enum.TryParse(String(raw, "buttons"), false, out buttons) && (((int)buttons & ~31) == 0), "Mouse-button state is invalid.");
                    Require(Enum.TryParse(String(raw, "modifiers"), false, out modifiers) && (((int)modifiers & ~15) == 0), "Modifier state is invalid.");
                    ManualInputPollSample sample = new ManualInputPollSample
                    {
                        ScreenX = Integer(raw, "screenX"),
                        ScreenY = Integer(raw, "screenY"),
                        ClientX = Integer(raw, "clientX"),
                        ClientY = Integer(raw, "clientY"),
                        ForegroundWindowHandle = Long(raw, "foregroundWindowHandle"),
                        ForegroundProcessId = Integer(raw, "foregroundProcessId"),
                        Buttons = buttons,
                        Modifiers = modifiers
                    };
                    if (kind == ManualInputTraceEventKind.Armed) { Require(armedIndex < 0, "Multiple Armed events exist."); armedIndex = index; }
                    if (kind == ManualInputTraceEventKind.LeftButtonDown) { Require(downIndex < 0, "Multiple left-down events exist."); downIndex = index; }
                    if (kind == ManualInputTraceEventKind.LeftButtonUp) { Require(upIndex < 0, "Multiple left-up events exist."); upIndex = index; }
                    Require(kind != ManualInputTraceEventKind.OtherInput
                        && kind != ManualInputTraceEventKind.ForegroundChanged
                        && kind != ManualInputTraceEventKind.PollFailure, "A rejected event kind exists in an accepted trace.");
                    events.Add(new ManualInputTraceEvent { Sequence = index, Kind = kind, ElapsedTicks = elapsed, Sample = sample });
                }

                Require(armedIndex >= 0 && downIndex > armedIndex && upIndex > downIndex, "The single-click event order is invalid.");
                Require(events[downIndex].ElapsedTicks == downTicks && events[upIndex].ElapsedTicks == upTicks, "Derived click ticks differ from event ticks.");
                Require(events[downIndex].Sample.ClientX == clientX && events[downIndex].Sample.ClientY == clientY, "Derived click coordinate differs from the down event.");
                Require(events[downIndex].Sample.Buttons == ManualMouseButtons.Left && events[upIndex].Sample.Buttons == ManualMouseButtons.None, "Click edge button states are invalid.");
                for (int index = armedIndex; index < events.Count; index++)
                {
                    Require(events[index].Sample.ForegroundProcessId == processId
                        && events[index].Sample.ForegroundWindowHandle == window, "Foreground binding changed at or after arming.");
                    Require(events[index].Sample.Modifiers == ManualModifiers.None, "A modifier exists at or after arming.");
                    Require((events[index].Sample.Buttons & ~ManualMouseButtons.Left) == 0, "A non-left mouse button exists at or after arming.");
                }

                string expectedHash = String(root, "eventsPayloadSha256");
                Require(expectedHash.Length == 64, "Event payload digest length is invalid.");
                string actualHash = JsonManualTraceEvidenceSink.Sha256(JsonManualTraceEvidenceSink.BuildCanonicalEventPayload(events));
                Require(FixedEquals(expectedHash, actualHash), "Event payload digest mismatch.");

                return new ManualInputTraceEvidenceLoadResult
                {
                    IsValid = true,
                    Code = "EVIDENCE_VALID",
                    Message = "The manual trace is intact and eligible for a separately authorized replay.",
                    Evidence = new ManualInputReplayEvidence
                    {
                        EvidencePath = fullPath,
                        SessionId = sessionId,
                        TargetCityId = targetCityId,
                        ProcessId = processId,
                        ProcessCreationFileTimeUtc = generation,
                        MainWindowHandle = window,
                        ClientX = clientX,
                        ClientY = clientY,
                        HoverDwellMilliseconds = hoverDwell,
                        ButtonDownMilliseconds = buttonDown,
                        EventsPayloadSha256 = actualHash,
                        StartWindowObservationToken = String(start, "windowObservationToken"),
                        StartStructuralToken = String(start, "structuralToken")
                    }
                };
            }
            catch (EvidenceFormatException exception)
            {
                return Invalid("EVIDENCE_INVALID", exception.Message);
            }
            catch (Exception exception)
            {
                return Invalid("EVIDENCE_PARSE_FAILED", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void ValidateSnapshot(Dictionary<string, object> value, string layer, int cityId, int processId, long generation, long window, bool exactCity)
        {
            RequireExactKeys(value, SnapshotKeys, "snapshot");
            Require(Boolean(value, "readSucceeded") && Boolean(value, "stableAbc") && !Boolean(value, "hasBlockingIssue"), "Snapshot readiness is invalid.");
            DateTimeOffset captured;
            Require(DateTimeOffset.TryParseExact(String(value, "capturedUtc"), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out captured), "Snapshot timestamp is invalid.");
            Require(Integer(value, "processId") == processId
                && Long(value, "processCreationFileTimeUtc") == generation
                && Long(value, "mainWindowHandle") == window, "Snapshot process binding differs from the trace root.");
            Require(String(value, "layer") == layer && Boolean(value, "strategicInput"), "Snapshot layer or phase is invalid.");
            if (exactCity) Require(Integer(value, "cityId") == cityId, "Final snapshot city differs from the requested target.");
            else Require(Integer(value, "cityId") == -1, "Start map snapshot unexpectedly claims an exact city.");
            Require(!string.IsNullOrEmpty(String(value, "windowObservationToken"))
                && !string.IsNullOrEmpty(String(value, "structuralToken")), "Snapshot observation token is missing.");
        }

        private static object Value(Dictionary<string, object> value, string key)
        {
            object result;
            Require(value.TryGetValue(key, out result) && result != null, "Required field is missing or null: " + key + ".");
            return result;
        }

        private static Dictionary<string, object> Dictionary(object value, string name)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            Require(result != null, name + " must be an object.");
            return result;
        }

        private static object[] Array(object value, string name)
        {
            object[] result = value as object[];
            Require(result != null, name + " must be an array.");
            return result;
        }

        private static string String(Dictionary<string, object> value, string key)
        {
            string result = Value(value, key) as string;
            Require(result != null, key + " must be a string.");
            return result;
        }

        private static bool Boolean(Dictionary<string, object> value, string key)
        {
            object raw = Value(value, key);
            Require(raw is bool, key + " must be a Boolean.");
            return (bool)raw;
        }

        private static int Integer(Dictionary<string, object> value, string key)
        {
            long result = Long(value, key);
            Require(result >= int.MinValue && result <= int.MaxValue, key + " is outside Int32.");
            return (int)result;
        }

        private static long Long(Dictionary<string, object> value, string key)
        {
            object raw = Value(value, key);
            if (raw is int) return (int)raw;
            if (raw is long) return (long)raw;
            if (raw is decimal)
            {
                decimal decimalValue = (decimal)raw;
                Require(decimal.Truncate(decimalValue) == decimalValue && decimalValue >= long.MinValue && decimalValue <= long.MaxValue, key + " is not an exact Int64.");
                return (long)decimalValue;
            }
            Require(false, key + " must be an integer.");
            return 0;
        }

        private static double Double(Dictionary<string, object> value, string key)
        {
            object raw = Value(value, key);
            if (raw is double) return (double)raw;
            if (raw is decimal) return (double)(decimal)raw;
            if (raw is int) return (int)raw;
            if (raw is long) return (long)raw;
            Require(false, key + " must be numeric.");
            return 0d;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void RequireExactKeys(Dictionary<string, object> value, IEnumerable<string> expected, string name)
        {
            HashSet<string> keys = new HashSet<string>(expected, StringComparer.Ordinal);
            Require(value.Count == keys.Count, name + " has an unexpected field count.");
            foreach (string key in value.Keys) Require(keys.Contains(key), name + " contains an unexpected field: " + key + ".");
        }

        private static bool FixedEquals(string expected, string actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length) return false;
            int difference = 0;
            for (int index = 0; index < expected.Length; index++) difference |= expected[index] ^ actual[index];
            return difference == 0;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new EvidenceFormatException(message);
        }

        private static ManualInputTraceEvidenceLoadResult Invalid(string code, string message)
        {
            return new ManualInputTraceEvidenceLoadResult { IsValid = false, Code = code, Message = message };
        }

        private sealed class EvidenceFormatException : Exception
        {
            internal EvidenceFormatException(string message) : base(message) { }
        }
    }
}
