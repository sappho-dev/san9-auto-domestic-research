namespace San9AutoDomestic.P1Wire
{
    public static class P1WireLayout
    {
        public const uint Magic = 0x31503953U; // "S9P1" in little-endian byte order.
        public const ushort SchemaMajor = 1;
        public const ushort SchemaMinor = 0;
        public const int FrameSize = 512;
        public const int DigestSize = 32;
        public const int NonceSize = 16;
        public const int HmacKeySize = 32;
        public const ulong MaximumLifetimeMilliseconds = 5000UL;
        public const uint AllowedFlags = 0U;
        public const int MaximumSessionPings = 100;

        public const int MagicOffset = 0;
        public const int SchemaMajorOffset = 4;
        public const int SchemaMinorOffset = 6;
        public const int DeclaredSizeOffset = 8;
        public const int KindOffset = 12;
        public const int StateOffset = 14;
        public const int FlagsOffset = 16;
        public const int Crc32Offset = 20;
        public const int ResultCodeOffset = 24;
        public const int ReservedHeaderOffset = 28;
        public const int ReservedHeaderSize = 4;
        public const int SequenceOffset = 32;
        public const int IssuedAtMillisecondsOffset = 40;
        public const int ExpiresAtMillisecondsOffset = 48;
        public const int GameProcessIdOffset = 56;
        public const int MainThreadIdOffset = 60;
        public const int GameWindowHandleOffset = 64;
        public const int HelperProcessIdOffset = 68;
        public const int EasyLoaderProcessIdOffset = 72;
        public const int ReservedBindingOffset = 76;
        public const int ReservedBindingSize = 4;
        public const int GameCreationTimeOffset = 80;
        public const int HelperCreationTimeOffset = 88;
        public const int EasyLoaderCreationTimeOffset = 96;
        public const int SessionNonceOffset = 104;
        public const int RequestIdOffset = 120;
        public const int EasyEpochNonceOffset = 136;
        public const int BuildDigestOffset = 152;
        public const int ProfileDigestOffset = 184;
        public const int ManifestDigestOffset = 216;
        public const int EasyEpochDigestOffset = 248;
        public const int EasyTicketDigestOffset = 280;
        public const int ContextDigestOffset = 312;
        public const int BridgeDigestOffset = 344;
        public const int MappingDigestOffset = 376;
        public const int ChallengeDigestOffset = 408;
        public const int ResultDigestOffset = 440;
        public const int HmacOffset = 472;
        public const int ReservedTailOffset = 504;
        public const int ReservedTailSize = 8;
    }
}
