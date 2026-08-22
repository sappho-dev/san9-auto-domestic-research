using System;

namespace San9AutoDomestic.P1Wire
{
    public sealed class P1WireFrame
    {
        public P1WireFrame()
        {
            SessionNonce = new byte[P1WireLayout.NonceSize];
            RequestId = new byte[P1WireLayout.NonceSize];
            EasyEpochNonce = new byte[P1WireLayout.NonceSize];
            BuildDigest = new byte[P1WireLayout.DigestSize];
            ProfileDigest = new byte[P1WireLayout.DigestSize];
            ManifestDigest = new byte[P1WireLayout.DigestSize];
            EasyEpochDigest = new byte[P1WireLayout.DigestSize];
            EasyTicketDigest = new byte[P1WireLayout.DigestSize];
            ContextDigest = new byte[P1WireLayout.DigestSize];
            BridgeDigest = new byte[P1WireLayout.DigestSize];
            MappingDigest = new byte[P1WireLayout.DigestSize];
            ChallengeDigest = new byte[P1WireLayout.DigestSize];
            ResultDigest = new byte[P1WireLayout.DigestSize];
        }

        public P1WireKind Kind { get; set; }
        public P1WireState State { get; set; }
        public uint Flags { get; set; }
        public uint ResultCode { get; set; }
        public ulong Sequence { get; set; }
        public ulong IssuedAtMilliseconds { get; set; }
        public ulong ExpiresAtMilliseconds { get; set; }
        public uint GameProcessId { get; set; }
        public uint MainThreadId { get; set; }
        public uint GameWindowHandle { get; set; }
        public uint HelperProcessId { get; set; }
        public uint EasyLoaderProcessId { get; set; }
        public ulong GameCreationTime { get; set; }
        public ulong HelperCreationTime { get; set; }
        public ulong EasyLoaderCreationTime { get; set; }
        public byte[] SessionNonce { get; set; }
        public byte[] RequestId { get; set; }
        public byte[] EasyEpochNonce { get; set; }
        public byte[] BuildDigest { get; set; }
        public byte[] ProfileDigest { get; set; }
        public byte[] ManifestDigest { get; set; }
        public byte[] EasyEpochDigest { get; set; }
        public byte[] EasyTicketDigest { get; set; }
        public byte[] ContextDigest { get; set; }
        public byte[] BridgeDigest { get; set; }
        public byte[] MappingDigest { get; set; }
        public byte[] ChallengeDigest { get; set; }
        public byte[] ResultDigest { get; set; }

        public P1WireFrame Clone()
        {
            return new P1WireFrame
            {
                Kind = Kind,
                State = State,
                Flags = Flags,
                ResultCode = ResultCode,
                Sequence = Sequence,
                IssuedAtMilliseconds = IssuedAtMilliseconds,
                ExpiresAtMilliseconds = ExpiresAtMilliseconds,
                GameProcessId = GameProcessId,
                MainThreadId = MainThreadId,
                GameWindowHandle = GameWindowHandle,
                HelperProcessId = HelperProcessId,
                EasyLoaderProcessId = EasyLoaderProcessId,
                GameCreationTime = GameCreationTime,
                HelperCreationTime = HelperCreationTime,
                EasyLoaderCreationTime = EasyLoaderCreationTime,
                SessionNonce = Copy(SessionNonce),
                RequestId = Copy(RequestId),
                EasyEpochNonce = Copy(EasyEpochNonce),
                BuildDigest = Copy(BuildDigest),
                ProfileDigest = Copy(ProfileDigest),
                ManifestDigest = Copy(ManifestDigest),
                EasyEpochDigest = Copy(EasyEpochDigest),
                EasyTicketDigest = Copy(EasyTicketDigest),
                ContextDigest = Copy(ContextDigest),
                BridgeDigest = Copy(BridgeDigest),
                MappingDigest = Copy(MappingDigest),
                ChallengeDigest = Copy(ChallengeDigest),
                ResultDigest = Copy(ResultDigest)
            };
        }

        private static byte[] Copy(byte[] value)
        {
            return value == null ? null : (byte[])value.Clone();
        }
    }
}
