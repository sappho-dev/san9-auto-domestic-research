namespace San9AutoDomestic.P1Wire
{
    public enum P1WireKind : ushort
    {
        PingRequest = 1,
        PingResponse = 2
    }

    public enum P1WireState : ushort
    {
        Pending = 1,
        Completed = 2,
        Rejected = 3
    }

    public enum P1WireDecodeStatus
    {
        Accepted = 0,
        NullFrame = 1,
        SizeMismatch = 2,
        MagicMismatch = 3,
        SchemaMismatch = 4,
        DeclaredSizeMismatch = 5,
        CrcMismatch = 6,
        HmacMismatch = 7,
        ReservedOrFlagsInvalid = 8,
        KindStateInvalid = 9,
        RequiredFieldInvalid = 10,
        LifetimeInvalid = 11,
        RequestShapeInvalid = 12,
        ResponseShapeInvalid = 13,
        KeyInvalid = 14
    }

    public enum P1WireGateStatus
    {
        Accepted = 0,
        WrongShape = 1,
        BindingMismatch = 2,
        Replay = 3,
        SequenceGap = 4,
        RequestIdReplay = 5,
        Expired = 6,
        FromFuture = 7,
        BudgetExhausted = 8,
        ClockRollback = 9,
        ClockFaulted = 10,
        AuthenticatedFrameRejected = 11
    }

    public static class P1WireContract
    {
        public const bool LiveAuthorization = false;
        public const bool ContainsBusinessFields = false;
        public const bool ContainsNativeAddresses = false;
    }
}
