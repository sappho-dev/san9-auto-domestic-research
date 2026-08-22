using System;
using System.Collections.Generic;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.V4RootTrace
{
    internal static class SyntheticRootTraceTests
    {
        private static readonly List<string> Failures = new List<string>();
        private static int passed;

        internal static int RunAll()
        {
            passed = 0;
            Failures.Clear();
            Run("normal root and descendant chain", VerifyNormalChain);
            Run("ordinary descendants do not expose domestic fields", VerifyOrdinaryDescendantHasNoDomesticFields);
            Run("multiple domestic controllers are rejected", VerifyMultipleDomesticControllersRejected);
            Run("child cycle is rejected", VerifyCycleRejected);
            Run("descendant depth is bounded", VerifyDepthRejected);
            Run("scheduler root vptr mismatch is rejected", VerifyRootVptrRejected);
            Run("process generation change is rejected", VerifyGenerationChangeRejected);
            Run("read failure stops the sample", VerifyReadFailureRejected);
            Run("descendant vptr outside module is rejected", VerifyDescendantVptrRejected);
            Run("descendant tick outside module is rejected", VerifyDescendantTickRejected);
            Run("one mutation between A/B passes is retried", VerifyOneTimeMutationRetried);
            Run("continuous A/B mutation is rejected", VerifyContinuousMutationRejected);
            Run("fake-window blocking issue closes every gate", VerifyBlockingIssueClosesGate);
            Run("session binding rejects a changed generation", VerifySessionGenerationChangeRejected);

            Console.WriteLine(
                "V4 root trace synthetic summary: {0} passed, {1} failed.",
                passed,
                Failures.Count);
            foreach (string failure in Failures)
            {
                Console.Error.WriteLine("FAIL: " + failure);
            }

            return Failures.Count == 0 ? 0 : 1;
        }

        private static void VerifyNormalChain()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            San9Pk101RootTraceSample sample = Read(memory);
            Assert(sample.ReadSucceeded, FailureText(sample));
            Assert(sample.ProcessId == SyntheticMemory.SyntheticProcessId, "PID binding was not preserved.");
            Assert(sample.ProcessCreationFileTimeUtc == memory.InitialIdentity.CreationFileTimeUtc,
                "Creation generation was not preserved.");
            Assert(sample.SchedulerRootPointer == memory.RootAddress, "Wrong scheduler root.");
            Assert(sample.SchedulerRootVtable == San9Pk101RootTraceReader.ExpectedSchedulerRootVtable,
                "Wrong scheduler root vptr.");
            Assert(sample.SchedulerRootPendingPointer == 0x0200a000, "Wrong root pending value.");
            Assert(sample.DomesticControllerPointer == memory.LeafAddress,
                "The domestic controller descendant was not identified.");
            Assert(sample.DomesticControllerDepth == 2, "Wrong domestic controller depth.");
            Assert(sample.DomesticControllerCorpsPointer == 0x0200b000, "Wrong domestic controller corps value.");
            Assert(sample.DomesticControllerState == 0x000003ea, "Wrong domestic controller state value.");
            Assert(sample.DomesticControllerTargetPointer == 0x0200c000, "Wrong domestic controller target value.");
            Assert(sample.LeafPointer == memory.LeafAddress, "Wrong leaf.");
            Assert(sample.LeafVtable == memory.LeafVtable, "Wrong leaf vptr.");
            Assert(sample.LeafTickFunctionPointer == memory.LeafTick, "Wrong leaf tick function.");
            Assert(sample.LeafPendingPointer == 0x0200d000, "Wrong leaf pending value.");
            Assert(sample.DescendantDepth == 2, "Wrong descendant depth.");
            Assert(sample.StructureDoubleReadStable && sample.StabilityAttemptCount == 1,
                "The normal sample did not pass its first complete A/B stability attempt.");
            Assert(!sample.IsActionable && !sample.ExecutionAuthorized,
                "A root trace sample exposed authorization.");
        }

        private static void VerifyOrdinaryDescendantHasNoDomesticFields()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.SetUInt32(memory.LeafAddress, memory.PlainLeafVtable);
            memory.SetUInt32(memory.PlainLeafVtable + 0x0c, memory.LeafTick);
            San9Pk101RootTraceSample sample = Read(memory);
            Assert(sample.ReadSucceeded, FailureText(sample));
            Assert(!sample.DomesticControllerPointer.HasValue
                && !sample.DomesticControllerDepth.HasValue
                && !sample.DomesticControllerCorpsPointer.HasValue
                && !sample.DomesticControllerState.HasValue
                && !sample.DomesticControllerTargetPointer.HasValue,
                "A non-domestic descendant exposed domestic-controller fields.");
        }

        private static void VerifyMultipleDomesticControllersRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.SetUInt32(memory.ChildAddress, San9Pk101RootTraceReader.ExpectedDomesticControllerVtable);
            AssertFailure(Read(memory), "MULTIPLE_DOMESTIC_CONTROLLERS");
        }

        private static void VerifyCycleRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.SetUInt32(memory.LeafAddress + 0x0c, memory.RootAddress);
            AssertFailure(Read(memory), "ROOT_CHAIN_CYCLE");
        }

        private static void VerifyDepthRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            uint first = 0x03000000;
            memory.SetUInt32(memory.RootAddress + 0x0c, first);
            for (int index = 0; index <= San9Pk101RootTraceReader.MaximumDescendantDepth; index++)
            {
                uint node = checked(first + unchecked((uint)(index * 0x100)));
                uint next = index == San9Pk101RootTraceReader.MaximumDescendantDepth
                    ? 0
                    : checked(node + 0x100);
                memory.SetTaskNode(node, memory.ChildVtable, next, 0);
            }

            AssertFailure(Read(memory), "ROOT_CHAIN_DEPTH_EXCEEDED");
        }

        private static void VerifyRootVptrRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            const uint wrongRootVtable = 0x00630000;
            memory.SetUInt32(memory.RootAddress, wrongRootVtable);
            memory.SetUInt32(wrongRootVtable + 0x0c, 0x0047e840);
            AssertFailure(Read(memory), "ROOT_VPTR_MISMATCH");
        }

        private static void VerifyGenerationChangeRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.ChangeGenerationOnSecondCapture = true;
            AssertFailure(Read(memory), "PROCESS_GENERATION_CHANGED_DURING_SAMPLE");
        }

        private static void VerifyReadFailureRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.FailingAddress = memory.LeafAddress + 0x30;
            AssertFailure(Read(memory), "MEMORY_READ_FAILED");
        }

        private static void VerifyDescendantVptrRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.SetUInt32(memory.LeafAddress, 0x03000000);
            AssertFailure(Read(memory), "TASK_VPTR_OUTSIDE_MAIN_MODULE");
        }

        private static void VerifyDescendantTickRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.SetUInt32(memory.LeafVtable + 0x0c, 0x03000000);
            AssertFailure(Read(memory), "TASK_TICK_OUTSIDE_MAIN_MODULE");
        }

        private static void VerifyOneTimeMutationRetried()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.MutateDomesticStateBeforeSecondPassOnce = true;
            San9Pk101RootTraceSample sample = Read(memory);
            Assert(sample.ReadSucceeded, FailureText(sample));
            Assert(sample.StructureDoubleReadStable, "Retried A/B result was not marked stable.");
            Assert(sample.StabilityAttemptCount == 2,
                "A one-time between-pass mutation did not consume exactly one retry.");
            Assert(sample.DomesticControllerState == 0x000003eb,
                "The accepted retry did not contain the stable post-mutation state.");
        }

        private static void VerifyContinuousMutationRejected()
        {
            SyntheticMemory memory = SyntheticMemory.CreateNormal();
            memory.AlternateDomesticStateAtEveryPass = true;
            San9Pk101RootTraceSample sample = Read(memory);
            AssertFailure(sample, "ROOT_TRACE_CHANGED_DURING_SAMPLE");
            Assert(sample.StabilityAttemptCount == San9Pk101RootTraceReader.MaximumStabilityRetries + 1,
                "The stability reader did not exhaust the configured retry count.");
        }

        private static void VerifyBlockingIssueClosesGate()
        {
            San9Pk101DiagnosticReport report = CreateCleanDiagnosticReport(4242, 1000);
            report.Issues = new[]
            {
                new DiagnosticIssue(
                    "WINDOW_CLASS_PATH_MISMATCH",
                    DiagnosticSeverity.Blocking,
                    "A fake expected-class window belongs to another image path.")
            };

            Assert(!San9Pk101DiagnosticGate.ComputeExecutionAllowed(report, report.Issues),
                "Diagnose compatibility computation ignored a blocking fake-window issue.");

            report.ExecutionAllowed = true;
            string error;
            Assert(!San9Pk101DiagnosticGate.TryValidateExactConflictFreeReport(report, out error),
                "The V4 report gate trusted ExecutionAllowed despite a blocking issue.");
        }

        private static void VerifySessionGenerationChangeRejected()
        {
            RootTraceEnvironmentStamp initial;
            RootTraceEnvironmentStamp changed;
            string error;
            Assert(RootTraceEnvironmentStamp.TryCreate(
                CreateCleanDiagnosticReport(4242, 1000),
                out initial,
                out error), error);
            Assert(RootTraceEnvironmentStamp.TryCreate(
                CreateCleanDiagnosticReport(4242, 1001),
                out changed,
                out error), error);
            RootTraceSessionBinding binding = new RootTraceSessionBinding(initial);
            Assert(!binding.TryMatch(changed, out error),
                "A changed process creation generation was accepted by the session binding.");
        }

        private static San9Pk101DiagnosticReport CreateCleanDiagnosticReport(
            int processId,
            long creationGeneration)
        {
            const long gameWindowHandle = 0x00009001;
            int loaderProcessId = processId + 100;
            long loaderCreationGeneration = creationGeneration + 10;
            const uint easyModuleBase = 0x10000000;
            ConflictDiagnostic exactEasyProcess = new ConflictDiagnostic
            {
                Kind = ConflictKind.KnownProcess,
                Code = "COMPATIBLE_EASY_PROCESS",
                Name = "San9PKEasy.exe",
                ProcessId = loaderProcessId,
                ProcessCreationFileTimeUtc = loaderCreationGeneration,
                Path = San9Pk101ConflictDetector.CompatibleEasyProcessPath,
                FileSize = San9Pk101ConflictDetector.CompatibleEasyProcessSize,
                FileSha256 = San9Pk101ConflictDetector.CompatibleEasyProcessSha256,
                IsBlocking = false
            };
            ConflictDiagnostic exactEasyModule = new ConflictDiagnostic
            {
                Kind = ConflictKind.KnownModule,
                Code = "COMPATIBLE_EASY_MODULE",
                Name = "Easy.dll",
                ProcessId = processId,
                Path = San9Pk101ConflictDetector.CompatibleEasyModulePath,
                FileSize = San9Pk101ConflictDetector.CompatibleEasyModuleFileSize,
                FileSha256 = San9Pk101ConflictDetector.CompatibleEasyModuleSha256,
                ModuleBaseAddress = easyModuleBase,
                ModuleImageSize = San9Pk101ConflictDetector.CompatibleEasyModuleImageSize,
                IsBlocking = false
            };
            EasyCompatibilityTicket ticket = new EasyCompatibilityTicket
            {
                GameProcessId = processId,
                GameProcessCreationFileTimeUtc = creationGeneration,
                GameWindowHandle = gameWindowHandle,
                EasyLoaderProcessId = loaderProcessId,
                EasyLoaderCreationFileTimeUtc = loaderCreationGeneration,
                EasyLoaderPath = exactEasyProcess.Path,
                EasyLoaderFileSize = exactEasyProcess.FileSize.GetValueOrDefault(),
                EasyLoaderFileSha256 = exactEasyProcess.FileSha256,
                EasyModuleBaseAddress = easyModuleBase,
                EasyModuleImageSize = exactEasyModule.ModuleImageSize.GetValueOrDefault(),
                EasyModulePath = exactEasyModule.Path,
                EasyModuleFileSize = exactEasyModule.FileSize.GetValueOrDefault(),
                EasyModuleFileSha256 = exactEasyModule.FileSha256,
                ManifestSha256 = EasyCompatibilityManifest.ManifestSha256,
                HookEpochId = new string('A', 64),
                SnapshotFingerprintSha256 = new string('B', 64)
            };
            San9Pk101DiagnosticReport report = new San9Pk101DiagnosticReport
            {
                FileValidation = new FileValidationDiagnostic
                {
                    IsValid = true,
                    Issues = new DiagnosticIssue[0]
                },
                ProcessDiscovery = new ProcessDiscoveryDiagnostic
                {
                    Status = ProcessDiscoveryStatus.Unique,
                    Candidates = new[]
                    {
                        new ProcessCandidateDiagnostic
                        {
                            ProcessId = processId,
                            ProcessName = "San9PK",
                            ImagePath = San9Pk101Target.ExpectedExecutablePath,
                            ImagePathQuerySucceeded = true,
                            PathMatches = true,
                            HasExpectedWindowClass = true,
                            WindowHandles = new[] { gameWindowHandle }
                        }
                    },
                    SelectedProcessId = processId,
                    SelectedImagePath = San9Pk101Target.ExpectedExecutablePath,
                    Issues = new DiagnosticIssue[0]
                },
                ReadOnlyConnection = new ReadOnlyConnectionDiagnostic
                {
                    Connected = true,
                    ProcessId = processId,
                    ImagePath = San9Pk101Target.ExpectedExecutablePath,
                    ProcessCreationFileTimeUtc = creationGeneration,
                    MainModuleBaseAddress = San9Pk101Target.ExpectedImageBase,
                    MainModuleSize = San9Pk101Target.ExpectedSizeOfImage
                },
                ConflictScan = new ConflictScanDiagnostic
                {
                    ProcessScanSucceeded = true,
                    ModuleScanAttempted = true,
                    ModuleScanSucceeded = true,
                    HasBlockingConflicts = false,
                    Conflicts = new[] { exactEasyProcess, exactEasyModule },
                    Issues = new DiagnosticIssue[0]
                },
                EasyCompatibility = new EasyRuntimeCompatibilityReport
                {
                    State = EasyRuntimeCompatibilityState.Installed,
                    StableSnapshot = true,
                    CompatibleForFutureBridge = true,
                    RestartRequired = false,
                    InstalledRedirectCount = EasyCompatibilityManifest.Redirects.Length,
                    OriginalRedirectCount = 0,
                    UnknownRedirectCount = 0,
                    Ticket = ticket,
                    Issues = new DiagnosticIssue[0]
                },
                Issues = new DiagnosticIssue[0]
            };
            report.ExecutionAllowed = San9Pk101DiagnosticGate.ComputeExecutionAllowed(
                report,
                report.Issues);
            return report;
        }

        private static San9Pk101RootTraceSample Read(SyntheticMemory memory)
        {
            return new San9Pk101RootTraceReader().Read(memory);
        }

        private static void AssertFailure(San9Pk101RootTraceSample sample, string code)
        {
            Assert(!sample.ReadSucceeded, "The synthetic fault unexpectedly succeeded.");
            Assert(string.Equals(sample.FailureCode, code, StringComparison.Ordinal),
                "Expected " + code + ", got " + FailureText(sample));
            Assert(!sample.IsActionable && !sample.ExecutionAuthorized,
                "A failed sample exposed authorization.");
        }

        private static string FailureText(San9Pk101RootTraceSample sample)
        {
            return (sample.FailureCode ?? "-") + ":" + (sample.FailureMessage ?? "-");
        }

        private static void Run(string name, Action action)
        {
            try
            {
                action();
                passed++;
                Console.WriteLine("PASS: " + name);
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

        private sealed class SyntheticMemory : IReadOnlyProcessMemory
        {
            internal const int SyntheticProcessId = 4242;
            internal readonly uint RootAddress = 0x02004000;
            internal readonly uint ChildAddress = 0x02005000;
            internal readonly uint LeafAddress = 0x02006000;
            internal readonly uint ChildVtable = 0x00620000;
            internal readonly uint PlainLeafVtable = 0x00620100;
            internal readonly uint LeafVtable = San9Pk101RootTraceReader.ExpectedDomesticControllerVtable;
            internal readonly uint LeafTick = 0x00516220;

            private readonly Dictionary<uint, uint> values = new Dictionary<uint, uint>();
            private int identityCaptureCount;
            private int structurePassCount;

            private SyntheticMemory()
            {
                InitialIdentity = new ProcessIdentitySnapshot
                {
                    ProcessId = SyntheticProcessId,
                    ImagePath = San9Pk101Target.ExpectedExecutablePath,
                    CreationFileTimeUtc = DateTime.UtcNow.ToFileTimeUtc(),
                    MainModuleBaseAddress = San9Pk101Target.ExpectedImageBase,
                    MainModuleSize = San9Pk101Target.ExpectedSizeOfImage
                };
            }

            public int ProcessId
            {
                get { return SyntheticProcessId; }
            }

            public string ImagePath
            {
                get { return San9Pk101Target.ExpectedExecutablePath; }
            }

            public ProcessIdentitySnapshot InitialIdentity { get; private set; }
            internal bool ChangeGenerationOnSecondCapture;
            internal uint? FailingAddress;
            internal bool MutateDomesticStateBeforeSecondPassOnce;
            internal bool AlternateDomesticStateAtEveryPass;

            internal static SyntheticMemory CreateNormal()
            {
                SyntheticMemory memory = new SyntheticMemory();
                const uint window = 0x02001000;
                const uint owner = 0x02002000;
                const uint scene = 0x02003000;
                const uint childTick = 0x0047e900;

                memory.SetUInt32(San9Pk101RootTraceReader.AppObjectAddress + 0x04, window);
                memory.SetUInt32(window + 0x1c, owner);
                memory.SetUInt32(owner + 0x18, scene);
                memory.SetUInt32(scene + 0x8c, memory.RootAddress);

                memory.SetUInt32(
                    San9Pk101RootTraceReader.ExpectedSchedulerRootVtable + 0x0c,
                    0x0047e360);
                memory.SetUInt32(memory.ChildVtable + 0x0c, childTick);
                memory.SetUInt32(memory.LeafVtable + 0x0c, memory.LeafTick);

                memory.SetTaskNode(
                    memory.RootAddress,
                    San9Pk101RootTraceReader.ExpectedSchedulerRootVtable,
                    memory.ChildAddress,
                    0x0200a000);
                memory.SetTaskNode(memory.ChildAddress, memory.ChildVtable, memory.LeafAddress, 0x0200a100);
                memory.SetTaskNode(memory.LeafAddress, memory.LeafVtable, 0, 0x0200d000);
                memory.SetUInt32(memory.LeafAddress + 0x30, 0x0200b000);
                memory.SetUInt32(memory.LeafAddress + 0x34, 0x000003ea);
                memory.SetUInt32(memory.LeafAddress + 0x38, 0x0200c000);
                return memory;
            }

            public byte[] ReadBytes(long address, int count)
            {
                uint nativeAddress = checked((uint)address);
                if (count != 4)
                {
                    throw new InvalidOperationException("Synthetic root trace accepts four-byte reads only.");
                }

                if (FailingAddress.HasValue && FailingAddress.Value == nativeAddress)
                {
                    throw new InvalidOperationException("Synthetic read failure.");
                }

                if (nativeAddress == San9Pk101RootTraceReader.AppObjectAddress + 0x04)
                {
                    structurePassCount++;
                    if (MutateDomesticStateBeforeSecondPassOnce && structurePassCount == 2)
                    {
                        values[LeafAddress + 0x34] = 0x000003ebU;
                    }

                    if (AlternateDomesticStateAtEveryPass)
                    {
                        values[LeafAddress + 0x34] = (structurePassCount & 1) == 0
                            ? 0x000003ebU
                            : 0x000003eaU;
                    }
                }

                uint value;
                if (!values.TryGetValue(nativeAddress, out value))
                {
                    throw new InvalidOperationException(string.Format(
                        "Unmapped synthetic read at 0x{0:X8}.",
                        nativeAddress));
                }

                return BitConverter.GetBytes(value);
            }

            public bool TryCaptureIdentity(out ProcessIdentitySnapshot identity, out string error)
            {
                identityCaptureCount++;
                identity = new ProcessIdentitySnapshot
                {
                    ProcessId = InitialIdentity.ProcessId,
                    ImagePath = InitialIdentity.ImagePath,
                    CreationFileTimeUtc = InitialIdentity.CreationFileTimeUtc,
                    MainModuleBaseAddress = InitialIdentity.MainModuleBaseAddress,
                    MainModuleSize = InitialIdentity.MainModuleSize
                };
                if (ChangeGenerationOnSecondCapture && identityCaptureCount >= 2)
                {
                    identity.CreationFileTimeUtc++;
                }

                error = null;
                return true;
            }

            internal void SetTaskNode(
                uint address,
                uint vtable,
                uint child,
                uint pending)
            {
                SetUInt32(address, vtable);
                SetUInt32(address + 0x0c, child);
                SetUInt32(address + 0x10, pending);
                if (!values.ContainsKey(vtable + 0x0c))
                {
                    SetUInt32(vtable + 0x0c, 0x0047e900);
                }
            }

            internal void SetUInt32(uint address, uint value)
            {
                values[address] = value;
            }
        }
    }
}
