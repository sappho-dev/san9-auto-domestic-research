using System;
using System.Collections.Generic;
using System.IO;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Input.Win32;

namespace San9AutoDomestic.Input.SelfTest
{
    internal static class ManualInputTraceSyntheticTests
    {
        internal static void Register(Action<string, Action> run)
        {
            run("manual trace accepts exactly one clean click", AcceptsOneCleanClick);
            run("manual trace rejects a second left click", RejectsExtraClick);
            run("manual trace rejects right mouse input", RejectsRightButton);
            run("manual trace rejects middle mouse input", RejectsMiddleButton);
            run("manual trace rejects keyboard modifiers", RejectsModifier);
            run("manual trace rejects foreground change", RejectsForegroundChange);
            run("manual trace rejects incomplete left gesture", RejectsIncompleteClick);
            run("manual trace rejects wrong final city", RejectsWrongFinalCity);
            run("manual trace rejects wrong final layer", RejectsWrongFinalLayer);
            run("manual trace derives click coordinates and timing", DerivesCoordinatesAndTiming);
            run("manual trace JSON is deterministic and manual-only", JsonIsDeterministic);
        }

        private static void AcceptsOneCleanClick()
        {
            Fixture fixture = Fixture.Valid();
            ManualInputTraceResult result = fixture.Record();
            Equal(ManualInputTraceStatus.Accepted, result.Status);
            Equal("TRACE_ACCEPTED", result.Code);
            True(result.ReplayAuthorized);
            Equal(1, result.LeftClickCount);
            Equal(16, result.ObservedTargetCityId.Value);
            True(fixture.Sink.Document.Accepted && fixture.Sink.Document.ReplayAuthorized);
        }

        private static void RejectsExtraClick()
        {
            Fixture fixture = Fixture.Valid();
            fixture.Poll.SetSamples(ArmSamples(
                Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.Left, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.Left, ManualModifiers.None)));
            ManualInputTraceResult result = fixture.Record();
            Equal(ManualInputTraceStatus.Rejected, result.Status);
            Equal("EXTRA_LEFT_CLICK", result.Code);
            True(!result.ReplayAuthorized);
        }

        private static void RejectsRightButton()
        {
            Fixture fixture = Fixture.Valid();
            fixture.Poll.SetSamples(ArmSamples(Sample(200, 101, ManualMouseButtons.Right, ManualModifiers.None)));
            Equal("OTHER_INPUT_OBSERVED", fixture.Record().Code);
        }

        private static void RejectsMiddleButton()
        {
            Fixture fixture = Fixture.Valid();
            fixture.Poll.SetSamples(ArmSamples(Sample(200, 101, ManualMouseButtons.Middle, ManualModifiers.None)));
            Equal("OTHER_INPUT_OBSERVED", fixture.Record().Code);
        }

        private static void RejectsModifier()
        {
            Fixture fixture = Fixture.Valid();
            fixture.Poll.SetSamples(ArmSamples(Sample(200, 101, ManualMouseButtons.None, ManualModifiers.Control)));
            Equal("OTHER_INPUT_OBSERVED", fixture.Record().Code);
        }

        private static void RejectsForegroundChange()
        {
            Fixture fixture = Fixture.Valid();
            ManualInputPollSample changed = Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None);
            changed.ForegroundWindowHandle++;
            fixture.Poll.SetSamples(ArmSamples(changed));
            Equal("FOREGROUND_CHANGED", fixture.Record().Code);
        }

        private static void RejectsIncompleteClick()
        {
            Fixture fixture = Fixture.Valid();
            fixture.Poll.SetSamples(ArmSamples(
                Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.Left, ManualModifiers.None)));
            ManualInputTraceResult result = fixture.Record();
            Equal("LEFT_CLICK_INCOMPLETE", result.Code);
            True(!result.ReplayAuthorized);
        }

        private static void RejectsWrongFinalCity()
        {
            Fixture fixture = Fixture.Valid();
            fixture.Final.CityId = 8;
            ManualInputTraceResult result = fixture.Record();
            Equal("FINAL_STATE_REJECTED", result.Code);
            True(!result.ReplayAuthorized);
        }

        private static void RejectsWrongFinalLayer()
        {
            Fixture fixture = Fixture.Valid();
            fixture.Final.Layer = UiLayerKind.StrategicMapCandidate;
            ManualInputTraceResult result = fixture.Record();
            Equal("FINAL_STATE_REJECTED", result.Code);
            True(!result.ReplayAuthorized);
        }

        private static void DerivesCoordinatesAndTiming()
        {
            Fixture fixture = Fixture.Valid();
            ManualInputTraceResult result = fixture.Record();
            Equal(ManualInputTraceStatus.Accepted, result.Status);
            ManualTraceDocument document = fixture.Sink.Document;
            Equal(200, document.ClickClientX.Value);
            Equal(101, document.ClickClientY.Value);
            Equal(1d, document.HoverDwellMilliseconds.Value);
            Equal(1d, document.ButtonDownMilliseconds.Value);
            True(document.Events.Count >= 4);
        }

        private static void JsonIsDeterministic()
        {
            Fixture fixture = Fixture.Valid();
            Equal(ManualInputTraceStatus.Accepted, fixture.Record().Status);
            string first = JsonManualTraceEvidenceSink.Serialize(fixture.Sink.Document);
            string second = JsonManualTraceEvidenceSink.Serialize(fixture.Sink.Document);
            Equal(first, second);
            True(first.IndexOf("\"source\":\"ManualObserved\"", StringComparison.Ordinal) >= 0);
            True(first.IndexOf("\"eventsPayloadSha256\":\"", StringComparison.Ordinal) >= 0);
            True(first.IndexOf("ProgramDispatched", StringComparison.Ordinal) < 0);
        }

        private static ManualInputPollSample[] ArmSamples(params ManualInputPollSample[] afterArm)
        {
            List<ManualInputPollSample> values = new List<ManualInputPollSample>();
            values.Add(Sample(100, 100, ManualMouseButtons.None, ManualModifiers.None));
            values.Add(Sample(100, 100, ManualMouseButtons.None, ManualModifiers.None));
            values.Add(Sample(100, 100, ManualMouseButtons.None, ManualModifiers.None));
            values.AddRange(afterArm);
            return values.ToArray();
        }

        private static ManualInputPollSample[] ValidSamples()
        {
            return ArmSamples(
                Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.Left, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None),
                Sample(200, 101, ManualMouseButtons.None, ManualModifiers.None));
        }

        private static ManualInputPollSample Sample(int clientX, int clientY, ManualMouseButtons buttons, ManualModifiers modifiers)
        {
            return new ManualInputPollSample
            {
                ScreenX = clientX + 50,
                ScreenY = clientY + 50,
                ClientX = clientX,
                ClientY = clientY,
                ForegroundWindowHandle = 0x00120ED8,
                ForegroundProcessId = 37692,
                Buttons = buttons,
                Modifiers = modifiers
            };
        }

        private static InputUiSnapshot StartSnapshot()
        {
            return new InputUiSnapshot
            {
                ReadSucceeded = true,
                StableAbc = true,
                HasBlockingIssue = false,
                CapturedUtc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero),
                ProcessId = 37692,
                ProcessCreationFileTimeUtc = 639218891755456732,
                MainWindowHandle = 0x00120ED8,
                Layer = UiLayerKind.StrategicMapCandidate,
                StrategicInput = true,
                CityId = -1,
                WindowObservationToken = "map-token",
                StructuralToken = "map-structure"
            };
        }

        private static InputUiSnapshot FinalSnapshot()
        {
            InputUiSnapshot value = StartSnapshot();
            value.Layer = UiLayerKind.DomesticCommandMenu;
            value.CityId = 16;
            value.WindowObservationToken = "city-16-menu";
            value.StructuralToken = "city-16-structure";
            return value;
        }

        private static void True(bool value)
        {
            if (!value) throw new InvalidOperationException("Expected true.");
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(string.Format("Expected {0}, got {1}.", expected, actual));
        }

        private sealed class Fixture
        {
            internal FakePollSource Poll;
            internal FakeEvidenceSink Sink;
            internal InputUiSnapshot Final;
            internal San9ManualInputTraceRecorder Recorder;

            internal static Fixture Valid()
            {
                InputUiSnapshot start = StartSnapshot();
                InputUiSnapshot final = FinalSnapshot();
                FakePollSource poll = new FakePollSource(ValidSamples());
                FakeEvidenceSink sink = new FakeEvidenceSink();
                return new Fixture
                {
                    Poll = poll,
                    Sink = sink,
                    Final = final,
                    Recorder = new San9ManualInputTraceRecorder(
                        new FakeObservationSource(start, final),
                        poll,
                        new FakeClock(),
                        sink)
                };
            }

            internal ManualInputTraceResult Record()
            {
                return Recorder.Record(new ManualInputTraceOptions(16, Path.GetTempPath(), 100, 2, 100, 2, 1));
            }
        }

        private sealed class FakeObservationSource : IInputUiObservationSource
        {
            private readonly InputUiSnapshot start;
            private readonly InputUiSnapshot final;
            private int count;

            internal FakeObservationSource(InputUiSnapshot start, InputUiSnapshot final)
            {
                this.start = start;
                this.final = final;
            }

            public InputUiSnapshot Capture()
            {
                count++;
                return count == 1 ? start : final;
            }
        }

        private sealed class FakePollSource : IManualInputPollSource
        {
            private readonly Queue<ManualInputPollSample> samples = new Queue<ManualInputPollSample>();
            private ManualInputPollSample last;

            internal FakePollSource(IEnumerable<ManualInputPollSample> values)
            {
                SetSamples(values);
            }

            internal void SetSamples(IEnumerable<ManualInputPollSample> values)
            {
                samples.Clear();
                foreach (ManualInputPollSample value in values) samples.Enqueue(value.Clone());
                last = null;
            }

            public bool TryPoll(WindowBinding expected, out ManualInputPollSample sample, out string error)
            {
                if (samples.Count != 0) last = samples.Dequeue();
                if (last == null)
                {
                    sample = null;
                    error = "No synthetic sample.";
                    return false;
                }
                sample = last.Clone();
                error = string.Empty;
                return true;
            }
        }

        private sealed class FakeClock : IManualTraceClock
        {
            private long timestamp;
            private DateTimeOffset utc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
            public long Frequency { get { return 1000; } }
            public long Timestamp { get { return timestamp; } }
            public DateTimeOffset UtcNow { get { return utc; } }
            public void Delay(int milliseconds)
            {
                timestamp += milliseconds;
                utc = utc.AddMilliseconds(milliseconds);
            }
        }

        private sealed class FakeEvidenceSink : IManualTraceEvidenceSink
        {
            internal ManualTraceDocument Document;
            public string Write(ManualTraceDocument document, string outputDirectory)
            {
                Document = document;
                return "memory://" + document.SessionId.ToString("N") + ".json";
            }
        }
    }
}
