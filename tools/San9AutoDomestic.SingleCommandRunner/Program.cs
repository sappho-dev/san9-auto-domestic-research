using System;
using System.IO;
using System.Linq;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Input.Win32;

namespace San9AutoDomestic.SingleCommandRunner
{
    internal static class Program
    {
        private const string Confirmation = "I_AUTHORIZE_ONE_SINGLE_CITY_COMMAND";

        private static int Main(string[] args)
        {
            DomesticCommandKind command;
            string evidence;
            if (!TryParse(args, out command, out evidence))
            {
                Console.WriteLine("Development runner; it sends no input without the exact confirmation token.");
                Console.WriteLine("Usage: --command Patrol|Commerce|Cultivate|Repair|Train --evidence <directory> --confirm " + Confirmation);
                Console.WriteLine("Precondition: the target city's domestic command menu is already open.");
                return 2;
            }

            SingleCityDomesticExecutionRequest request = new SingleCityDomesticExecutionRequest(
                Guid.NewGuid(), command, evidence);
            SingleCityDomesticExecutionResult result = new SingleCityDomesticExecutor().Execute(
                request, new DomesticExecutionStopSignal());
            Console.WriteLine("status={0}; code={1}; city={2}; actions={3}; commitAttempted={4}",
                result.Status,
                result.Code,
                result.CityId.HasValue ? result.CityId.Value.ToString() : "none",
                result.InputActionCount,
                result.CommitClickAttempted);
            Console.WriteLine("money={0}->{1}; ready={2}->{3}; officers=[{4}]",
                Value(result.MoneyBefore), Value(result.MoneyAfter), Value(result.ReadyBefore), Value(result.ReadyAfter),
                string.Join(",", result.SelectedOfficerIds.Select(item => item.ToString()).ToArray()));
            Console.WriteLine(result.Message);
            Console.WriteLine("evidence=" + request.EvidenceDirectory);
            return result.Status == DomesticExecutionStatus.Completed
                || result.Status == DomesticExecutionStatus.SkippedUnavailable
                || result.Status == DomesticExecutionStatus.SkippedFewerThanFive ? 0
                : result.Status == DomesticExecutionStatus.RejectedBeforeInput ? 2 : 3;
        }

        private static bool TryParse(string[] args, out DomesticCommandKind command, out string evidence)
        {
            command = DomesticCommandKind.Patrol;
            evidence = string.Empty;
            if (args == null) return false;
            string commandText = ValueAfter(args, "--command");
            string confirmation = ValueAfter(args, "--confirm");
            evidence = ValueAfter(args, "--evidence") ?? string.Empty;
            return string.Equals(confirmation, Confirmation, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(evidence)
                && Enum.TryParse(commandText, false, out command)
                && Enum.IsDefined(typeof(DomesticCommandKind), command);
        }

        private static string ValueAfter(string[] args, string name)
        {
            for (int index = 0; index + 1 < args.Length; index++)
                if (string.Equals(args[index], name, StringComparison.Ordinal)) return args[index + 1];
            return null;
        }

        private static string Value(int? value)
        {
            return value.HasValue ? value.Value.ToString() : "none";
        }
    }
}
