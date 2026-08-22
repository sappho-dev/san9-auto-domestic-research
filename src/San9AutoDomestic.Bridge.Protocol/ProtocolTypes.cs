using System;

namespace San9AutoDomestic.Bridge.Protocol
{
    public enum ProtocolFrameKind : ushort
    {
        Request = 1,
        Claim = 2,
        Result = 3
    }

    public enum ProtocolRequestState : ushort
    {
        Pending = 1,
        Claimed = 2,
        Completed = 3,
        Rejected = 4
    }

    public enum ProtocolRejectionCode : uint
    {
        None = 0,
        InvalidFrame = 1,
        CrossSession = 2,
        TargetIdentityMismatch = 3,
        ContextTokenMismatch = 4,
        SequenceReplay = 5,
        SequenceGap = 6,
        DuplicateRequest = 7,
        RequestExpired = 8,
        RequestFromFuture = 9,
        LifetimeInvalid = 10,
        OutstandingRequestExists = 11,
        NoOutstandingRequest = 12,
        RequestBindingMismatch = 13,
        InvalidStateTransition = 14,
        SessionAborted = 15,
        SequenceExhausted = 16,
        TransportFull = 17,
        ClockInvalid = 18,
        ClockRegressed = 19
    }

    public sealed class ProtocolRejection
    {
        public ProtocolRejection(ProtocolRejectionCode code, string message)
        {
            if (code == ProtocolRejectionCode.None)
            {
                throw new ArgumentOutOfRangeException("code");
            }

            Code = code;
            Message = message ?? string.Empty;
        }

        public ProtocolRejectionCode Code { get; private set; }

        public string Message { get; private set; }
    }

    public interface IProtocolClock
    {
        long UtcNowTicks { get; }
    }

    public sealed class SystemProtocolClock : IProtocolClock
    {
        public long UtcNowTicks
        {
            get { return DateTime.UtcNow.Ticks; }
        }
    }
}
