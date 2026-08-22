using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.V0SelfTest
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();
        private static int passed;

        private static int Main(string[] args)
        {
            bool liveOnly = args.Length == 1
                && string.Equals(args[0], "--live-only", StringComparison.Ordinal);
            bool includeLive = args.Length == 1
                && string.Equals(args[0], "--live", StringComparison.Ordinal);
            if (args.Length != 0 && !liveOnly && !includeLive)
            {
                Console.Error.WriteLine("Usage: San9AutoDomestic.V0SelfTest.exe [--live|--live-only]");
                return 2;
            }

            if (!liveOnly)
            {
                Run("x86 process", delegate { Assert(IntPtr.Size == 4, "Self-test must run as x86."); });
                Run(".NET Framework 4.8 metadata", VerifyTargetFramework);
                Run("exact target constants", VerifyConstants);
                Run("exact target validation", VerifyExactTarget);
                Run("wrong target path rejected", VerifyWrongPathRejected);
                Run("public connection surface is read-only", VerifyReadOnlySurface);
                Run("exact Easy runtime ticket is mandatory and non-authorizing", VerifyEasyRuntimeGateContract);
            }
            if (includeLive || liveOnly)
            {
                Console.WriteLine("Live read-only diagnostics explicitly enabled.");
                Run("live diagnostics invariants", VerifyLiveDiagnostics);
                Run("live V1 read invariants", VerifyLiveV1Read);
            }

            Console.WriteLine("Self-test summary: {0} passed, {1} failed.", passed, Failures.Count);
            foreach (string failure in Failures)
            {
                Console.Error.WriteLine("FAIL: {0}", failure);
            }

            return Failures.Count == 0 ? 0 : 1;
        }

        private static void VerifyTargetFramework()
        {
            Assembly assembly = typeof(San9Pk101Adapter).Assembly;
            TargetFrameworkAttribute attribute = assembly
                .GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
                .Cast<TargetFrameworkAttribute>()
                .Single();
            Assert(
                string.Equals(attribute.FrameworkName, ".NETFramework,Version=v4.8", StringComparison.Ordinal),
                "Adapter target metadata is " + attribute.FrameworkName + ".");
        }

        private static void VerifyConstants()
        {
            Assert(San9Pk101Target.ExpectedExecutableSize == 2636800L, "Unexpected expected size.");
            Assert(San9Pk101Target.ExpectedFileVersion.Equals(new Version(1, 0, 1, 0)), "Unexpected version.");
            Assert(San9Pk101Target.ExpectedPeMachine == 0x014c, "Unexpected PE machine.");
            Assert(
                San9Pk101Target.ExpectedSha256.Length == 64,
                "Expected SHA-256 must contain 64 hexadecimal characters.");
        }

        private static void VerifyExactTarget()
        {
            Assert(File.Exists(San9Pk101Target.ExpectedExecutablePath), "The PRD target executable is missing.");
            FileValidationDiagnostic validation = new San9Pk101TargetValidator().Validate();
            Assert(validation.IsValid, JoinIssues(validation.Issues));
        }

        private static void VerifyWrongPathRejected()
        {
            string wrongPath = Path.Combine(
                Path.GetTempPath(),
                "San9AutoDomestic-does-not-exist",
                "San9PK.exe");
            FileValidationDiagnostic validation = new San9Pk101TargetValidator().Validate(wrongPath);
            Assert(!validation.IsValid, "A non-exact target path was accepted.");
            Assert(
                validation.Issues.Any(issue => issue.Code == "TARGET_PATH_MISMATCH"),
                "Path mismatch was not diagnosed.");
        }

        private static void VerifyReadOnlySurface()
        {
            string[] forbiddenFragments = { "Write", "Alloc", "Protect", "Thread", "Inject" };
            MethodInfo[] publicMethods = typeof(ReadOnlyProcessConnection).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (MethodInfo method in publicMethods)
            {
                Assert(
                    !forbiddenFragments.Any(fragment => method.Name.IndexOf(
                        fragment,
                        StringComparison.OrdinalIgnoreCase) >= 0),
                    "Forbidden public capability: " + method.Name);
            }

            Type nativeMethods = typeof(San9Pk101Adapter).Assembly.GetType(
                "San9AutoDomestic.Adapter.San9Pk101.NativeMethods",
                true);
            string[] forbiddenNativeMethods =
            {
                "WriteProcessMemory",
                "VirtualAllocEx",
                "VirtualProtectEx",
                "CreateRemoteThread"
            };
            MethodInfo[] allNativeMethods = nativeMethods.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (string forbidden in forbiddenNativeMethods)
            {
                Assert(
                    !allNativeMethods.Any(method => string.Equals(
                        method.Name,
                        forbidden,
                        StringComparison.Ordinal)),
                    "Forbidden native import exists: " + forbidden);
            }
        }

        private static void VerifyLiveDiagnostics()
        {
            San9Pk101Adapter adapter = new San9Pk101Adapter();
            ReadOnlyProcessConnection connection = null;
            try
            {
                San9Pk101DiagnosticReport report = adapter.Diagnose(out connection);
                Assert(report.FileValidation.IsValid, "Exact target validation failed during diagnostics.");

                if (report.ProcessDiscovery.Status == ProcessDiscoveryStatus.Unique)
                {
                    Assert(report.ReadOnlyConnection.Connected, "Unique target did not get a read-only connection.");
                    Assert(connection != null && connection.IsConnected, "Live read-only handle was not returned.");
                }
                else
                {
                    Assert(!report.ExecutionAllowed, "Execution gate opened without a unique process.");
                }

                Process[] easyProcesses = Process.GetProcessesByName("San9PKEasy");
                bool easyIsRunning = easyProcesses.Length > 0;
                foreach (Process process in easyProcesses)
                {
                    process.Dispose();
                }

                if (easyIsRunning)
                {
                    Assert(
                        report.ConflictScan.Conflicts.Any(conflict =>
                            conflict.Kind == ConflictKind.KnownProcess
                            && string.Equals(conflict.Name, "San9PKEasy.exe", StringComparison.OrdinalIgnoreCase)),
                        "Running San9PKEasy was not detected.");
                }

                bool easyModuleDetected = report.ConflictScan.Conflicts.Any(conflict =>
                    conflict.Kind == ConflictKind.KnownModule
                    && string.Equals(conflict.Name, "Easy.dll", StringComparison.OrdinalIgnoreCase));
                bool compatibleEasyProcess = report.ConflictScan.Conflicts.Any(conflict =>
                    string.Equals(conflict.Code, "COMPATIBLE_EASY_PROCESS", StringComparison.Ordinal)
                    && !conflict.IsBlocking);
                bool compatibleEasyModule = report.ConflictScan.Conflicts.Any(conflict =>
                    string.Equals(conflict.Code, "COMPATIBLE_EASY_MODULE", StringComparison.Ordinal)
                    && !conflict.IsBlocking);
                if (compatibleEasyProcess && compatibleEasyModule
                    && !report.ConflictScan.HasBlockingConflicts
                    && report.EasyCompatibility != null
                    && report.EasyCompatibility.State == EasyRuntimeCompatibilityState.Installed
                    && report.EasyCompatibility.CompatibleForFutureBridge
                    && !report.EasyCompatibility.RestartRequired)
                {
                    Assert(report.ExecutionAllowed, "The exact installed Easy runtime compatibility ticket was blocked.");
                    Assert(!report.EasyCompatibility.ExecutionAuthorized
                            && report.EasyCompatibility.Ticket != null
                            && !report.EasyCompatibility.Ticket.ExecutionAuthorized,
                        "P0 compatibility observation authorized native execution.");
                }
                else
                {
                    Assert(!report.ExecutionAllowed, "A missing, unstable, or non-compatible Easy runtime opened the gate.");
                }
            }
            finally
            {
                if (connection != null)
                {
                    connection.Dispose();
                }
            }
        }

        private static void VerifyEasyRuntimeGateContract()
        {
            ConflictScanDiagnostic absent = new ConflictScanDiagnostic
            {
                ProcessScanSucceeded = true,
                ModuleScanAttempted = true,
                ModuleScanSucceeded = true,
                Conflicts = new ConflictDiagnostic[0]
            };
            Assert(!San9Pk101ConflictDetector.ContainsOnlyExactCompatibleEasy(absent),
                "No-Easy was accepted as the exact compatibility profile.");

            EasyRuntimeCompatibilityReport report = new EasyRuntimeCompatibilityReport();
            Assert(!report.ExecutionAuthorized,
                "A newly created P0 report authorized native execution.");
            Assert(string.Equals(report.ManifestSha256,
                    "72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE",
                    StringComparison.Ordinal),
                "The frozen manifest identity is not exposed by the typed report.");

            ConflictScanDiagnostic exactPair = new ConflictScanDiagnostic
            {
                ProcessScanSucceeded = true,
                ModuleScanAttempted = true,
                ModuleScanSucceeded = true,
                Conflicts = new[]
                {
                    new ConflictDiagnostic
                    {
                        Kind = ConflictKind.KnownProcess,
                        Code = "COMPATIBLE_EASY_PROCESS",
                        Name = "San9PKEasy.exe",
                        ProcessId = 4342,
                        ProcessCreationFileTimeUtc = 222,
                        Path = San9Pk101ConflictDetector.CompatibleEasyProcessPath,
                        FileSize = San9Pk101ConflictDetector.CompatibleEasyProcessSize,
                        FileSha256 = San9Pk101ConflictDetector.CompatibleEasyProcessSha256
                    },
                    new ConflictDiagnostic
                    {
                        Kind = ConflictKind.KnownModule,
                        Code = "COMPATIBLE_EASY_MODULE",
                        Name = "Easy.dll",
                        ProcessId = 4242,
                        Path = San9Pk101ConflictDetector.CompatibleEasyModulePath,
                        FileSize = San9Pk101ConflictDetector.CompatibleEasyModuleFileSize,
                        FileSha256 = San9Pk101ConflictDetector.CompatibleEasyModuleSha256,
                        ModuleBaseAddress = 0x10000000,
                        ModuleImageSize = San9Pk101ConflictDetector.CompatibleEasyModuleImageSize
                    }
                }
            };
            San9Pk101DiagnosticReport baseline = new San9Pk101DiagnosticReport
            {
                FileValidation = new FileValidationDiagnostic { IsValid = true },
                ProcessDiscovery = new ProcessDiscoveryDiagnostic
                {
                    Status = ProcessDiscoveryStatus.Unique,
                    SelectedProcessId = 4242,
                    SelectedImagePath = San9Pk101Target.ExpectedExecutablePath,
                    Candidates = new[]
                    {
                        new ProcessCandidateDiagnostic
                        {
                            ProcessId = 4242,
                            WindowHandles = new[] { 0x9001L }
                        }
                    }
                },
                ReadOnlyConnection = new ReadOnlyConnectionDiagnostic
                {
                    Connected = true,
                    ProcessId = 4242,
                    ImagePath = San9Pk101Target.ExpectedExecutablePath,
                    ProcessCreationFileTimeUtc = 111,
                    MainModuleBaseAddress = San9Pk101Target.ExpectedImageBase,
                    MainModuleSize = San9Pk101Target.ExpectedSizeOfImage
                },
                ConflictScan = exactPair,
                EasyCompatibility = new EasyRuntimeCompatibilityReport
                {
                    State = EasyRuntimeCompatibilityState.Installed,
                    StableSnapshot = true,
                    CompatibleForFutureBridge = true,
                    InstalledRedirectCount = 32,
                    Ticket = new EasyCompatibilityTicket
                    {
                        GameProcessId = 4242,
                        GameProcessCreationFileTimeUtc = 111,
                        GameWindowHandle = 0x9001,
                        EasyLoaderProcessId = 4342,
                        EasyLoaderCreationFileTimeUtc = 222,
                        EasyLoaderPath = San9Pk101ConflictDetector.CompatibleEasyProcessPath,
                        EasyLoaderFileSize = San9Pk101ConflictDetector.CompatibleEasyProcessSize,
                        EasyLoaderFileSha256 = San9Pk101ConflictDetector.CompatibleEasyProcessSha256,
                        EasyModuleBaseAddress = 0x10000000,
                        EasyModuleImageSize = San9Pk101ConflictDetector.CompatibleEasyModuleImageSize,
                        EasyModulePath = San9Pk101ConflictDetector.CompatibleEasyModulePath,
                        EasyModuleFileSize = San9Pk101ConflictDetector.CompatibleEasyModuleFileSize,
                        EasyModuleFileSha256 = San9Pk101ConflictDetector.CompatibleEasyModuleSha256,
                        ManifestSha256 = "72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE",
                        HookEpochId = new string('A', 64),
                        SnapshotFingerprintSha256 = new string('B', 64)
                    }
                }
            };
            Assert(San9Pk101DiagnosticGate.ComputeExecutionAllowed(baseline, new DiagnosticIssue[0]),
                "The exact installed runtime ticket did not participate in the Diagnose gate.");
            EasyCompatibilityTicket ticket = baseline.EasyCompatibility.Ticket;
            ticket.GameWindowHandle++;
            Assert(!San9Pk101DiagnosticGate.ComputeExecutionAllowed(baseline, new DiagnosticIssue[0]),
                "A ticket HWND inconsistent with the baseline opened the gate.");
            ticket.GameWindowHandle--;
            ticket.EasyLoaderCreationFileTimeUtc++;
            Assert(!San9Pk101DiagnosticGate.ComputeExecutionAllowed(baseline, new DiagnosticIssue[0]),
                "A ticket loader generation inconsistent with the scan opened the gate.");
            ticket.EasyLoaderCreationFileTimeUtc--;
            ticket.EasyModuleBaseAddress++;
            Assert(!San9Pk101DiagnosticGate.ComputeExecutionAllowed(baseline, new DiagnosticIssue[0]),
                "A ticket module base inconsistent with the scan opened the gate.");
            ticket.EasyModuleBaseAddress--;
            ticket.ManifestSha256 = new string('C', 64);
            Assert(!San9Pk101DiagnosticGate.ComputeExecutionAllowed(baseline, new DiagnosticIssue[0]),
                "A ticket with the wrong frozen manifest identity opened the gate.");
            ticket.ManifestSha256 = report.ManifestSha256;
            baseline.EasyCompatibility.InstalledRedirectCount = 31;
            Assert(!San9Pk101DiagnosticGate.ComputeExecutionAllowed(baseline, new DiagnosticIssue[0]),
                "A report with an incomplete redirect count opened the gate.");
            baseline.EasyCompatibility.InstalledRedirectCount = 32;
            Assert(San9Pk101DiagnosticGate.ComputeExecutionAllowed(baseline, new DiagnosticIssue[0]),
                "The gate did not recover after restoring every exact ticket/report field.");
            Assert(!ticket.IsSecurityCapability && !ticket.ExecutionAuthorized,
                "The diagnostic ticket claimed an execution/security capability.");
            Assert(!typeof(EasyCompatibilityTicket).GetProperties().Any(property =>
                    property.Name.IndexOf("Nonce", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.Name.IndexOf("Hmac", StringComparison.OrdinalIgnoreCase) >= 0
                    || string.Equals(property.Name, "Mac", StringComparison.OrdinalIgnoreCase)),
                "The diagnostic ticket incorrectly claims nonce/MAC authentication.");
            baseline.EasyCompatibility = report;
            Assert(!San9Pk101DiagnosticGate.ComputeExecutionAllowed(baseline, new DiagnosticIssue[0]),
                "The Diagnose gate opened after the runtime ticket was removed.");
        }

        private static void VerifyLiveV1Read()
        {
            San9Pk101Adapter adapter = new San9Pk101Adapter();
            San9Pk101ReadReport first = adapter.ReadSnapshot();
            if (first.Baseline.ProcessDiscovery.Status != ProcessDiscoveryStatus.Unique)
            {
                Assert(!first.ReadSucceeded, "V1 read succeeded without a unique exact target.");
                return;
            }

            VerifyV1Report(first);

            // Each public read performs two complete reads of every relevant field and only
            // accepts an internally consistent pair. A second public read verifies this gate
            // remains repeatable without hard-coding the currently loaded force or cities.
            San9Pk101ReadReport second = adapter.ReadSnapshot();
            VerifyV1Report(second);
        }

        private static void VerifyV1Report(San9Pk101ReadReport report)
        {
            Assert(report.ReadSucceeded, JoinReadIssues(report.Issues));
            Assert(report.RelevantFieldsWereStable, "The relevant-field double-read was not stable.");
            Assert(report.StableReadAttempts >= 1 && report.StableReadAttempts <= 3, "Invalid stable-read attempt count.");
            Assert(report.Forces.Length == 50, "Force table length is not 50.");
            Assert(report.Cities.Length == 50, "City table length is not 50.");
            Assert(report.Persons.Length == 850, "Person table length is not 850.");
            Assert(report.PlayerForceId.HasValue, "Player main force was not resolved.");
            Assert(report.ProcessId.HasValue, "V1 report did not bind a PID.");
            Assert(report.ProcessCreationFileTimeUtc.HasValue, "V1 report did not bind a process generation.");
            Assert(report.MainModuleBaseAddress == San9Pk101Target.ExpectedImageBase,
                "Unexpected live main-module base.");
            Assert(report.MainModuleSize == San9Pk101Target.ExpectedSizeOfImage,
                "Unexpected live SizeOfImage.");
            Assert(report.ProcessIdentityRevalidatedAfterRead,
                "Process identity was not revalidated after the read.");
            Assert(report.ReadCompletedUtc.HasValue, "Read completion timestamp is absent.");
            Assert(report.StableSummarySha256 != null && report.StableSummarySha256.Length == 64,
                "Stable structural summary SHA-256 is absent.");

            ForceReadRecord[] controlledCorps = report.Forces
                .Where(force => force.IsValidCorps
                    && force.IsPlayerControlled
                    && !force.IsBarbarian)
                .ToArray();
            Assert(controlledCorps.Length != 0, "No populated non-barbarian player-controlled corps was found.");
            int[] controlledMainForces = controlledCorps
                .Select(force => force.MainForceId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .Distinct()
                .ToArray();
            Assert(controlledMainForces.Length == 1, "Player-controlled corps do not resolve to one main force.");
            Assert(controlledMainForces[0] == report.PlayerForceId.Value, "Resolved player main force differs from corps ownership.");

            foreach (CityReadRecord city in report.Cities)
            {
                Assert(city.SelfPointer == city.Address, "City self pointer mismatch: " + city.Id);
                Assert(!city.IsInCombat.HasValue,
                    "Unverified city+0x7A semantics were promoted to combat state: " + city.Id);
                Assert(
                    city.DeclaredValidResidentOfficerCount == city.ObservedValidResidentOfficerCount,
                    "Resident count mismatch at city " + city.Id + ".");
                Assert(
                    city.ResidentOfficerIds.Length == city.ObservedValidResidentOfficerCount,
                    "Resident id list mismatch at city " + city.Id + ".");

                bool expectedDirect = false;
                if (city.CorpsId.HasValue)
                {
                    ForceReadRecord corps = report.Forces[city.CorpsId.Value];
                    expectedDirect = corps.IsValidCorps
                        && !corps.IsBarbarian
                        && corps.IsPlayerControlled
                        && corps.MainForceId.HasValue
                        && corps.MainForceId.Value == report.PlayerForceId.Value;
                }

                Assert(city.IsDirectlyControlled == expectedDirect, "Direct-control mismatch at city " + city.Id + ".");
                if (city.IsDirectlyControlled)
                {
                    Assert(city.OwnerForceId == report.PlayerForceId, "Direct city owner mismatch at city " + city.Id + ".");
                }
            }

            foreach (PersonReadRecord person in report.Persons)
            {
                Assert(person.RecordId == person.Id, "Person record ID mismatch: " + person.Id);
            }

            Assert(report.Snapshot != null, "Core snapshot is missing.");
            Assert(report.Snapshot.PlayerForceId == report.PlayerForceId.Value, "Core player force mismatch.");
            Assert(report.Snapshot.Context.Readiness == SnapshotReadiness.StructureOnly,
                "V1 snapshot must remain structure-only.");
            Assert(report.Snapshot.Context.ProcessId == report.ProcessId,
                "Core structure-only context lost the process ID.");
            Assert(report.Snapshot.Context.ProcessStartUtcTicks.HasValue,
                "Core structure-only context lost the process generation timestamp.");
            Assert(report.Snapshot.Facilities.Count == 50, "Core snapshot does not contain all 50 cities.");
            Assert(report.CoreCanActValuesAreConservativeFalse, "CanAct projection policy was not declared.");
            Assert(report.CoreCommandListsAreEmpty, "Empty-command projection policy was not declared.");

            DomesticCommand[] commands =
            {
                DomesticCommand.Patrol,
                DomesticCommand.Commerce,
                DomesticCommand.Cultivate,
                DomesticCommand.Train,
                DomesticCommand.Repair
            };
            foreach (FacilitySnapshot facility in report.Snapshot.Facilities)
            {
                Assert(facility.Officers.All(officer => !officer.CanAct), "Core CanAct must remain conservative false.");
                foreach (DomesticCommand command in commands)
                {
                    NativeCommandSnapshot commandSnapshot;
                    Assert(
                        !facility.TryGetCommand(command, out commandSnapshot),
                        "V1-read guessed command state for city " + facility.Id + ".");
                }
            }

            Assert(
                !report.Issues.Any(issue => issue.Severity == DiagnosticSeverity.Blocking),
                JoinReadIssues(report.Issues));
            if (report.Baseline.ConflictScan.HasBlockingConflicts)
            {
                Assert(report.ConflictsIgnoredForReadOnlyScan, "Read-only scan did not record ignored execution conflicts.");
                Assert(!report.Baseline.ExecutionAllowed, "Execution gate stayed open with blocking conflicts.");
                Assert(report.ReadSucceeded, "Execution conflicts blocked the read-only snapshot.");
            }
        }

        private static string JoinIssues(DiagnosticIssue[] issues)
        {
            return string.Join(" | ", issues.Select(issue => issue.ToString()).ToArray());
        }

        private static string JoinReadIssues(ReadInvariantDiagnostic[] issues)
        {
            return string.Join(
                " | ",
                issues.Select(issue => string.Format(
                    "[{0}] {1} {2}:{3} {4}",
                    issue.Severity,
                    issue.Code,
                    issue.EntityKind,
                    issue.EntityId.HasValue ? issue.EntityId.Value.ToString() : "-",
                    issue.Message)).ToArray());
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Console.WriteLine("PASS: {0}", name);
            }
            catch (Exception exception)
            {
                Failures.Add(name + " - " + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
