using System;
using System.Linq;
using System.Text;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.V1ReadDiagnostics
{
    internal static class Program
    {
        private static int Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            San9Pk101ReadReport report = new San9Pk101Adapter().ReadSnapshot();
            PrintBaseline(report.Baseline);

            Console.WriteLine(
                "READ success={0} stable={1} attempts={2} playerMainForce={3}",
                report.ReadSucceeded,
                report.RelevantFieldsWereStable,
                report.StableReadAttempts,
                Value(report.PlayerForceId));
            Console.WriteLine(
                "CONTEXT pid={0} createdFileTimeUtc={1} createdUtc={2} moduleBase={3} moduleSize={4} "
                    + "postChecked={5} completedUtc={6} stableSha256={7}",
                Value(report.ProcessId),
                report.ProcessCreationFileTimeUtc.HasValue
                    ? report.ProcessCreationFileTimeUtc.Value.ToString()
                    : "-",
                report.ProcessCreationTimeUtc.HasValue
                    ? report.ProcessCreationTimeUtc.Value.ToString("u")
                    : "-",
                report.MainModuleBaseAddress.HasValue
                    ? string.Format("0x{0:X8}", report.MainModuleBaseAddress.Value)
                    : "-",
                report.MainModuleSize.HasValue ? report.MainModuleSize.Value.ToString() : "-",
                report.ProcessIdentityRevalidatedAfterRead,
                report.ReadCompletedUtc.HasValue ? report.ReadCompletedUtc.Value.ToString("u") : "-",
                report.StableSummarySha256 ?? "-");
            Console.WriteLine(
                "TABLES forces={0} cities={1} persons={2} coreFacilities={3}",
                report.Forces.Length,
                report.Cities.Length,
                report.Persons.Length,
                report.Snapshot == null ? 0 : report.Snapshot.Facilities.Count);

            ForceReadRecord[] controlledCorps = report.Forces
                .Where(force => force.IsPlayerControlled && !force.IsBarbarian && force.LeaderPersonPointer != 0)
                .OrderBy(force => force.Id)
                .ToArray();
            Console.WriteLine("PLAYER_CONTROLLED_CORPS count={0}", controlledCorps.Length);
            foreach (ForceReadRecord force in controlledCorps)
            {
                Console.WriteLine(
                    "  corps={0} main={1} leader={2} money={3} flags=0x{4:X8}",
                    force.Id,
                    Value(force.MainForceId),
                    Value(force.LeaderPersonId),
                    force.Money,
                    force.RawFlags);
            }

            CityReadRecord[] directCities = report.Cities
                .Where(city => city.IsDirectlyControlled)
                .OrderBy(city => city.Id)
                .ToArray();
            Console.WriteLine("DIRECT_CITIES count={0}", directCities.Length);
            foreach (CityReadRecord city in directCities)
            {
                Console.WriteLine(
                    "  city={0} name={1} corps={2} owner={3} combat={4} raw7A=0x{5:X2} residents={6}/{7} excluded={8}",
                    city.Id,
                    city.Name,
                    Value(city.CorpsId),
                    Value(city.OwnerForceId),
                    city.IsInCombat.HasValue ? city.IsInCombat.Value.ToString() : "unknown",
                    city.RawCandidateFlags7A,
                    city.ObservedValidResidentOfficerCount,
                    city.DeclaredValidResidentOfficerCount,
                    city.ObservedExcludedResidentPersonCount);
                foreach (PersonReadRecord person in report.Persons
                    .Where(person => person.IsIncludedInCoreSnapshot && person.ResidentCityId == city.Id)
                    .OrderBy(person => person.Id))
                {
                    Console.WriteLine(
                        "    officer={0} name={1} rawSurname={2} rawGiven={3} prefixStripped={4} "
                            + "identity={5} L/M/I/P={6}/{7}/{8}/{9} canAct={10}",
                        person.Id,
                        person.Name,
                        person.RawSurnameHex,
                        person.RawGivenNameHex,
                        person.SurnameHexPrefixStripped,
                        person.RawIdentity,
                        person.EffectiveLeadership,
                        person.EffectiveMight,
                        person.EffectiveIntelligence,
                        person.EffectivePolitics,
                        person.CanAct.HasValue ? person.CanAct.Value.ToString() : "unknown");
                }
            }

            foreach (ReadInvariantDiagnostic issue in report.Issues)
            {
                Console.WriteLine(
                    "ISSUE severity={0} code={1} entity={2}:{3} message={4}",
                    issue.Severity,
                    issue.Code,
                    issue.EntityKind,
                    Value(issue.EntityId),
                    issue.Message);
            }

            return report.ReadSucceeded ? 0 : 1;
        }

        private static void PrintBaseline(San9Pk101DiagnosticReport baseline)
        {
            if (baseline == null)
            {
                Console.WriteLine("BASELINE unavailable");
                return;
            }

            Console.WriteLine(
                "BASELINE fileValid={0} discovery={1} pid={2} readConnected={3} "
                    + "executionAllowed={4} blockingConflicts={5}",
                baseline.FileValidation != null && baseline.FileValidation.IsValid,
                baseline.ProcessDiscovery == null ? ProcessDiscoveryStatus.Failed : baseline.ProcessDiscovery.Status,
                baseline.ProcessDiscovery == null ? "-" : Value(baseline.ProcessDiscovery.SelectedProcessId),
                baseline.ReadOnlyConnection != null && baseline.ReadOnlyConnection.Connected,
                baseline.ExecutionAllowed,
                baseline.ConflictScan != null && baseline.ConflictScan.HasBlockingConflicts);
            if (baseline.ConflictScan != null)
            {
                foreach (ConflictDiagnostic conflict in baseline.ConflictScan.Conflicts)
                {
                    Console.WriteLine(
                        "CONFLICT kind={0} name={1} pid={2} blocking={3} path={4}",
                        conflict.Kind,
                        conflict.Name,
                        Value(conflict.ProcessId),
                        conflict.IsBlocking,
                        conflict.Path ?? string.Empty);
                }
            }
        }

        private static string Value(int? value)
        {
            return value.HasValue ? value.Value.ToString() : "-";
        }
    }
}
