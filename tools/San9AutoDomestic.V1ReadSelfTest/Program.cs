using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.V1ReadSelfTest
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();
        private static int passed;

        private static int Main()
        {
            Run("valid synthetic snapshot", VerifyValidSnapshot);
            Run("resident list cycle rejected", VerifyCycleRejected);
            Run("resident list wrong tail rejected", VerifyWrongTailRejected);
            Run("person record ID mismatch rejected", VerifyWrongPersonIdRejected);
            Run("active person polluted container rejected", VerifyPollutedContainerRejected);
            Run("cross-read table change rejected", VerifyCrossReadChangeRejected);
            Run("cross-read resident graph change rejected", VerifyCrossReadGraphChangeRejected);
            Run("32-bit read range end guarded", VerifyReadRangeGuard);
            Run("process generation change rejected", VerifyProcessGenerationChangeRejected);

            Console.WriteLine("V1 synthetic summary: {0} passed, {1} failed.", passed, Failures.Count);
            foreach (string failure in Failures)
            {
                Console.Error.WriteLine("FAIL: " + failure);
            }

            return Failures.Count == 0 ? 0 : 1;
        }

        private static void VerifyValidSnapshot()
        {
            SyntheticState state = SyntheticState.Create();
            San9Pk101ReadReport report = Read(state);
            Assert(report.ReadSucceeded, JoinIssues(report));
            Assert(report.RelevantFieldsWereStable, "Expected a stable A/B read.");
            Assert(report.ProcessIdentityRevalidatedAfterRead, "Identity post-check was not recorded.");
            Assert(report.StableSummarySha256 != null && report.StableSummarySha256.Length == 64,
                "Stable summary SHA-256 is absent.");
            Assert(report.Cities[0].ResidentOfficerIds.SequenceEqual(new[] { 0 }),
                "The verified resident set differs from the synthetic list.");
            Assert(report.Snapshot.Context.Readiness == San9AutoDomestic.Core.Domain.SnapshotReadiness.StructureOnly,
                "V1 Core projection was not marked structure-only.");
            Assert(report.Snapshot.Context.ProcessId == report.ProcessId,
                "V1 Core projection lost its process binding.");
        }

        private static void VerifyCycleRejected()
        {
            SyntheticState state = SyntheticState.Create();
            state.SetNodeUInt32(state.FirstNodeAddress, 0, state.FirstNodeAddress);
            San9Pk101ReadReport report = Read(state);
            AssertBlocked(report, "CITY_RESIDENT_LIST_CAPTURE_FAILED");
            AssertBlocked(report, "CITY_RESIDENT_LIST_NOT_TERMINATED");
        }

        private static void VerifyWrongTailRejected()
        {
            SyntheticState state = SyntheticState.Create();
            state.SetTableUInt32(
                San9Pk101MemoryLayout.CityBase + San9Pk101MemoryLayout.CityResidentListLastPointerOffset,
                state.FirstNodeAddress + 0x20);
            San9Pk101ReadReport report = Read(state);
            AssertBlocked(report, "CITY_RESIDENT_LIST_TAIL_MISMATCH");
        }

        private static void VerifyWrongPersonIdRejected()
        {
            SyntheticState state = SyntheticState.Create();
            state.SetTableUInt16(
                San9Pk101MemoryLayout.PersonBase + San9Pk101MemoryLayout.PersonIdOffset,
                7);
            San9Pk101ReadReport report = Read(state);
            AssertBlocked(report, "PERSON_RECORD_ID_MISMATCH");
        }

        private static void VerifyPollutedContainerRejected()
        {
            SyntheticState state = SyntheticState.Create();
            state.SetTableUInt32(
                San9Pk101MemoryLayout.PersonBase + San9Pk101MemoryLayout.PersonResidencePointerOffset,
                0xdeadbeef);
            San9Pk101ReadReport report = Read(state);
            AssertBlocked(report, "ACTIVE_PERSON_CONTAINER_UNREADABLE");
        }

        private static void VerifyCrossReadChangeRejected()
        {
            SyntheticState state = SyntheticState.Create();
            byte[] changed = (byte[])state.TableRegion.Clone();
            int cityNameOffset = checked((int)(
                San9Pk101MemoryLayout.CityBase
                + San9Pk101MemoryLayout.CityNameOffset
                - SyntheticState.RegionBase));
            changed[cityNameOffset] ^= 1;
            state.TableReadVariants = new[] { state.TableRegion, changed };
            San9Pk101ReadReport report = Read(state);
            Assert(!report.ReadSucceeded, "Alternating A/B table images were accepted.");
            Assert(!report.RelevantFieldsWereStable, "Alternating A/B table images were marked stable.");
            AssertBlocked(report, "SNAPSHOT_UNSTABLE");
        }

        private static void VerifyReadRangeGuard()
        {
            IntPtr pointer = ReadOnlyProcessConnection.ValidateReadRangeAndCreatePointer(
                San9Pk101MemoryLayout.CityBase,
                4);
            Assert(pointer != IntPtr.Zero, "A valid table address was rejected.");
            bool rejected = false;
            try
            {
                ReadOnlyProcessConnection.ValidateReadRangeAndCreatePointer(uint.MaxValue, 2);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }

            Assert(rejected, "A read crossing 0xFFFFFFFF was accepted.");
        }

        private static void VerifyCrossReadGraphChangeRejected()
        {
            SyntheticState state = SyntheticState.Create();
            byte[] normal = state.CopyNode(state.FirstNodeAddress);
            byte[] changed = (byte[])normal.Clone();
            SyntheticState.WriteUInt32(changed, San9Pk101MemoryLayout.ResidentNodeNextPointerOffset,
                state.FirstNodeAddress);
            state.NodeReadVariants = new[] { normal, changed };
            San9Pk101ReadReport report = Read(state);
            Assert(!report.ReadSucceeded, "Alternating resident graphs were accepted.");
            Assert(!report.RelevantFieldsWereStable, "Alternating resident graphs were marked stable.");
            AssertBlocked(report, "SNAPSHOT_UNSTABLE");
        }

        private static void VerifyProcessGenerationChangeRejected()
        {
            SyntheticState state = SyntheticState.Create();
            state.ChangeGenerationOnPostCheck = true;
            San9Pk101ReadReport report = Read(state);
            AssertBlocked(report, "PROCESS_IDENTITY_CHANGED_DURING_READ");
        }

        private static San9Pk101ReadReport Read(SyntheticState state)
        {
            return new San9Pk101SnapshotReader().Read(state, new San9Pk101DiagnosticReport());
        }

        private static void AssertBlocked(San9Pk101ReadReport report, string code)
        {
            Assert(!report.ReadSucceeded, "Snapshot unexpectedly succeeded.");
            Assert(report.Issues.Any(issue =>
                issue.Severity == DiagnosticSeverity.Blocking && issue.Code == code),
                "Missing blocking issue " + code + ": " + JoinIssues(report));
        }

        private static string JoinIssues(San9Pk101ReadReport report)
        {
            return string.Join(" | ", report.Issues.Select(issue => issue.Code + ":" + issue.Message).ToArray());
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

        private sealed class SyntheticState : IReadOnlyProcessMemory
        {
            internal const uint RegionBase = 0x0124db58;
            internal readonly uint FirstNodeAddress = 0x02000000;
            internal byte[] TableRegion;
            internal byte[][] TableReadVariants;
            internal byte[][] NodeReadVariants;
            internal bool ChangeGenerationOnPostCheck;
            private readonly Dictionary<uint, byte[]> external = new Dictionary<uint, byte[]>();
            private int tableReadCount;
            private int nodeReadCount;
            private byte[] currentTable;

            private SyntheticState()
            {
                uint personEnd = checked(
                    San9Pk101MemoryLayout.PersonBase
                    + unchecked((uint)(San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount)));
                TableRegion = new byte[checked((int)(personEnd - RegionBase))];
                currentTable = TableRegion;
                InitialIdentity = new ProcessIdentitySnapshot
                {
                    ProcessId = 4242,
                    ImagePath = San9Pk101Target.ExpectedExecutablePath,
                    CreationFileTimeUtc = DateTime.UtcNow.ToFileTimeUtc(),
                    MainModuleBaseAddress = 0x00400000,
                    MainModuleSize = San9Pk101Target.ExpectedSizeOfImage
                };
            }

            public int ProcessId { get { return InitialIdentity.ProcessId; } }
            public string ImagePath { get { return InitialIdentity.ImagePath; } }
            public ProcessIdentitySnapshot InitialIdentity { get; private set; }

            internal static SyntheticState Create()
            {
                SyntheticState state = new SyntheticState();
                for (int cityId = 0; cityId < San9Pk101MemoryLayout.CityCount; cityId++)
                {
                    uint city = checked(
                        San9Pk101MemoryLayout.CityBase
                        + unchecked((uint)(cityId * San9Pk101MemoryLayout.CityStride)));
                    state.SetTableByte(city + San9Pk101MemoryLayout.CityTypeOffset,
                        San9Pk101MemoryLayout.CityTypeValue);
                    state.SetTableAscii(city + San9Pk101MemoryLayout.CityNameOffset, "C" + cityId);
                    state.SetTableUInt32(city + San9Pk101MemoryLayout.CitySelfPointerOffset, city);
                }

                for (int personId = 0; personId < San9Pk101MemoryLayout.PersonCount; personId++)
                {
                    uint person = checked(
                        San9Pk101MemoryLayout.PersonBase
                        + unchecked((uint)(personId * San9Pk101MemoryLayout.PersonStride)));
                    state.SetTableUInt16(person + San9Pk101MemoryLayout.PersonIdOffset,
                        unchecked((ushort)personId));
                    state.SetTableUInt32(person + San9Pk101MemoryLayout.PersonIdentityOffset, uint.MaxValue);
                }

                state.SetTableUInt32(
                    San9Pk101MemoryLayout.ForceBase + San9Pk101MemoryLayout.ForceMoneyOffset,
                    1000);
                state.SetTableUInt32(
                    San9Pk101MemoryLayout.ForceBase + San9Pk101MemoryLayout.ForceFlagsOffset,
                    3);
                state.SetTableUInt32(
                    San9Pk101MemoryLayout.ForceBase + San9Pk101MemoryLayout.ForceMainForcePointerOffset,
                    San9Pk101MemoryLayout.ForceBase);
                state.SetTableUInt32(
                    San9Pk101MemoryLayout.ForceBase + San9Pk101MemoryLayout.ForceLeaderPointerOffset,
                    San9Pk101MemoryLayout.PersonBase);

                uint city0 = San9Pk101MemoryLayout.CityBase;
                uint person0 = San9Pk101MemoryLayout.PersonBase;
                state.SetTableUInt32(city0 + San9Pk101MemoryLayout.CityCorpsPointerOffset,
                    San9Pk101MemoryLayout.ForceBase);
                state.SetTableUInt32(city0 + San9Pk101MemoryLayout.CityResidentListFirstPointerOffset,
                    state.FirstNodeAddress);
                state.SetTableUInt32(city0 + San9Pk101MemoryLayout.CityResidentListLastPointerOffset,
                    state.FirstNodeAddress);
                state.SetTableUInt32(city0 + San9Pk101MemoryLayout.CityValidResidentOfficerCountOffset, 1);
                state.SetTableUInt32(
                    city0 + San9Pk101MemoryLayout.CityResidentUnitPointerOffset
                        + San9Pk101MemoryLayout.ContainerEmbeddedFlagsOffset,
                    San9Pk101MemoryLayout.ContainerEmbeddedFlag);

                state.SetTableAscii(person0 + San9Pk101MemoryLayout.PersonSurnameOffset, "P");
                state.SetTableUInt32(person0 + San9Pk101MemoryLayout.PersonIdentityOffset, 3);
                state.SetTableUInt32(
                    person0 + San9Pk101MemoryLayout.PersonResidencePointerOffset,
                    city0 + San9Pk101MemoryLayout.CityResidentUnitPointerOffset);
                state.SetTableUInt32(person0 + San9Pk101MemoryLayout.PersonEffectiveMightOffset, 50);
                state.SetTableUInt32(person0 + San9Pk101MemoryLayout.PersonEffectiveIntelligenceOffset, 50);
                state.SetTableUInt32(person0 + San9Pk101MemoryLayout.PersonEffectivePoliticsOffset, 50);
                state.SetTableUInt32(person0 + San9Pk101MemoryLayout.PersonEffectiveLeadershipOffset, 50);

                byte[] node = new byte[San9Pk101MemoryLayout.ResidentNodeSize];
                WriteUInt32(node, San9Pk101MemoryLayout.ResidentNodePersonPointerOffset, person0);
                state.external.Add(state.FirstNodeAddress, node);
                state.TableReadVariants = new[] { state.TableRegion };
                return state;
            }

            public byte[] ReadBytes(long address, int count)
            {
                uint start = checked((uint)address);
                if (start == RegionBase && count == TableRegion.Length)
                {
                    currentTable = TableReadVariants[tableReadCount % TableReadVariants.Length];
                    tableReadCount++;
                    return (byte[])currentTable.Clone();
                }

                ulong tableEnd = unchecked((ulong)RegionBase) + unchecked((uint)currentTable.Length);
                ulong readEnd = unchecked((ulong)start) + unchecked((uint)count);
                if (start >= RegionBase && readEnd <= tableEnd)
                {
                    byte[] result = new byte[count];
                    Buffer.BlockCopy(currentTable, checked((int)(start - RegionBase)), result, 0, count);
                    return result;
                }

                byte[] bytes;
                if (external.TryGetValue(start, out bytes) && bytes.Length == count)
                {
                    if (start == FirstNodeAddress && NodeReadVariants != null)
                    {
                        byte[] variant = NodeReadVariants[nodeReadCount % NodeReadVariants.Length];
                        nodeReadCount++;
                        return (byte[])variant.Clone();
                    }

                    return (byte[])bytes.Clone();
                }

                throw new InvalidOperationException(string.Format(
                    "Synthetic read is unmapped: 0x{0:X8}, {1} bytes.", start, count));
            }

            public bool TryCaptureIdentity(out ProcessIdentitySnapshot identity, out string error)
            {
                identity = new ProcessIdentitySnapshot
                {
                    ProcessId = InitialIdentity.ProcessId,
                    ImagePath = InitialIdentity.ImagePath,
                    CreationFileTimeUtc = InitialIdentity.CreationFileTimeUtc,
                    MainModuleBaseAddress = InitialIdentity.MainModuleBaseAddress,
                    MainModuleSize = InitialIdentity.MainModuleSize
                };
                if (ChangeGenerationOnPostCheck)
                {
                    identity.CreationFileTimeUtc++;
                }
                error = null;
                return true;
            }

            internal void SetTableByte(uint address, byte value)
            {
                TableRegion[checked((int)(address - RegionBase))] = value;
            }

            internal void SetTableUInt16(uint address, ushort value)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Buffer.BlockCopy(bytes, 0, TableRegion, checked((int)(address - RegionBase)), 2);
            }

            internal void SetTableUInt32(uint address, uint value)
            {
                WriteUInt32(TableRegion, checked((int)(address - RegionBase)), value);
            }

            internal void SetTableAscii(uint address, string value)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(value);
                Buffer.BlockCopy(bytes, 0, TableRegion, checked((int)(address - RegionBase)), bytes.Length);
            }

            internal void SetNodeUInt32(uint nodeAddress, int offset, uint value)
            {
                WriteUInt32(external[nodeAddress], offset, value);
            }

            internal byte[] CopyNode(uint nodeAddress)
            {
                return (byte[])external[nodeAddress].Clone();
            }

            internal static void WriteUInt32(byte[] target, int offset, uint value)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                Buffer.BlockCopy(bytes, 0, target, offset, 4);
            }
        }
    }
}
