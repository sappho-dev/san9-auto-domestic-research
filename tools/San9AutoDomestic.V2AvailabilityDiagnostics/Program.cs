using System;
using System.Linq;
using System.Text;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.V2AvailabilityDiagnostics
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length > 0 && string.Equals(args[0], "--ui-observe", StringComparison.OrdinalIgnoreCase))
                return RunUiObservation(args);
            if (args.Length != 0)
            {
                WriteUsage();
                return 2;
            }
            San9Pk101AvailabilityReport report = new San9Pk101Adapter().ReadAvailability();
            Console.WriteLine("V2 dataRead={0} blocked={1} rawStable={2} codeExact={3} structureRevalidated={4} planningReady={5}",
                report.DataReadSucceeded, report.ObservationBlocked, report.RawFieldsWereStable,
                report.CodeAnchorsWereStableAndExact, report.StructureWasRevalidated, report.PlanningReady);
            if (report.ContextToken != null)
            {
                Console.WriteLine("CONTEXT currentCity=0x{0:X8} raw2480=0x{1:X8} phase={2} time=0x{3:X8} strategicCandidate={4} token={5}",
                    report.ContextToken.CurrentCityPointerCandidate, report.ContextToken.RawValue2480,
                    report.ContextToken.PhaseCandidate, report.ContextToken.StrategicTimeCounterCandidate,
                    report.ContextToken.IsStrategicInputPhaseCandidate, report.ContextToken.TokenSha256);
            }
            Console.WriteLine("ANCHORS exact={0}/{1}", report.CodeAnchors.Count(a => a.DiskMatchesExpected && a.LiveWasStable && a.LiveMatchesDisk), report.CodeAnchors.Length);
            foreach (CodeAnchorObservation anchor in report.CodeAnchors.Where(a => !a.DiskMatchesExpected || !a.LiveWasStable || !a.LiveMatchesDisk || !string.IsNullOrEmpty(a.Error)))
            {
                Console.WriteLine("  ANCHOR name={0} address=0x{1:X8} diskExpected={2} liveStable={3} liveDisk={4} error={5}",
                    anchor.Name, anchor.Address, anchor.DiskMatchesExpected, anchor.LiveWasStable,
                    anchor.LiveMatchesDisk, anchor.Error ?? "-");
            }
            foreach (CityAvailabilityObservation city in report.Cities)
            {
                Console.WriteLine("CITY id={0} name={1} corps={2} money={3} direct={4}", city.CityId, city.CityName,
                    city.CorpsId.HasValue ? city.CorpsId.Value.ToString() : "-", city.CorpsMoney, city.IsDirectlyControlled);
                foreach (CommandAvailabilityObservation command in city.Commands)
                {
                    string rankedHead = string.Join(",", command.RankedCandidates.Take(5).Select(p => p.PersonId + ":" + p.Name + ":" + p.Score).ToArray());
                    string failed = string.Join(",", command.Conditions.Where(c => c.Passed == false).Select(c => c.Code).ToArray());
                    Console.WriteLine("  COMMAND {0} ready={1} knownStaticSubset={2} costPerOfficer={3} rankedHead=[{4}] failed=[{5}]",
                        command.Command, command.ReadyCandidateCount, command.KnownStaticSubsetWouldPass,
                        command.ProvisionalCostPerOfficer, rankedHead, failed);
                }
            }
            foreach (AvailabilityIssue issue in report.Issues)
            {
                Console.WriteLine("ISSUE severity={0} code={1} message={2}", issue.Severity, issue.Code, issue.Message);
            }
            return report.DataReadSucceeded ? 0 : 1;
        }

        private static int RunUiObservation(string[] args)
        {
            int samples = 1;
            int intervalMilliseconds = 0;
            if (args.Length > 3
                || (args.Length >= 2 && !int.TryParse(args[1], out samples))
                || (args.Length >= 3 && !int.TryParse(args[2], out intervalMilliseconds))
                || samples < 1 || samples > 120
                || intervalMilliseconds < 0 || intervalMilliseconds > 10000)
            {
                WriteUsage();
                return 2;
            }

            San9Pk101Adapter adapter = new San9Pk101Adapter();
            int successCount = 0;
            for (int sample = 1; sample <= samples; sample++)
            {
                San9Pk101UiObservationReport report = adapter.ReadUiObservation();
                Console.WriteLine(
                    "UI_SAMPLE index={0}/{1} success={2} stable={3} attempts={4} stableMs={5} layer={6} pid={7} hwnd={8}",
                    sample,
                    samples,
                    report.ReadSucceeded,
                    report.StableAbc,
                    report.StabilityAttemptCount,
                    report.StableDurationMilliseconds,
                    report.Layer,
                    report.ProcessId.HasValue ? report.ProcessId.Value.ToString() : "-",
                    report.MainWindowHandle.HasValue ? string.Format("0x{0:X8}", report.MainWindowHandle.Value) : "-");
                Console.WriteLine(
                    "UI_GATES planningReady={0} nativeVerified={1} actionable={2} commitAuthorized={3}",
                    report.PlanningReady,
                    report.VerifiedNativeCapability,
                    report.IsActionable,
                    report.CommitAuthorized);
                foreach (UiObservationField field in report.Fields)
                {
                    Console.WriteLine(
                        "UI_FIELD name={0} state={1} value=[{2}] evidence={3}",
                        field.Name,
                        field.State,
                        field.Value,
                        field.Evidence);
                }
                Console.WriteLine("UI_CANDIDATES [{0}]", string.Join(",", report.CandidateOfficerIds.Select(item => item.ToString()).ToArray()));
                Console.WriteLine("UI_SELECTED [{0}]", string.Join(",", report.SelectedOfficerIds.Select(item => item.ToString()).ToArray()));
                foreach (AvailabilityIssue issue in report.Issues)
                    Console.WriteLine("UI_ISSUE severity={0} code={1} message={2}", issue.Severity, issue.Code, issue.Message);
                if (report.ReadSucceeded) successCount++;
                if (sample < samples && intervalMilliseconds > 0) Thread.Sleep(intervalMilliseconds);
            }
            return successCount == samples ? 0 : 1;
        }

        private static void WriteUsage()
        {
            Console.Error.WriteLine("Usage: San9AutoDomestic.V2AvailabilityDiagnostics.exe");
            Console.Error.WriteLine("   or: San9AutoDomestic.V2AvailabilityDiagnostics.exe --ui-observe [samples 1..120] [interval-ms 0..10000]");
        }
    }
}
