using System;

namespace San9AutoDomestic.Bridge.Protocol
{
    public sealed class ProtocolFrame
    {
        private ProtocolFrame(
            ProtocolFrameKind kind,
            ProtocolRequestState state,
            FixedValue sessionNonce,
            ulong sequence,
            FixedValue requestId,
            FixedValue targetIdentityDigest,
            FixedValue contextTokenDigest,
            long createdAtUtcTicks,
            long expiresAtUtcTicks,
            uint operationCode,
            uint resultCode,
            FixedValue requestPayloadDigest,
            FixedValue resultPayloadDigest,
            FixedValue requestBindingDigest)
        {
            Kind = kind;
            State = state;
            SessionNonce = sessionNonce;
            Sequence = sequence;
            RequestId = requestId;
            TargetIdentityDigest = targetIdentityDigest;
            ContextTokenDigest = contextTokenDigest;
            CreatedAtUtcTicks = createdAtUtcTicks;
            ExpiresAtUtcTicks = expiresAtUtcTicks;
            OperationCode = operationCode;
            ResultCode = resultCode;
            RequestPayloadDigest = requestPayloadDigest;
            ResultPayloadDigest = resultPayloadDigest;
            RequestBindingDigest = requestBindingDigest;
        }

        public ProtocolFrameKind Kind { get; private set; }

        public ProtocolRequestState State { get; private set; }

        public FixedValue SessionNonce { get; private set; }

        public ulong Sequence { get; private set; }

        public FixedValue RequestId { get; private set; }

        public FixedValue TargetIdentityDigest { get; private set; }

        public FixedValue ContextTokenDigest { get; private set; }

        public long CreatedAtUtcTicks { get; private set; }

        public long ExpiresAtUtcTicks { get; private set; }

        public uint OperationCode { get; private set; }

        public uint ResultCode { get; private set; }

        public FixedValue RequestPayloadDigest { get; private set; }

        public FixedValue ResultPayloadDigest { get; private set; }

        public FixedValue RequestBindingDigest { get; private set; }

        public bool IsExecutionAuthorized
        {
            get { return false; }
        }

        public static ProtocolFrame CreateRequest(
            FixedValue sessionNonce,
            ulong sequence,
            FixedValue requestId,
            FixedValue targetIdentityDigest,
            FixedValue contextTokenDigest,
            long createdAtUtcTicks,
            long expiresAtUtcTicks,
            uint operationCode,
            FixedValue requestPayloadDigest)
        {
            Require(sessionNonce, ProtocolWireLayout.NonceSize, "sessionNonce");
            Require(requestId, ProtocolWireLayout.RequestIdSize, "requestId");
            Require(targetIdentityDigest, ProtocolWireLayout.DigestSize, "targetIdentityDigest");
            Require(contextTokenDigest, ProtocolWireLayout.DigestSize, "contextTokenDigest");
            Require(requestPayloadDigest, ProtocolWireLayout.DigestSize, "requestPayloadDigest");
            if (sequence == 0)
            {
                throw new ArgumentOutOfRangeException("sequence");
            }

            if (operationCode == 0)
            {
                throw new ArgumentOutOfRangeException("operationCode");
            }

            return new ProtocolFrame(
                ProtocolFrameKind.Request,
                ProtocolRequestState.Pending,
                sessionNonce,
                sequence,
                requestId,
                targetIdentityDigest,
                contextTokenDigest,
                createdAtUtcTicks,
                expiresAtUtcTicks,
                operationCode,
                0,
                requestPayloadDigest,
                null,
                null);
        }

        internal static ProtocolFrame CreateResult(
            ProtocolFrame request,
            ProtocolRequestState state,
            uint resultCode,
            FixedValue resultPayloadDigest,
            FixedValue requestBindingDigest)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            Require(resultPayloadDigest, ProtocolWireLayout.DigestSize, "resultPayloadDigest");
            Require(requestBindingDigest, ProtocolWireLayout.DigestSize, "requestBindingDigest");
            if (state != ProtocolRequestState.Completed && state != ProtocolRequestState.Rejected)
            {
                throw new ArgumentOutOfRangeException("state");
            }

            if (state == ProtocolRequestState.Completed && resultCode != 0)
            {
                throw new ArgumentException("Completed results must use result code zero.", "resultCode");
            }

            if (state == ProtocolRequestState.Rejected && resultCode == 0)
            {
                throw new ArgumentException("Rejected results require a nonzero result code.", "resultCode");
            }

            return new ProtocolFrame(
                ProtocolFrameKind.Result,
                state,
                request.SessionNonce,
                request.Sequence,
                request.RequestId,
                request.TargetIdentityDigest,
                request.ContextTokenDigest,
                request.CreatedAtUtcTicks,
                request.ExpiresAtUtcTicks,
                request.OperationCode,
                resultCode,
                request.RequestPayloadDigest,
                resultPayloadDigest,
                requestBindingDigest);
        }

        internal static ProtocolFrame CreateClaim(
            ProtocolFrame request,
            FixedValue requestBindingDigest)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            Require(requestBindingDigest, ProtocolWireLayout.DigestSize, "requestBindingDigest");
            return new ProtocolFrame(
                ProtocolFrameKind.Claim,
                ProtocolRequestState.Claimed,
                request.SessionNonce,
                request.Sequence,
                request.RequestId,
                request.TargetIdentityDigest,
                request.ContextTokenDigest,
                request.CreatedAtUtcTicks,
                request.ExpiresAtUtcTicks,
                request.OperationCode,
                0,
                request.RequestPayloadDigest,
                null,
                requestBindingDigest);
        }

        internal static ProtocolFrame FromDecoded(
            ProtocolFrameKind kind,
            ProtocolRequestState state,
            FixedValue sessionNonce,
            ulong sequence,
            FixedValue requestId,
            FixedValue targetIdentityDigest,
            FixedValue contextTokenDigest,
            long createdAtUtcTicks,
            long expiresAtUtcTicks,
            uint operationCode,
            uint resultCode,
            FixedValue requestPayloadDigest,
            FixedValue resultPayloadDigest,
            FixedValue requestBindingDigest)
        {
            return new ProtocolFrame(
                kind,
                state,
                sessionNonce,
                sequence,
                requestId,
                targetIdentityDigest,
                contextTokenDigest,
                createdAtUtcTicks,
                expiresAtUtcTicks,
                operationCode,
                resultCode,
                requestPayloadDigest,
                resultPayloadDigest,
                requestBindingDigest);
        }

        private static void Require(FixedValue value, int expectedLength, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (value.Length != expectedLength)
            {
                throw new ArgumentException("Unexpected fixed-value width.", parameterName);
            }
        }
    }
}
