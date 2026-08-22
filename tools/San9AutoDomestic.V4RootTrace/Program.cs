using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.V4RootTrace
{
    internal static class Program
    {
        private const int DefaultDurationSeconds = 10;
        private const int MinimumDurationSeconds = 1;
        private const int MaximumDurationSeconds = 300;
        private const int DefaultIntervalMilliseconds = 100;
        private const int MinimumIntervalMilliseconds = 20;
        private const int MaximumIntervalMilliseconds = 5000;

        private static volatile bool cancelRequested;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);

            CommandLineOptions options;
            string parseError;
            if (!CommandLineOptions.TryParse(args, out options, out parseError))
            {
                Console.Error.WriteLine("ARGUMENT_ERROR: " + parseError);
                WriteUsage(Console.Error);
                return 2;
            }

            if (options.ShowHelp)
            {
                WriteUsage(Console.Out);
                return 0;
            }

            if (options.RunSelfTest)
            {
                return SyntheticRootTraceTests.RunAll();
            }

            return RunReadOnlyTrace(options);
        }

        private static int RunReadOnlyTrace(CommandLineOptions options)
        {
            San9Pk101Adapter adapter = new San9Pk101Adapter();
            ReadOnlyProcessConnection startupConnection = null;
            San9Pk101DiagnosticReport startupReport;
            RootTraceEnvironmentStamp startupStamp;
            string startupError;
            try
            {
                if (!TryDiagnoseCleanEnvironment(
                    adapter,
                    out startupReport,
                    out startupConnection,
                    out startupStamp,
                    out startupError))
                {
                    Console.Error.WriteLine("REFUSED: " + startupError);
                    WriteBlockingDiagnostics(startupReport);
                    return 1;
                }
            }
            finally
            {
                DisposeConnection(startupConnection);
            }

            RootTraceSessionBinding sessionBinding = new RootTraceSessionBinding(startupStamp);
            Console.WriteLine(
                "V4_ROOT_TRACE READ_ONLY NON_AUTHORIZING durationSeconds={0} intervalMs={1} pid={2} generation={3}",
                options.DurationSeconds,
                options.IntervalMilliseconds,
                sessionBinding.ProcessId,
                sessionBinding.ProcessCreationFileTimeUtc);

            cancelRequested = false;
            ConsoleCancelEventHandler handler = delegate(object sender, ConsoleCancelEventArgs eventArgs)
            {
                eventArgs.Cancel = true;
                cancelRequested = true;
            };
            Console.CancelKeyPress += handler;
            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                long durationMilliseconds = checked((long)options.DurationSeconds * 1000L);
                int sampleNumber = 0;

                while (!cancelRequested && stopwatch.ElapsedMilliseconds < durationMilliseconds)
                {
                    San9Pk101DiagnosticReport beforeReport;
                    ReadOnlyProcessConnection beforeConnection = null;
                    RootTraceEnvironmentStamp beforeStamp;
                    string gateError;
                    San9Pk101RootTraceSample sample;
                    try
                    {
                        if (!TryDiagnoseCleanEnvironment(
                            adapter,
                            out beforeReport,
                            out beforeConnection,
                            out beforeStamp,
                            out gateError))
                        {
                            Console.Error.WriteLine("STOP code=V4_PRE_SAMPLE_GATE_BLOCKED message=" + gateError);
                            WriteBlockingDiagnostics(beforeReport);
                            return 1;
                        }

                        if (!sessionBinding.TryMatch(beforeStamp, out gateError))
                        {
                            Console.Error.WriteLine("STOP code=V4_SESSION_BINDING_CHANGED message=" + gateError);
                            return 1;
                        }

                        sample = adapter.ReadRootTraceSample(beforeConnection, beforeReport);
                        if (!sample.ReadSucceeded)
                        {
                            Console.Error.WriteLine(
                                "STOP code={0} message={1}",
                                sample.FailureCode ?? "ROOT_TRACE_FAILED",
                                sample.FailureMessage ?? "Read-only root trace sample failed.");
                            return 1;
                        }

                        if (!sample.ProcessId.HasValue
                            || !sample.ProcessCreationFileTimeUtc.HasValue
                            || sample.ProcessId.Value != sessionBinding.ProcessId
                            || sample.ProcessCreationFileTimeUtc.Value != sessionBinding.ProcessCreationFileTimeUtc)
                        {
                            Console.Error.WriteLine("STOP code=SAMPLE_PROCESS_BINDING_CHANGED message=The sample is not bound to the V4 session PID/creation generation.");
                            return 1;
                        }
                    }
                    finally
                    {
                        DisposeConnection(beforeConnection);
                    }

                    San9Pk101DiagnosticReport afterReport;
                    ReadOnlyProcessConnection afterConnection = null;
                    RootTraceEnvironmentStamp afterStamp;
                    try
                    {
                        if (!TryDiagnoseCleanEnvironment(
                            adapter,
                            out afterReport,
                            out afterConnection,
                            out afterStamp,
                            out gateError))
                        {
                            Console.Error.WriteLine("STOP code=V4_POST_SAMPLE_GATE_BLOCKED message=" + gateError);
                            WriteBlockingDiagnostics(afterReport);
                            return 1;
                        }

                        if (!sessionBinding.TryMatch(afterStamp, out gateError)
                            || !beforeStamp.SameProcessGenerationAndModule(afterStamp))
                        {
                            Console.Error.WriteLine("STOP code=V4_SESSION_BINDING_CHANGED message=" + gateError);
                            return 1;
                        }
                    }
                    finally
                    {
                        DisposeConnection(afterConnection);
                    }

                    sampleNumber++;
                    WriteSample(sampleNumber, sample);

                    long remaining = durationMilliseconds - stopwatch.ElapsedMilliseconds;
                    if (remaining <= 0 || cancelRequested)
                    {
                        break;
                    }

                    int sleepMilliseconds = unchecked((int)Math.Min(
                        options.IntervalMilliseconds,
                        remaining));
                    Thread.Sleep(sleepMilliseconds);
                }

                Console.WriteLine(
                    "V4_ROOT_TRACE_STOP reason={0} samples={1}",
                    cancelRequested ? "ctrl_c" : "duration_elapsed",
                    sampleNumber);
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }

        private static bool TryDiagnoseCleanEnvironment(
            San9Pk101Adapter adapter,
            out San9Pk101DiagnosticReport report,
            out ReadOnlyProcessConnection connection,
            out RootTraceEnvironmentStamp stamp,
            out string error)
        {
            report = null;
            connection = null;
            stamp = null;
            try
            {
                report = adapter.Diagnose(out connection);
            }
            catch (Exception exception)
            {
                error = "The fresh diagnostic pass failed: " + exception.Message;
                return false;
            }

            if (!San9Pk101DiagnosticGate.TryValidateExactConflictFreeConnection(
                connection,
                report,
                out error))
            {
                return false;
            }

            return RootTraceEnvironmentStamp.TryCreate(report, out stamp, out error);
        }

        private static void DisposeConnection(ReadOnlyProcessConnection connection)
        {
            if (connection != null)
            {
                connection.Dispose();
            }
        }

        private static void WriteBlockingDiagnostics(San9Pk101DiagnosticReport report)
        {
            if (report == null)
            {
                return;
            }

            if (report.ConflictScan != null && report.ConflictScan.Conflicts != null)
            {
                foreach (ConflictDiagnostic conflict in report.ConflictScan.Conflicts)
                {
                    Console.Error.WriteLine(
                        "CONFLICT code={0} kind={1} name={2} pid={3} reason={4}",
                        conflict.Code ?? "-",
                        conflict.Kind,
                        conflict.Name ?? "-",
                        conflict.ProcessId.HasValue
                            ? conflict.ProcessId.Value.ToString(CultureInfo.InvariantCulture)
                            : "-",
                        conflict.Reason ?? "-");
                }
            }

            if (report.Issues != null)
            {
                foreach (DiagnosticIssue issue in report.Issues)
                {
                    if (issue.Severity == DiagnosticSeverity.Blocking)
                    {
                        Console.Error.WriteLine(
                            "ISSUE severity={0} code={1} message={2}",
                            issue.Severity,
                            issue.Code,
                            issue.Message);
                    }
                }
            }
        }

        private static void WriteSample(int sampleNumber, San9Pk101RootTraceSample sample)
        {
            Console.WriteLine(
                "SAMPLE n={0} utc={1} pid={2} generation={3} window=0x{4:X8} owner=0x{5:X8} scene=0x{6:X8} "
                    + "root=0x{7:X8} rootVptr=0x{8:X8} rootPending=0x{9:X8} "
                    + "domestic={10} domesticDepth={11} domesticCorps={12} domesticState={13} domesticTarget={14} "
                    + "leaf=0x{15:X8} leafVptr=0x{16:X8} leafTick=0x{17:X8} leafPending=0x{18:X8} depth={19} stableAB={20} stabilityAttempts={21} actionable={22} authorized={23}",
                sampleNumber,
                sample.CapturedUtc.ToString("O", CultureInfo.InvariantCulture),
                sample.ProcessId.Value,
                sample.ProcessCreationFileTimeUtc.Value,
                sample.WindowPointer,
                sample.OwnerPointer,
                sample.ScenePointer,
                sample.SchedulerRootPointer,
                sample.SchedulerRootVtable,
                sample.SchedulerRootPendingPointer,
                FormatOptionalHex(sample.DomesticControllerPointer),
                sample.DomesticControllerDepth.HasValue
                    ? sample.DomesticControllerDepth.Value.ToString(CultureInfo.InvariantCulture)
                    : "-",
                FormatOptionalHex(sample.DomesticControllerCorpsPointer),
                FormatOptionalHex(sample.DomesticControllerState),
                FormatOptionalHex(sample.DomesticControllerTargetPointer),
                sample.LeafPointer,
                sample.LeafVtable,
                sample.LeafTickFunctionPointer,
                sample.LeafPendingPointer,
                sample.DescendantDepth,
                sample.StructureDoubleReadStable,
                sample.StabilityAttemptCount,
                sample.IsActionable,
                sample.ExecutionAuthorized);
        }

        private static string FormatOptionalHex(uint? value)
        {
            return value.HasValue
                ? "0x" + value.Value.ToString("X8", CultureInfo.InvariantCulture)
                : "-";
        }

        private static void WriteUsage(System.IO.TextWriter writer)
        {
            writer.WriteLine("Usage:");
            writer.WriteLine("  San9AutoDomestic.V4RootTrace.exe [--duration-seconds 1..300] [--interval-ms 20..5000]");
            writer.WriteLine("  San9AutoDomestic.V4RootTrace.exe --self-test");
            writer.WriteLine("  San9AutoDomestic.V4RootTrace.exe --help");
            writer.WriteLine("The live mode writes samples to stdout only and never authorizes execution.");
        }

        private sealed class CommandLineOptions
        {
            internal int DurationSeconds;
            internal int IntervalMilliseconds;
            internal bool RunSelfTest;
            internal bool ShowHelp;

            internal static bool TryParse(
                string[] args,
                out CommandLineOptions options,
                out string error)
            {
                options = new CommandLineOptions
                {
                    DurationSeconds = DefaultDurationSeconds,
                    IntervalMilliseconds = DefaultIntervalMilliseconds
                };
                error = null;
                if (args == null)
                {
                    args = new string[0];
                }

                for (int index = 0; index < args.Length; index++)
                {
                    string argument = args[index];
                    if (string.Equals(argument, "--self-test", StringComparison.Ordinal))
                    {
                        options.RunSelfTest = true;
                    }
                    else if (string.Equals(argument, "--help", StringComparison.Ordinal)
                        || string.Equals(argument, "-h", StringComparison.Ordinal))
                    {
                        options.ShowHelp = true;
                    }
                    else if (string.Equals(argument, "--duration-seconds", StringComparison.Ordinal))
                    {
                        if (!TryReadBoundedInteger(
                            args,
                            ref index,
                            MinimumDurationSeconds,
                            MaximumDurationSeconds,
                            "duration-seconds",
                            out options.DurationSeconds,
                            out error))
                        {
                            return false;
                        }
                    }
                    else if (string.Equals(argument, "--interval-ms", StringComparison.Ordinal))
                    {
                        if (!TryReadBoundedInteger(
                            args,
                            ref index,
                            MinimumIntervalMilliseconds,
                            MaximumIntervalMilliseconds,
                            "interval-ms",
                            out options.IntervalMilliseconds,
                            out error))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        error = "Unknown argument '" + argument + "'.";
                        return false;
                    }
                }

                if (options.RunSelfTest && (args.Length != 1 || options.ShowHelp))
                {
                    error = "--self-test must be used by itself.";
                    return false;
                }

                if (options.ShowHelp && args.Length != 1)
                {
                    error = "--help must be used by itself.";
                    return false;
                }

                return true;
            }

            private static bool TryReadBoundedInteger(
                string[] args,
                ref int index,
                int minimum,
                int maximum,
                string name,
                out int value,
                out string error)
            {
                value = 0;
                error = null;
                if (index + 1 >= args.Length)
                {
                    error = "--" + name + " requires a value.";
                    return false;
                }

                index++;
                if (!int.TryParse(
                    args[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out value)
                    || value < minimum
                    || value > maximum)
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "--{0} must be an integer in {1}..{2}.",
                        name,
                        minimum,
                        maximum);
                    return false;
                }

                return true;
            }
        }
    }
}
