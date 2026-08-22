namespace San9AutoDomestic.Bridge.Protocol
{
    public static class ProtocolWireLayout
    {
        public const uint Magic = 0x50423953U; // "S9BP" in little-endian byte order.
        public const ushort SchemaMajor = 1;
        public const ushort SchemaMinor = 0;
        public const int FrameSize = 288;

        public const int MagicOffset = 0;
        public const int SchemaMajorOffset = 4;
        public const int SchemaMinorOffset = 6;
        public const int FrameSizeOffset = 8;
        public const int FrameKindOffset = 12;
        public const int StateOffset = 14;
        public const int ChecksumOffset = 16;
        public const int FlagsOffset = 20;
        public const int ReservedHeaderOffset = 24;
        public const int ReservedHeaderSize = 8;
        public const int SessionNonceOffset = 32;
        public const int SequenceOffset = 48;
        public const int RequestIdOffset = 56;
        public const int TargetIdentityDigestOffset = 72;
        public const int ContextTokenDigestOffset = 104;
        public const int CreatedAtUtcTicksOffset = 136;
        public const int ExpiresAtUtcTicksOffset = 144;
        public const int OperationCodeOffset = 152;
        public const int ResultCodeOffset = 156;
        public const int RequestPayloadDigestOffset = 160;
        public const int ResultPayloadDigestOffset = 192;
        public const int RequestBindingDigestOffset = 224;
        public const int ReservedTailOffset = 256;
        public const int ReservedTailSize = 32;

        public const int NonceSize = 16;
        public const int RequestIdSize = 16;
        public const int DigestSize = 32;
        public const uint AllowedFlags = 0;
        public const long MaximumRequestLifetimeTicks = 600000000L; // One minute.
    }
}
