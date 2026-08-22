using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public enum EasyRuntimeCompatibilityState
    {
        NotInspected,
        Missing,
        Ambiguous,
        IdentityMismatch,
        SnapshotUnstable,
        Original,
        PartialOrUnknown,
        IdleAnchorConflict,
        Installed,
        RestartRequired,
        InspectionFailed
    }

    public sealed class EasyCompatibilityTicket
    {
        // This is deterministic diagnostic evidence only. It carries no nonce,
        // MAC, authority, permit, or execution capability.
        internal EasyCompatibilityTicket()
        {
            ExecutionAuthorized = false;
            IsSecurityCapability = false;
        }

        public int GameProcessId { get; internal set; }
        public long GameProcessCreationFileTimeUtc { get; internal set; }
        public long GameWindowHandle { get; internal set; }
        public int EasyLoaderProcessId { get; internal set; }
        public long EasyLoaderCreationFileTimeUtc { get; internal set; }
        public string EasyLoaderPath { get; internal set; }
        public long EasyLoaderFileSize { get; internal set; }
        public string EasyLoaderFileSha256 { get; internal set; }
        public uint EasyModuleBaseAddress { get; internal set; }
        public uint EasyModuleImageSize { get; internal set; }
        public string EasyModulePath { get; internal set; }
        public long EasyModuleFileSize { get; internal set; }
        public string EasyModuleFileSha256 { get; internal set; }
        public string ManifestSha256 { get; internal set; }
        public string HookEpochId { get; internal set; }
        public string SnapshotFingerprintSha256 { get; internal set; }
        public bool ExecutionAuthorized { get; private set; }
        public bool IsSecurityCapability { get; private set; }
    }

    public sealed class EasyRuntimeCompatibilityReport
    {
        internal EasyRuntimeCompatibilityReport()
        {
            ManifestSha256 = EasyCompatibilityManifest.ManifestSha256;
            Issues = new DiagnosticIssue[0];
            ExecutionAuthorized = false;
        }

        public EasyRuntimeCompatibilityState State { get; internal set; }
        public bool StableSnapshot { get; internal set; }
        public bool CompatibleForFutureBridge { get; internal set; }
        public bool RestartRequired { get; internal set; }
        public bool ExecutionAuthorized { get; private set; }
        public int InstalledRedirectCount { get; internal set; }
        public int OriginalRedirectCount { get; internal set; }
        public int UnknownRedirectCount { get; internal set; }
        public string ManifestSha256 { get; private set; }
        public EasyCompatibilityTicket Ticket { get; internal set; }
        public DiagnosticIssue[] Issues { get; internal set; }
    }

    internal sealed class EasyRedirectDefinition
    {
        private readonly byte[] original;
        private readonly byte[] trailing;

        internal EasyRedirectDefinition(
            string id,
            uint address,
            int length,
            string originalHex,
            byte opcode,
            uint targetRva,
            string trailingHex)
        {
            Id = id;
            Address = address;
            Length = length;
            original = EasyCompatibilityManifest.ParseHex(originalHex);
            Opcode = opcode;
            TargetRva = targetRva;
            trailing = EasyCompatibilityManifest.ParseHex(trailingHex);
        }

        internal readonly string Id;
        internal readonly uint Address;
        internal readonly int Length;
        internal readonly byte Opcode;
        internal readonly uint TargetRva;

        internal byte[] Original
        {
            get { return (byte[])original.Clone(); }
        }

        internal byte[] Trailing
        {
            get { return (byte[])trailing.Clone(); }
        }

        internal byte[] InstalledBytes(uint easyBase)
        {
            byte[] result = new byte[Length];
            result[0] = Opcode;
            // The displacement belongs to the five-byte CALL/JMP. For the two
            // CALL+NOP sites the trailing NOP is not part of the instruction.
            uint nextInstruction = unchecked(Address + 5u);
            uint target = unchecked(easyBase + TargetRva);
            uint displacementModulo2To32 = unchecked(target - nextInstruction);
            Buffer.BlockCopy(BitConverter.GetBytes(displacementModulo2To32), 0, result, 1, 4);
            if (trailing.Length != 0)
            {
                Buffer.BlockCopy(trailing, 0, result, 5, trailing.Length);
            }

            return result;
        }
    }

    internal static class EasyCompatibilityManifest
    {
        // SHA-256 of docs/easy-compatibility-manifest.json. Runtime code never loads that
        // mutable file: the reviewed ownership table below is compiled into this assembly.
        internal const string ManifestSha256 = "72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE";
        internal const uint HwndSlotRva = 0x00006450;
        internal const uint HwndOriginalValue = 0x00000000;
        internal const uint ChildTrainingAAddress = 0x0040CAEF;
        internal const uint ChildTrainingBAddress = 0x0040BF25;
        internal const byte ChildTrainingOriginalValue = 0x32;
        internal const byte ChildTrainingAlternateValue = 0x00;
        internal const uint MaxCorpsFoodAAddress = 0x0043DA06;
        internal const uint MaxCorpsFoodBAddress = 0x0045AC96;
        internal const uint AiCounterBarbariansAddress = 0x004B06B1;
        internal const byte AiCounterBarbariansOriginalValue = 0xE4;
        internal const byte AiCounterBarbariansInstalledValue = 0x00;
        internal const uint ProtectedPageAddress = 0x0061C000;
        internal const uint ProtectedPageLength = 0x00001000;
        internal const uint InstalledPageProtection = 0x04;
        internal const uint OriginalPageProtection = 0x02;
        internal const uint IdleSlotAddress = 0x00604DF4;
        internal const uint OriginalIdleAddress = 0x00434100;
        internal const uint IdleCallerAnchorAddress = 0x005C5D05;
        internal const uint IdleCallerReturnAddress = 0x005C5D11;

        private static readonly byte[] OriginalIdlePrefixValue = ParseHex("56578B7C240C");
        private static readonly byte[] IdleCallerAnchorValue = ParseHex("85FF8BCE75088B0657FF5024EBDD");
        private static readonly byte[] MaxCorpsFoodOriginalValue = ParseHex("40420F00");
        private static readonly byte[] MaxCorpsFoodInstalledValue = ParseHex("80969800");

        internal static byte[] OriginalIdlePrefix { get { return (byte[])OriginalIdlePrefixValue.Clone(); } }
        internal static byte[] IdleCallerAnchor { get { return (byte[])IdleCallerAnchorValue.Clone(); } }
        internal static byte[] MaxCorpsFoodOriginal { get { return (byte[])MaxCorpsFoodOriginalValue.Clone(); } }
        internal static byte[] MaxCorpsFoodInstalled { get { return (byte[])MaxCorpsFoodInstalledValue.Clone(); } }

        private static readonly EasyRedirectDefinition[] RedirectsValue =
        {
            new EasyRedirectDefinition("redirect-00", 0x004E6270u, 5, "E92B52FEFF", 0xE9, 0x00001110u, ""),
            new EasyRedirectDefinition("redirect-01", 0x00485DB6u, 5, "E845FCFFFF", 0xE8, 0x00001120u, ""),
            new EasyRedirectDefinition("redirect-02", 0x0049575Du, 5, "E89E02FFFF", 0xE8, 0x00001120u, ""),
            new EasyRedirectDefinition("redirect-03", 0x004EC414u, 5, "E8673F0800", 0xE8, 0x000011C0u, ""),
            new EasyRedirectDefinition("redirect-04", 0x004ED0AEu, 5, "E8CD320800", 0xE8, 0x000011F0u, ""),
            new EasyRedirectDefinition("redirect-05", 0x004CF7BBu, 5, "E8C00B0A00", 0xE8, 0x00001220u, ""),
            new EasyRedirectDefinition("redirect-06", 0x004EDFABu, 5, "E8D0230800", 0xE8, 0x00001250u, ""),
            new EasyRedirectDefinition("redirect-07", 0x004E122Bu, 5, "E850F10800", 0xE8, 0x00001280u, ""),
            new EasyRedirectDefinition("redirect-08", 0x004D134Bu, 5, "E830F00900", 0xE8, 0x000012B0u, ""),
            new EasyRedirectDefinition("redirect-09", 0x004DB3EBu, 5, "E8904F0900", 0xE8, 0x000012E0u, ""),
            new EasyRedirectDefinition("redirect-10", 0x004DFCABu, 5, "E8D0060900", 0xE8, 0x00001310u, ""),
            new EasyRedirectDefinition("redirect-11", 0x004EC3D8u, 5, "E8C37AFAFF", 0xE8, 0x000013E0u, ""),
            new EasyRedirectDefinition("redirect-12", 0x004ED075u, 5, "E8967EFAFF", 0xE8, 0x000013F0u, ""),
            new EasyRedirectDefinition("redirect-13", 0x004D1EA7u, 5, "E804CBFBFF", 0xE8, 0x00001410u, ""),
            new EasyRedirectDefinition("redirect-14", 0x004D16DDu, 5, "E8CED2FBFF", 0xE8, 0x00001410u, ""),
            new EasyRedirectDefinition("redirect-15", 0x004DBE67u, 5, "E88449FBFF", 0xE8, 0x00001430u, ""),
            new EasyRedirectDefinition("redirect-16", 0x004DB77Du, 5, "E86E50FBFF", 0xE8, 0x00001430u, ""),
            new EasyRedirectDefinition("redirect-17", 0x004E06EAu, 5, "E8D11FFBFF", 0xE8, 0x00001450u, ""),
            new EasyRedirectDefinition("redirect-18", 0x004E003Du, 5, "E87E26FBFF", 0xE8, 0x00001450u, ""),
            new EasyRedirectDefinition("redirect-19", 0x004D0DFAu, 5, "E821D6FBFF", 0xE8, 0x00001470u, ""),
            new EasyRedirectDefinition("redirect-20", 0x004D068Du, 5, "E88EDDFBFF", 0xE8, 0x00001470u, ""),
            new EasyRedirectDefinition("redirect-21", 0x004D016Du, 5, "E83ED4FBFF", 0xE8, 0x00001490u, ""),
            new EasyRedirectDefinition("redirect-22", 0x004CFB0Du, 5, "E89EDAFBFF", 0xE8, 0x00001490u, ""),
            new EasyRedirectDefinition("redirect-23", 0x004EE94Du, 5, "E8FE78FAFF", 0xE8, 0x000014B0u, ""),
            new EasyRedirectDefinition("redirect-24", 0x004EE34Du, 5, "E8FE7EFAFF", 0xE8, 0x000014B0u, ""),
            new EasyRedirectDefinition("redirect-25", 0x004E11F2u, 5, "E8D91FFBFF", 0xE8, 0x000014D0u, ""),
            new EasyRedirectDefinition("redirect-26", 0x005CAE0Eu, 5, "E8DDFEFFFF", 0xE8, 0x00001580u, ""),
            new EasyRedirectDefinition("redirect-27", 0x0043308Cu, 5, "E86FCAFEFF", 0xE8, 0x00001620u, ""),
            new EasyRedirectDefinition("redirect-28", 0x00510579u, 5, "E882F5F0FF", 0xE8, 0x00001640u, ""),
            new EasyRedirectDefinition("redirect-29", 0x0052FA45u, 5, "E896E5F2FF", 0xE8, 0x000016B0u, ""),
            new EasyRedirectDefinition("redirect-30", 0x0043DA3Du, 6, "FF150CF35F00", 0xE8, 0x000016A0u, "90"),
            new EasyRedirectDefinition("redirect-31", 0x0045DDA1u, 6, "FF150CF35F00", 0xE8, 0x000016D0u, "90")
        };

        internal static EasyRedirectDefinition[] Redirects
        {
            get { return RedirectsValue.ToArray(); }
        }

        internal static byte[] ParseHex(string value)
        {
            if (string.IsNullOrEmpty(value)) return new byte[0];
            if ((value.Length & 1) != 0) throw new ArgumentException("Hex text must have an even length.", "value");
            byte[] result = new byte[value.Length / 2];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            }

            return result;
        }
    }

    internal sealed class EasyRuntimeBinding
    {
        internal int GameProcessId;
        internal long GameCreationFileTimeUtc;
        internal uint GameImageBase;
        internal uint GameImageSize;
        internal uint GameWindowHandle;
        internal int LoaderProcessId;
        internal long LoaderCreationFileTimeUtc;
        internal string LoaderPath;
        internal long LoaderFileSize;
        internal string LoaderFileSha256;
        internal uint EasyModuleBase;
        internal uint EasyModuleImageSize;
        internal string EasyModulePath;
        internal long EasyModuleFileSize;
        internal string EasyModuleFileSha256;

        internal string GenerationKey
        {
            get { return GameProcessId + ":" + GameCreationFileTimeUtc; }
        }

        internal string HookEpochId()
        {
            return EasyRuntimeSnapshot.Sha256(Encoding.UTF8.GetBytes(string.Join("|", new[]
            {
                EasyCompatibilityManifest.ManifestSha256,
                GenerationKey,
                GameWindowHandle.ToString("X8"),
                LoaderProcessId.ToString(),
                LoaderCreationFileTimeUtc.ToString(),
                LoaderPath ?? string.Empty,
                LoaderFileSha256 ?? string.Empty,
                EasyModuleBase.ToString("X8"),
                EasyModuleImageSize.ToString("X8"),
                EasyModulePath ?? string.Empty,
                EasyModuleFileSha256 ?? string.Empty
            })));
        }
    }

    internal sealed class EasyRuntimeSnapshot
    {
        internal byte[][] RedirectBytes;
        internal byte ChildTrainingA;
        internal byte ChildTrainingB;
        internal byte[] MaxCorpsFoodA;
        internal byte[] MaxCorpsFoodB;
        internal byte AiCounterBarbarians;
        internal uint BoundHwnd;
        internal uint ProtectedPageBase;
        internal uint ProtectedRegionSize;
        internal uint ProtectedPageState;
        internal uint ProtectedPageProtection;
        internal uint IdleSlotValue;
        internal byte[] OriginalIdlePrefix;
        internal byte[] IdleCallerAnchor;

        internal string FingerprintSha256()
        {
            List<byte> bytes = new List<byte>();
            foreach (byte[] redirect in RedirectBytes ?? new byte[0][])
            {
                AddLengthAndBytes(bytes, redirect);
            }
            bytes.Add(ChildTrainingA);
            bytes.Add(ChildTrainingB);
            AddLengthAndBytes(bytes, MaxCorpsFoodA);
            AddLengthAndBytes(bytes, MaxCorpsFoodB);
            bytes.Add(AiCounterBarbarians);
            AddUInt32(bytes, BoundHwnd);
            AddUInt32(bytes, ProtectedPageBase);
            AddUInt32(bytes, ProtectedRegionSize);
            AddUInt32(bytes, ProtectedPageState);
            AddUInt32(bytes, ProtectedPageProtection);
            AddUInt32(bytes, IdleSlotValue);
            AddLengthAndBytes(bytes, OriginalIdlePrefix);
            AddLengthAndBytes(bytes, IdleCallerAnchor);
            return Sha256(bytes.ToArray());
        }

        internal static string Sha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty);
            }
        }

        private static void AddLengthAndBytes(List<byte> target, byte[] value)
        {
            byte[] actual = value ?? new byte[0];
            AddUInt32(target, unchecked((uint)actual.Length));
            target.AddRange(actual);
        }

        private static void AddUInt32(List<byte> target, uint value)
        {
            target.AddRange(BitConverter.GetBytes(value));
        }
    }

    internal static class EasyRuntimeSnapshotVerifier
    {
        internal static EasyRuntimeCompatibilityReport Verify(
            EasyRuntimeBinding binding,
            EasyRuntimeSnapshot first,
            EasyRuntimeSnapshot second)
        {
            EasyRuntimeCompatibilityReport report = new EasyRuntimeCompatibilityReport();
            List<DiagnosticIssue> issues = new List<DiagnosticIssue>();
            string bindingError;
            if (!IsExactBinding(binding, out bindingError))
            {
                report.State = EasyRuntimeCompatibilityState.IdentityMismatch;
                issues.Add(Block("EASY_RUNTIME_IDENTITY_MISMATCH", bindingError));
                report.Issues = issues.ToArray();
                return report;
            }

            if (first == null || second == null
                || !string.Equals(first.FingerprintSha256(), second.FingerprintSha256(), StringComparison.Ordinal))
            {
                report.State = EasyRuntimeCompatibilityState.SnapshotUnstable;
                issues.Add(Block("EASY_RUNTIME_SNAPSHOT_UNSTABLE", "Easy ownership did not produce an identical A/B snapshot."));
                report.Issues = issues.ToArray();
                return report;
            }

            report.StableSnapshot = true;
            ValidateRedirects(binding.EasyModuleBase, second, report, issues);
            bool idleExact = second.IdleSlotValue == EasyCompatibilityManifest.OriginalIdleAddress
                && BytesEqual(second.OriginalIdlePrefix, EasyCompatibilityManifest.OriginalIdlePrefix)
                && BytesEqual(second.IdleCallerAnchor, EasyCompatibilityManifest.IdleCallerAnchor);
            bool fixedAuxInstalled = BytesEqual(second.MaxCorpsFoodA, EasyCompatibilityManifest.MaxCorpsFoodInstalled)
                && BytesEqual(second.MaxCorpsFoodB, EasyCompatibilityManifest.MaxCorpsFoodInstalled)
                && second.AiCounterBarbarians == EasyCompatibilityManifest.AiCounterBarbariansInstalledValue;
            bool trainingInstalled = (second.ChildTrainingA == EasyCompatibilityManifest.ChildTrainingOriginalValue
                    && second.ChildTrainingB == EasyCompatibilityManifest.ChildTrainingOriginalValue)
                || (second.ChildTrainingA == EasyCompatibilityManifest.ChildTrainingAlternateValue
                    && second.ChildTrainingB == EasyCompatibilityManifest.ChildTrainingAlternateValue);
            bool hwndInstalled = second.BoundHwnd == binding.GameWindowHandle;
            bool protectionInstalled = second.ProtectedPageBase <= EasyCompatibilityManifest.ProtectedPageAddress
                && unchecked((ulong)second.ProtectedPageBase + second.ProtectedRegionSize)
                    >= unchecked((ulong)EasyCompatibilityManifest.ProtectedPageAddress
                        + EasyCompatibilityManifest.ProtectedPageLength)
                && second.ProtectedPageState == 0x1000
                && second.ProtectedPageProtection == EasyCompatibilityManifest.InstalledPageProtection;
            bool protectionOriginal = second.ProtectedPageBase <= EasyCompatibilityManifest.ProtectedPageAddress
                && unchecked((ulong)second.ProtectedPageBase + second.ProtectedRegionSize)
                    >= unchecked((ulong)EasyCompatibilityManifest.ProtectedPageAddress
                        + EasyCompatibilityManifest.ProtectedPageLength)
                && second.ProtectedPageState == 0x1000
                && second.ProtectedPageProtection == EasyCompatibilityManifest.OriginalPageProtection;

            if (!idleExact)
            {
                report.State = EasyRuntimeCompatibilityState.IdleAnchorConflict;
                issues.Add(Block("EASY_IDLE_ANCHOR_OCCUPIED", "The idle slot, original function prefix, or caller anchor is not exact."));
            }
            if (!fixedAuxInstalled)
                issues.Add(Block("EASY_FIXED_AUX_NOT_INSTALLED", "One or more fixed Easy auxiliary writes is not installed."));
            if (!trainingInstalled)
                issues.Add(Block("EASY_TRAINING_PAIR_MIXED", "Child-training bytes must be exactly 32/32 or 00/00."));
            if (!hwndInstalled)
                issues.Add(Block("EASY_HWND_MISMATCH", "Easy.dll+0x6450 is not the exact bound game HWND."));
            if (!protectionInstalled)
                issues.Add(Block("EASY_PAGE_PROTECTION_MISMATCH", "The 0x0061C000 page is not a complete committed PAGE_READWRITE region."));

            bool redirectsInstalled = report.InstalledRedirectCount == EasyCompatibilityManifest.Redirects.Length
                && report.OriginalRedirectCount == 0
                && report.UnknownRedirectCount == 0;
            if (redirectsInstalled && fixedAuxInstalled && trainingInstalled && hwndInstalled
                && protectionInstalled && idleExact)
            {
                report.State = EasyRuntimeCompatibilityState.Installed;
                report.CompatibleForFutureBridge = true;
                report.Ticket = CreateTicket(binding, second.FingerprintSha256());
            }
            else if (report.OriginalRedirectCount == EasyCompatibilityManifest.Redirects.Length
                && BytesEqual(second.MaxCorpsFoodA, EasyCompatibilityManifest.MaxCorpsFoodOriginal)
                && BytesEqual(second.MaxCorpsFoodB, EasyCompatibilityManifest.MaxCorpsFoodOriginal)
                && second.AiCounterBarbarians == EasyCompatibilityManifest.AiCounterBarbariansOriginalValue
                && second.ChildTrainingA == EasyCompatibilityManifest.ChildTrainingOriginalValue
                && second.ChildTrainingB == EasyCompatibilityManifest.ChildTrainingOriginalValue
                && second.BoundHwnd == EasyCompatibilityManifest.HwndOriginalValue
                && protectionOriginal
                && idleExact)
            {
                report.State = EasyRuntimeCompatibilityState.Original;
                issues.Add(Block("EASY_HOOKS_NOT_INSTALLED", "The exact original state is valid cleanup evidence but is not production-compatible."));
            }
            else if (report.State != EasyRuntimeCompatibilityState.IdleAnchorConflict)
            {
                report.State = EasyRuntimeCompatibilityState.PartialOrUnknown;
                issues.Add(Block("EASY_RUNTIME_PARTIAL_OR_UNKNOWN", "The Easy ownership set is partial, original/installed mixed, or unknown."));
            }

            report.Issues = issues.ToArray();
            return report;
        }

        internal static bool IsExactBinding(EasyRuntimeBinding binding, out string error)
        {
            error = null;
            if (binding == null)
            {
                error = "The Easy runtime binding is absent.";
                return false;
            }
            if (binding.GameProcessId <= 0 || binding.GameCreationFileTimeUtc <= 0
                || binding.GameImageBase != San9Pk101Target.ExpectedImageBase
                || binding.GameImageSize != San9Pk101Target.ExpectedSizeOfImage
                || binding.GameWindowHandle == 0)
            {
                error = "The game PID/generation/image/HWND binding is incomplete.";
                return false;
            }
            if (binding.LoaderProcessId <= 0 || binding.LoaderCreationFileTimeUtc <= 0
                || !San9Pk101TargetValidator.PathsEqual(binding.LoaderPath, San9Pk101ConflictDetector.CompatibleEasyProcessPath)
                || binding.LoaderFileSize != San9Pk101ConflictDetector.CompatibleEasyProcessSize
                || !string.Equals(binding.LoaderFileSha256, San9Pk101ConflictDetector.CompatibleEasyProcessSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "The Easy loader PID/generation/path/size/hash is not exact.";
                return false;
            }
            ulong moduleEnd = unchecked((ulong)binding.EasyModuleBase + binding.EasyModuleImageSize);
            if (binding.EasyModuleBase == 0
                || moduleEnd > (ulong)uint.MaxValue + 1UL
                || binding.EasyModuleImageSize != San9Pk101ConflictDetector.CompatibleEasyModuleImageSize
                || !San9Pk101TargetValidator.PathsEqual(binding.EasyModulePath, San9Pk101ConflictDetector.CompatibleEasyModulePath)
                || binding.EasyModuleFileSize != San9Pk101ConflictDetector.CompatibleEasyModuleFileSize
                || !string.Equals(binding.EasyModuleFileSha256, San9Pk101ConflictDetector.CompatibleEasyModuleSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "The loaded Easy.dll base/image/path/size/hash is not exact.";
                return false;
            }
            if (unchecked((ulong)EasyCompatibilityManifest.HwndSlotRva + 4u) > binding.EasyModuleImageSize
                || EasyCompatibilityManifest.Redirects.Any(definition =>
                    unchecked((ulong)definition.TargetRva + 1u) > binding.EasyModuleImageSize))
            {
                error = "A frozen Easy handler/HWND RVA is outside the exact loaded image.";
                return false;
            }
            return true;
        }

        private static void ValidateRedirects(
            uint easyBase,
            EasyRuntimeSnapshot snapshot,
            EasyRuntimeCompatibilityReport report,
            List<DiagnosticIssue> issues)
        {
            if (snapshot.RedirectBytes == null
                || snapshot.RedirectBytes.Length != EasyCompatibilityManifest.Redirects.Length)
            {
                report.UnknownRedirectCount = EasyCompatibilityManifest.Redirects.Length;
                issues.Add(Block("EASY_REDIRECT_SET_INCOMPLETE", "The runtime snapshot does not contain exactly 32 redirects."));
                return;
            }

            for (int index = 0; index < EasyCompatibilityManifest.Redirects.Length; index++)
            {
                EasyRedirectDefinition definition = EasyCompatibilityManifest.Redirects[index];
                byte[] actual = snapshot.RedirectBytes[index];
                if (BytesEqual(actual, definition.InstalledBytes(easyBase)))
                    report.InstalledRedirectCount++;
                else if (BytesEqual(actual, definition.Original))
                    report.OriginalRedirectCount++;
                else
                    report.UnknownRedirectCount++;
            }
            if (report.UnknownRedirectCount != 0)
                issues.Add(Block("EASY_REDIRECT_UNKNOWN", "One or more Easy redirects has unknown bytes or resolves to the wrong module/RVA."));
            if (report.OriginalRedirectCount != 0)
                issues.Add(Block("EASY_REDIRECT_NOT_INSTALLED", "One or more Easy redirects remains original."));
        }

        private static EasyCompatibilityTicket CreateTicket(EasyRuntimeBinding binding, string fingerprint)
        {
            return new EasyCompatibilityTicket
            {
                GameProcessId = binding.GameProcessId,
                GameProcessCreationFileTimeUtc = binding.GameCreationFileTimeUtc,
                GameWindowHandle = binding.GameWindowHandle,
                EasyLoaderProcessId = binding.LoaderProcessId,
                EasyLoaderCreationFileTimeUtc = binding.LoaderCreationFileTimeUtc,
                EasyLoaderPath = binding.LoaderPath,
                EasyLoaderFileSize = binding.LoaderFileSize,
                EasyLoaderFileSha256 = binding.LoaderFileSha256,
                EasyModuleBaseAddress = binding.EasyModuleBase,
                EasyModuleImageSize = binding.EasyModuleImageSize,
                EasyModulePath = binding.EasyModulePath,
                EasyModuleFileSize = binding.EasyModuleFileSize,
                EasyModuleFileSha256 = binding.EasyModuleFileSha256,
                ManifestSha256 = EasyCompatibilityManifest.ManifestSha256,
                HookEpochId = binding.HookEpochId(),
                SnapshotFingerprintSha256 = fingerprint
            };
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            return left != null && right != null && left.SequenceEqual(right);
        }

        private static DiagnosticIssue Block(string code, string message)
        {
            return new DiagnosticIssue(code, DiagnosticSeverity.Blocking, message);
        }
    }

    internal sealed class EasyHookEpochTracker
    {
        // Process-wide hard bound: unarmed generations are LRU-evicted; if all
        // 64 are armed, a new generation fails closed and no armed latch is lost.
        private const int MaximumTrackedGenerations = 64;
        private static readonly EasyHookEpochTracker SharedValue = new EasyHookEpochTracker();

        private sealed class EpochState
        {
            internal bool Armed;
            internal bool RestartRequired;
            internal string HookEpochId;
            internal string LatestExactTicketIdentity;
            internal long LastObservedOrdinal;
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, EpochState> generations = new Dictionary<string, EpochState>();
        private long observationOrdinal;
        private string activeArmedGenerationKey;
        private string latestObservedGenerationKey;

        internal static EasyHookEpochTracker Shared
        {
            get { return SharedValue; }
        }

        internal int TrackedGenerationCount
        {
            get
            {
                lock (sync) return generations.Count;
            }
        }

        internal EasyRuntimeCompatibilityReport Observe(
            EasyRuntimeBinding binding,
            EasyRuntimeCompatibilityReport report)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (binding == null || binding.GameProcessId <= 0 || binding.GameCreationFileTimeUtc <= 0)
            {
                lock (sync)
                {
                    string previousLatestGenerationKey = latestObservedGenerationKey;
                    latestObservedGenerationKey = null;
                    EpochState previousLatest;
                    if (!string.IsNullOrEmpty(previousLatestGenerationKey)
                        && generations.TryGetValue(previousLatestGenerationKey, out previousLatest))
                    {
                        // A ticket is current only while its exact generation is also the
                        // latest successful observation. Any unbound inspection gap revokes
                        // that diagnostic evidence before an explicit bridge install can arm.
                        previousLatest.LatestExactTicketIdentity = null;
                    }
                    EpochState active;
                    if (!string.IsNullOrEmpty(activeArmedGenerationKey)
                        && generations.TryGetValue(activeArmedGenerationKey, out active)
                        && active.Armed)
                    {
                        active.RestartRequired = true;
                        active.LatestExactTicketIdentity = null;
                        ApplyRestartRequired(report);
                    }
                    return report;
                }
            }

            lock (sync)
            {
                latestObservedGenerationKey = binding.GenerationKey;
                EpochState state;
                if (!generations.TryGetValue(binding.GenerationKey, out state))
                {
                    if (generations.Count >= MaximumTrackedGenerations)
                    {
                        KeyValuePair<string, EpochState> removable = generations
                            .Where(item => !item.Value.Armed)
                            .OrderBy(item => item.Value.LastObservedOrdinal)
                            .FirstOrDefault();
                        if (removable.Value == null)
                        {
                            report.State = EasyRuntimeCompatibilityState.RestartRequired;
                            report.CompatibleForFutureBridge = false;
                            report.RestartRequired = true;
                            report.Ticket = null;
                            report.Issues = (report.Issues ?? new DiagnosticIssue[0])
                                .Concat(new[] { new DiagnosticIssue(
                                    "EASY_EPOCH_TRACKER_CAPACITY",
                                    DiagnosticSeverity.Blocking,
                                    "The bounded process-wide Easy epoch tracker is full of armed generations.") })
                                .ToArray();
                            return report;
                        }
                        generations.Remove(removable.Key);
                    }
                    state = new EpochState();
                    generations.Add(binding.GenerationKey, state);
                }
                state.LastObservedOrdinal = ++observationOrdinal;

                bool nowExact = report.State == EasyRuntimeCompatibilityState.Installed
                    && report.CompatibleForFutureBridge
                    && report.Ticket != null;
                string nowEpoch = nowExact ? report.Ticket.HookEpochId : null;
                state.LatestExactTicketIdentity = nowExact
                    ? TicketIdentity(report.Ticket)
                    : null;
                if (!state.Armed)
                {
                    return report;
                }

                if (!nowExact || !string.Equals(state.HookEpochId, nowEpoch, StringComparison.Ordinal))
                    state.RestartRequired = true;
                if (state.RestartRequired)
                {
                    ApplyRestartRequired(report);
                }
                return report;
            }
        }

        internal bool NotifyBridgeInstalled(EasyCompatibilityTicket ticket)
        {
            if (ticket == null || ticket.GameProcessId <= 0
                || ticket.GameProcessCreationFileTimeUtc <= 0
                || ticket.ExecutionAuthorized || ticket.IsSecurityCapability)
                return false;
            string generationKey = ticket.GameProcessId + ":" + ticket.GameProcessCreationFileTimeUtc;
            lock (sync)
            {
                EpochState state;
                string identity = TicketIdentity(ticket);
                if (!generations.TryGetValue(generationKey, out state)
                    || !string.Equals(latestObservedGenerationKey, generationKey, StringComparison.Ordinal)
                    || state.RestartRequired
                    || string.IsNullOrEmpty(state.LatestExactTicketIdentity)
                    || !string.Equals(state.LatestExactTicketIdentity, identity, StringComparison.Ordinal)
                    || string.IsNullOrEmpty(ticket.HookEpochId))
                    return false;
                state.Armed = true;
                state.HookEpochId = ticket.HookEpochId;
                state.LastObservedOrdinal = ++observationOrdinal;
                activeArmedGenerationKey = generationKey;
                return true;
            }
        }

        private static string TicketIdentity(EasyCompatibilityTicket ticket)
        {
            if (ticket == null) return string.Empty;
            return string.Join("|", new[]
            {
                ticket.GameProcessId.ToString(),
                ticket.GameProcessCreationFileTimeUtc.ToString(),
                ticket.GameWindowHandle.ToString(),
                ticket.EasyLoaderProcessId.ToString(),
                ticket.EasyLoaderCreationFileTimeUtc.ToString(),
                ticket.EasyLoaderPath ?? string.Empty,
                ticket.EasyLoaderFileSize.ToString(),
                ticket.EasyLoaderFileSha256 ?? string.Empty,
                ticket.EasyModuleBaseAddress.ToString("X8"),
                ticket.EasyModuleImageSize.ToString("X8"),
                ticket.EasyModulePath ?? string.Empty,
                ticket.EasyModuleFileSize.ToString(),
                ticket.EasyModuleFileSha256 ?? string.Empty,
                ticket.ManifestSha256 ?? string.Empty,
                ticket.HookEpochId ?? string.Empty,
                ticket.SnapshotFingerprintSha256 ?? string.Empty
            });
        }

        private static void ApplyRestartRequired(EasyRuntimeCompatibilityReport report)
        {
            report.State = EasyRuntimeCompatibilityState.RestartRequired;
            report.CompatibleForFutureBridge = false;
            report.RestartRequired = true;
            report.Ticket = null;
            List<DiagnosticIssue> issues = new List<DiagnosticIssue>(report.Issues ?? new DiagnosticIssue[0]);
            if (!issues.Any(item => item != null
                && string.Equals(item.Code, "EASY_HOOK_EPOCH_RESTART_REQUIRED", StringComparison.Ordinal)))
            {
                issues.Add(new DiagnosticIssue(
                    "EASY_HOOK_EPOCH_RESTART_REQUIRED",
                    DiagnosticSeverity.Blocking,
                    "This game generation previously lost or changed its explicitly armed Easy hook epoch; restart the game."));
            }
            report.Issues = issues.ToArray();
        }
    }

    internal sealed class San9Pk101EasyRuntimeInspector
    {
        private readonly EasyHookEpochTracker epochTracker;

        internal San9Pk101EasyRuntimeInspector(EasyHookEpochTracker epochTracker)
        {
            if (epochTracker == null) throw new ArgumentNullException("epochTracker");
            this.epochTracker = epochTracker;
        }

        internal EasyRuntimeCompatibilityReport Inspect(
            ReadOnlyProcessConnection connection,
            San9Pk101DiagnosticReport baseline)
        {
            EasyRuntimeBinding binding;
            EasyRuntimeCompatibilityReport identityFailure;
            if (!TryCreateBinding(baseline, out binding, out identityFailure))
                return epochTracker.Observe(binding, identityFailure);

            try
            {
                if (!RevalidateExternalIdentity(binding))
                    return epochTracker.Observe(binding, Failure(EasyRuntimeCompatibilityState.IdentityMismatch,
                        "EASY_RUNTIME_IDENTITY_CHANGED", "The loader or loaded Easy.dll identity changed before snapshot A."));

                ProcessIdentitySnapshot before;
                string identityError = null;
                if (connection == null || !connection.TryCaptureIdentity(out before, out identityError)
                    || !connection.InitialIdentity.SameGenerationAndImage(before))
                    return epochTracker.Observe(binding, Failure(EasyRuntimeCompatibilityState.IdentityMismatch,
                        "EASY_GAME_IDENTITY_CHANGED", identityError ?? "The game identity changed before snapshot A."));

                EasyRuntimeSnapshot first = Capture(connection, binding);
                EasyRuntimeSnapshot second = Capture(connection, binding);

                ProcessIdentitySnapshot after;
                if (!connection.TryCaptureIdentity(out after, out identityError)
                    || !before.SameGenerationAndImage(after)
                    || !RevalidateExternalIdentity(binding))
                    return epochTracker.Observe(binding, Failure(EasyRuntimeCompatibilityState.IdentityMismatch,
                        "EASY_RUNTIME_IDENTITY_CHANGED", identityError ?? "A bound process/module generation changed during A/B capture."));

                return epochTracker.Observe(binding, EasyRuntimeSnapshotVerifier.Verify(binding, first, second));
            }
            catch (Exception exception)
            {
                return epochTracker.Observe(binding, Failure(
                    EasyRuntimeCompatibilityState.InspectionFailed,
                    "EASY_RUNTIME_INSPECTION_FAILED",
                    exception.GetType().Name + ": " + exception.Message));
            }
        }

        internal static bool TryCreateBinding(
            San9Pk101DiagnosticReport baseline,
            out EasyRuntimeBinding binding,
            out EasyRuntimeCompatibilityReport failure)
        {
            binding = null;
            failure = null;
            if (baseline == null || baseline.ReadOnlyConnection == null
                || !baseline.ReadOnlyConnection.Connected || baseline.ConflictScan == null)
            {
                failure = Failure(EasyRuntimeCompatibilityState.Missing,
                    "EASY_RUNTIME_NOT_READY", "A connected exact game and exact Easy process/module pair are required.");
                return false;
            }


            binding = new EasyRuntimeBinding
            {
                GameProcessId = baseline.ReadOnlyConnection.ProcessId.GetValueOrDefault(),
                GameCreationFileTimeUtc = baseline.ReadOnlyConnection.ProcessCreationFileTimeUtc.GetValueOrDefault(),
                GameImageBase = baseline.ReadOnlyConnection.MainModuleBaseAddress.GetValueOrDefault(),
                GameImageSize = baseline.ReadOnlyConnection.MainModuleSize.GetValueOrDefault()
            };

            ConflictDiagnostic[] processRows = (baseline.ConflictScan.Conflicts ?? new ConflictDiagnostic[0])
                .Where(item => item != null && string.Equals(item.Code, "COMPATIBLE_EASY_PROCESS", StringComparison.Ordinal))
                .ToArray();
            ConflictDiagnostic[] moduleRows = (baseline.ConflictScan.Conflicts ?? new ConflictDiagnostic[0])
                .Where(item => item != null && string.Equals(item.Code, "COMPATIBLE_EASY_MODULE", StringComparison.Ordinal))
                .ToArray();
            if (processRows.Length == 0 || moduleRows.Length == 0)
            {
                failure = Failure(EasyRuntimeCompatibilityState.Missing,
                    "EASY_EXACT_PAIR_MISSING", "The exact San9PKEasy loader and Easy.dll must both be present.");
                return false;
            }
            if (processRows.Length != 1 || moduleRows.Length != 1)
            {
                failure = Failure(EasyRuntimeCompatibilityState.Ambiguous,
                    "EASY_EXACT_PAIR_AMBIGUOUS", "Exactly one Easy loader and one Easy.dll module are required.");
                return false;
            }

            ProcessCandidateDiagnostic game = baseline.ProcessDiscovery == null
                ? null
                : (baseline.ProcessDiscovery.Candidates ?? new ProcessCandidateDiagnostic[0])
                    .SingleOrDefault(item => item.ProcessId == baseline.ReadOnlyConnection.ProcessId);
            long[] windows = game == null ? new long[0] : (game.WindowHandles ?? new long[0]);
            if (windows.Length != 1 || windows[0] <= 0 || unchecked((ulong)windows[0]) > uint.MaxValue)
            {
                failure = Failure(EasyRuntimeCompatibilityState.Ambiguous,
                    "EASY_GAME_HWND_AMBIGUOUS", "Exactly one 32-bit KOEI_SAN9WINDOW HWND must bind the game generation.");
                return false;
            }

            ConflictDiagnostic process = processRows[0];
            ConflictDiagnostic module = moduleRows[0];
            binding.GameWindowHandle = unchecked((uint)windows[0]);
            binding.LoaderProcessId = process.ProcessId.GetValueOrDefault();
            binding.LoaderCreationFileTimeUtc = process.ProcessCreationFileTimeUtc.GetValueOrDefault();
            binding.LoaderPath = process.Path;
            binding.LoaderFileSize = process.FileSize.GetValueOrDefault();
            binding.LoaderFileSha256 = process.FileSha256;
            binding.EasyModuleBase = module.ModuleBaseAddress.GetValueOrDefault();
            binding.EasyModuleImageSize = module.ModuleImageSize.GetValueOrDefault();
            binding.EasyModulePath = module.Path;
            binding.EasyModuleFileSize = module.FileSize.GetValueOrDefault();
            binding.EasyModuleFileSha256 = module.FileSha256;
            string error;
            if (!EasyRuntimeSnapshotVerifier.IsExactBinding(binding, out error)
                || module.ProcessId != binding.GameProcessId
                || !San9Pk101ConflictDetector.ContainsOnlyExactCompatibleEasy(baseline.ConflictScan))
            {
                failure = Failure(EasyRuntimeCompatibilityState.IdentityMismatch,
                    "EASY_RUNTIME_IDENTITY_MISMATCH", error ?? "The exact Easy pair is not bound to this game PID.");
                return false;
            }
            return true;
        }

        private static EasyRuntimeSnapshot Capture(ReadOnlyProcessConnection connection, EasyRuntimeBinding binding)
        {
            EasyRuntimeSnapshot snapshot = new EasyRuntimeSnapshot();
            snapshot.RedirectBytes = EasyCompatibilityManifest.Redirects
                .Select(definition => connection.ReadBytes(definition.Address, definition.Length))
                .ToArray();
            snapshot.ChildTrainingA = connection.ReadBytes(EasyCompatibilityManifest.ChildTrainingAAddress, 1)[0];
            snapshot.ChildTrainingB = connection.ReadBytes(EasyCompatibilityManifest.ChildTrainingBAddress, 1)[0];
            snapshot.MaxCorpsFoodA = connection.ReadBytes(EasyCompatibilityManifest.MaxCorpsFoodAAddress, 4);
            snapshot.MaxCorpsFoodB = connection.ReadBytes(EasyCompatibilityManifest.MaxCorpsFoodBAddress, 4);
            snapshot.AiCounterBarbarians = connection.ReadBytes(EasyCompatibilityManifest.AiCounterBarbariansAddress, 1)[0];
            snapshot.BoundHwnd = BitConverter.ToUInt32(
                connection.ReadBytes(unchecked(binding.EasyModuleBase + EasyCompatibilityManifest.HwndSlotRva), 4), 0);
            MemoryRegionSnapshot region;
            string queryError;
            if (!connection.TryQueryMemory(EasyCompatibilityManifest.ProtectedPageAddress, out region, out queryError))
                throw new InvalidOperationException(queryError ?? "VirtualQueryEx failed for the Easy protection page.");
            snapshot.ProtectedPageBase = region.BaseAddress;
            snapshot.ProtectedRegionSize = region.RegionSize;
            snapshot.ProtectedPageState = region.State;
            snapshot.ProtectedPageProtection = region.Protect;
            snapshot.IdleSlotValue = BitConverter.ToUInt32(connection.ReadBytes(EasyCompatibilityManifest.IdleSlotAddress, 4), 0);
            snapshot.OriginalIdlePrefix = connection.ReadBytes(
                EasyCompatibilityManifest.OriginalIdleAddress,
                EasyCompatibilityManifest.OriginalIdlePrefix.Length);
            snapshot.IdleCallerAnchor = connection.ReadBytes(
                EasyCompatibilityManifest.IdleCallerAnchorAddress,
                EasyCompatibilityManifest.IdleCallerAnchor.Length);
            return snapshot;
        }

        private static bool RevalidateExternalIdentity(EasyRuntimeBinding binding)
        {
            ConflictScanDiagnostic fresh = new San9Pk101ConflictDetector().Scan(
                binding.GameProcessId,
                San9Pk101Target.ExpectedExecutablePath);
            return IsFreshScanExact(binding, fresh);
        }

        internal static bool IsFreshScanExact(
            EasyRuntimeBinding binding,
            ConflictScanDiagnostic fresh)
        {
            if (binding == null || fresh == null
                || !fresh.ProcessScanSucceeded
                || !fresh.ModuleScanAttempted
                || !fresh.ModuleScanSucceeded
                || !San9Pk101ConflictDetector.ContainsOnlyExactCompatibleEasy(fresh))
                return false;
            ConflictDiagnostic process = fresh.Conflicts.Single(item =>
                string.Equals(item.Code, "COMPATIBLE_EASY_PROCESS", StringComparison.Ordinal));
            ConflictDiagnostic module = fresh.Conflicts.Single(item =>
                string.Equals(item.Code, "COMPATIBLE_EASY_MODULE", StringComparison.Ordinal));
            return process.ProcessId == binding.LoaderProcessId
                && process.ProcessCreationFileTimeUtc == binding.LoaderCreationFileTimeUtc
                && process.FileSize == binding.LoaderFileSize
                && string.Equals(process.FileSha256, binding.LoaderFileSha256, StringComparison.OrdinalIgnoreCase)
                && San9Pk101TargetValidator.PathsEqual(process.Path, binding.LoaderPath)
                && module.ProcessId == binding.GameProcessId
                && module.ModuleBaseAddress == binding.EasyModuleBase
                && module.ModuleImageSize == binding.EasyModuleImageSize
                && module.FileSize == binding.EasyModuleFileSize
                && string.Equals(module.FileSha256, binding.EasyModuleFileSha256, StringComparison.OrdinalIgnoreCase)
                && San9Pk101TargetValidator.PathsEqual(module.Path, binding.EasyModulePath);
        }

        private static EasyRuntimeCompatibilityReport Failure(
            EasyRuntimeCompatibilityState state,
            string code,
            string message)
        {
            return new EasyRuntimeCompatibilityReport
            {
                State = state,
                Issues = new[] { new DiagnosticIssue(code, DiagnosticSeverity.Blocking, message) }
            };
        }
    }

}
