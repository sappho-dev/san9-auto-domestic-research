using System;
using System.Text;

namespace San9AutoDomestic.Bridge.Protocol
{
    public sealed class ExactTargetIdentity
    {
        private const uint IdentityMagic = 0x49543953U; // "S9TI"
        private const int CanonicalSize = 96;
        private readonly FixedValue executableSha256;
        private readonly FixedValue executablePathDigest;

        public ExactTargetIdentity(
            string canonicalExecutablePath,
            byte[] executableSha256Value,
            uint expectedImageBase,
            uint expectedImageSize,
            ulong expectedFileSize,
            ushort versionMajor,
            ushort versionMinor,
            ushort versionBuild,
            ushort versionRevision)
        {
            if (string.IsNullOrWhiteSpace(canonicalExecutablePath))
            {
                throw new ArgumentException("A canonical executable path is required.", "canonicalExecutablePath");
            }

            executableSha256 = FixedValue.Digest(executableSha256Value);
            string normalizedPath = canonicalExecutablePath
                .Replace('/', '\\')
                .Normalize(NormalizationForm.FormC)
                .ToUpperInvariant();
            executablePathDigest = FixedValue.Sha256(Encoding.UTF8.GetBytes(normalizedPath));
            ExpectedImageBase = expectedImageBase;
            ExpectedImageSize = expectedImageSize;
            ExpectedFileSize = expectedFileSize;
            VersionMajor = versionMajor;
            VersionMinor = versionMinor;
            VersionBuild = versionBuild;
            VersionRevision = versionRevision;
        }

        public uint ExpectedImageBase { get; private set; }

        public uint ExpectedImageSize { get; private set; }

        public ulong ExpectedFileSize { get; private set; }

        public ushort VersionMajor { get; private set; }

        public ushort VersionMinor { get; private set; }

        public ushort VersionBuild { get; private set; }

        public ushort VersionRevision { get; private set; }

        public FixedValue ComputeDigest()
        {
            byte[] canonical = new byte[CanonicalSize];
            LittleEndian.WriteUInt32(canonical, 0, IdentityMagic);
            LittleEndian.WriteUInt16(canonical, 4, 1);
            LittleEndian.WriteUInt16(canonical, 6, CanonicalSize);
            Buffer.BlockCopy(executableSha256.ToArray(), 0, canonical, 8, ProtocolWireLayout.DigestSize);
            LittleEndian.WriteUInt32(canonical, 40, ExpectedImageBase);
            LittleEndian.WriteUInt32(canonical, 44, ExpectedImageSize);
            LittleEndian.WriteUInt64(canonical, 48, ExpectedFileSize);
            LittleEndian.WriteUInt16(canonical, 56, VersionMajor);
            LittleEndian.WriteUInt16(canonical, 58, VersionMinor);
            LittleEndian.WriteUInt16(canonical, 60, VersionBuild);
            LittleEndian.WriteUInt16(canonical, 62, VersionRevision);
            Buffer.BlockCopy(executablePathDigest.ToArray(), 0, canonical, 64, ProtocolWireLayout.DigestSize);
            return FixedValue.Sha256(canonical);
        }
    }
}
