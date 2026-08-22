using System;
using System.Collections.Generic;

namespace San9AutoDomestic.Bridge.Protocol
{
    public sealed class RequestClaim
    {
        internal RequestClaim(ProtocolFrame request, FixedValue bindingDigest)
        {
            Request = request;
            RequestBindingDigest = bindingDigest;
            ClaimFrame = ProtocolFrame.CreateClaim(request, bindingDigest);
        }

        public ProtocolFrame Request { get; private set; }

        public FixedValue RequestBindingDigest { get; private set; }

        public ProtocolFrame ClaimFrame { get; private set; }

        public bool IsExecutionAuthorized
        {
            get { return false; }
        }
    }

    public sealed class BridgeProtocolStateMachine
    {
        private readonly object sync = new object();
        private readonly FixedValue sessionNonce;
        private readonly FixedValue targetIdentityDigest;
        private readonly FixedValue contextTokenDigest;
        private readonly IProtocolClock clock;
        private readonly HashSet<string> seenRequestIds = new HashSet<string>(StringComparer.Ordinal);
        private ProtocolFrame activeRequest;
        private ProtocolRequestState? activeState;
        private RequestClaim activeClaim;
        private ulong lastAcceptedSequence;
        private bool aborted;
        private bool lastTerminalPassedThroughClaimed;
        private bool hasObservedClock;
        private long lastObservedClockTicks;

        public BridgeProtocolStateMachine(
            FixedValue sessionNonceValue,
            FixedValue targetIdentityDigestValue,
            FixedValue contextTokenDigestValue,
            IProtocolClock protocolClock)
        {
            Require(sessionNonceValue, ProtocolWireLayout.NonceSize, "sessionNonceValue");
            Require(targetIdentityDigestValue, ProtocolWireLayout.DigestSize, "targetIdentityDigestValue");
            Require(contextTokenDigestValue, ProtocolWireLayout.DigestSize, "contextTokenDigestValue");
            if (protocolClock == null)
            {
                throw new ArgumentNullException("protocolClock");
            }

            sessionNonce = sessionNonceValue;
            targetIdentityDigest = targetIdentityDigestValue;
            contextTokenDigest = contextTokenDigestValue;
            clock = protocolClock;
        }

        public bool IsExecutionAuthorized
        {
            get { return false; }
        }

        public bool IsAborted
        {
            get
            {
                lock (sync)
                {
                    return aborted;
                }
            }
        }

        public bool HasUnresolvedRequest
        {
            get
            {
                lock (sync)
                {
                    return activeRequest != null;
                }
            }
        }

        public ProtocolRequestState? ActiveState
        {
            get
            {
                lock (sync)
                {
                    return activeState;
                }
            }
        }

        public ulong LastAcceptedSequence
        {
            get
            {
                lock (sync)
                {
                    return lastAcceptedSequence;
                }
            }
        }

        public bool LastTerminalPassedThroughClaimed
        {
            get
            {
                lock (sync)
                {
                    return lastTerminalPassedThroughClaimed;
                }
            }
        }

        public bool TrySubmit(ProtocolFrame request, out ProtocolRejection rejection)
        {
            lock (sync)
            {
                rejection = null;
                if (aborted)
                {
                    return Reject(ProtocolRejectionCode.SessionAborted, "The session has been aborted.", out rejection);
                }

                if (request == null
                    || request.Kind != ProtocolFrameKind.Request
                    || request.State != ProtocolRequestState.Pending)
                {
                    return Reject(ProtocolRejectionCode.InvalidFrame, "Only a pending request frame can be submitted.", out rejection);
                }

                if (!sessionNonce.Equals(request.SessionNonce))
                {
                    return Reject(ProtocolRejectionCode.CrossSession, "The session nonce does not match.", out rejection);
                }

                if (!targetIdentityDigest.Equals(request.TargetIdentityDigest))
                {
                    return Reject(ProtocolRejectionCode.TargetIdentityMismatch, "The exact-target digest does not match.", out rejection);
                }

                if (!contextTokenDigest.Equals(request.ContextTokenDigest))
                {
                    return Reject(ProtocolRejectionCode.ContextTokenMismatch, "The context-token digest does not match.", out rejection);
                }

                string requestId = request.RequestId.ToHex();
                if (seenRequestIds.Contains(requestId))
                {
                    return Reject(ProtocolRejectionCode.DuplicateRequest, "The request identifier has already been accepted.", out rejection);
                }

                if (activeRequest != null)
                {
                    return Reject(ProtocolRejectionCode.OutstandingRequestExists, "Only one unresolved request is allowed.", out rejection);
                }

                if (lastAcceptedSequence == ulong.MaxValue)
                {
                    return Reject(ProtocolRejectionCode.SequenceExhausted, "The sequence space is exhausted.", out rejection);
                }

                ulong expectedSequence = lastAcceptedSequence + 1;
                if (request.Sequence <= lastAcceptedSequence)
                {
                    return Reject(ProtocolRejectionCode.SequenceReplay, "The sequence is stale or replayed.", out rejection);
                }

                if (request.Sequence != expectedSequence)
                {
                    return Reject(ProtocolRejectionCode.SequenceGap, "The next sequence must be accepted without a gap.", out rejection);
                }

                long lifetime;
                try
                {
                    lifetime = checked(request.ExpiresAtUtcTicks - request.CreatedAtUtcTicks);
                }
                catch (OverflowException)
                {
                    return Reject(ProtocolRejectionCode.LifetimeInvalid, "The request lifetime overflows.", out rejection);
                }

                if (request.CreatedAtUtcTicks <= 0
                    || lifetime <= 0
                    || lifetime > ProtocolWireLayout.MaximumRequestLifetimeTicks)
                {
                    return Reject(ProtocolRejectionCode.LifetimeInvalid, "The request lifetime is outside the protocol bound.", out rejection);
                }

                long now;
                if (!TryObserveClock(out now, out rejection))
                {
                    return false;
                }

                if (request.CreatedAtUtcTicks > now)
                {
                    return Reject(ProtocolRejectionCode.RequestFromFuture, "The request creation time is in the future.", out rejection);
                }

                if (request.ExpiresAtUtcTicks <= now)
                {
                    return Reject(ProtocolRejectionCode.RequestExpired, "The request has expired.", out rejection);
                }

                seenRequestIds.Add(requestId);
                lastAcceptedSequence = request.Sequence;
                activeRequest = request;
                activeState = ProtocolRequestState.Pending;
                activeClaim = null;
                lastTerminalPassedThroughClaimed = false;
                return true;
            }
        }

        public bool TryClaim(
            FixedValue presentedSessionNonce,
            FixedValue presentedRequestId,
            ulong presentedSequence,
            out RequestClaim claim,
            out ProtocolRejection rejection)
        {
            lock (sync)
            {
                claim = null;
                rejection = null;
                if (aborted)
                {
                    return Reject(ProtocolRejectionCode.SessionAborted, "The session has been aborted.", out rejection);
                }

                if (!sessionNonce.Equals(presentedSessionNonce))
                {
                    return Reject(ProtocolRejectionCode.CrossSession, "The claim session nonce does not match.", out rejection);
                }

                if (activeRequest == null)
                {
                    return Reject(ProtocolRejectionCode.NoOutstandingRequest, "There is no request to claim.", out rejection);
                }

                if (activeState != ProtocolRequestState.Pending)
                {
                    return Reject(ProtocolRejectionCode.InvalidStateTransition, "Only a pending request can be claimed.", out rejection);
                }

                if (!activeRequest.RequestId.Equals(presentedRequestId)
                    || activeRequest.Sequence != presentedSequence)
                {
                    return Reject(ProtocolRejectionCode.RequestBindingMismatch, "The claim is not bound to the outstanding request.", out rejection);
                }

                long now;
                if (!TryObserveClock(out now, out rejection))
                {
                    return false;
                }

                if (activeRequest.ExpiresAtUtcTicks <= now)
                {
                    return Reject(ProtocolRejectionCode.RequestExpired, "The outstanding request expired before claim.", out rejection);
                }

                activeClaim = new RequestClaim(
                    activeRequest,
                    ProtocolFrameCodec.ComputeRequestBindingDigest(activeRequest));
                activeState = ProtocolRequestState.Claimed;
                lastTerminalPassedThroughClaimed = true;
                claim = activeClaim;
                return true;
            }
        }

        public bool TryComplete(
            RequestClaim claim,
            FixedValue resultPayloadDigest,
            out ProtocolFrame result,
            out ProtocolRejection rejection)
        {
            lock (sync)
            {
                result = null;
                rejection = null;
                if (!TryValidateClaim(claim, out rejection))
                {
                    return false;
                }

                long now;
                if (!TryObserveClock(out now, out rejection))
                {
                    return false;
                }

                if (activeRequest.ExpiresAtUtcTicks <= now)
                {
                    if (!IsDigest(resultPayloadDigest))
                    {
                        return Reject(ProtocolRejectionCode.InvalidFrame, "A 32-byte terminal result digest is required.", out rejection);
                    }

                    result = ProtocolFrame.CreateResult(
                        activeRequest,
                        ProtocolRequestState.Rejected,
                        (uint)ProtocolRejectionCode.RequestExpired,
                        resultPayloadDigest,
                        activeClaim.RequestBindingDigest);
                    ClearTerminal();
                    return Reject(ProtocolRejectionCode.RequestExpired, "The claimed request expired before completion and was rejected.", out rejection);
                }

                if (!IsDigest(resultPayloadDigest))
                {
                    return Reject(ProtocolRejectionCode.InvalidFrame, "A 32-byte completion result digest is required.", out rejection);
                }

                result = ProtocolFrame.CreateResult(
                    activeRequest,
                    ProtocolRequestState.Completed,
                    0,
                    resultPayloadDigest,
                    activeClaim.RequestBindingDigest);
                ClearTerminal();
                return true;
            }
        }

        public bool TryReject(
            RequestClaim claim,
            ProtocolRejectionCode resultCode,
            FixedValue resultPayloadDigest,
            out ProtocolFrame result,
            out ProtocolRejection rejection)
        {
            lock (sync)
            {
                result = null;
                rejection = null;
                if (resultCode == ProtocolRejectionCode.None)
                {
                    return Reject(ProtocolRejectionCode.InvalidStateTransition, "A rejected result requires a nonzero code.", out rejection);
                }

                if (!TryValidateClaim(claim, out rejection))
                {
                    return false;
                }

                if (!IsDigest(resultPayloadDigest))
                {
                    return Reject(ProtocolRejectionCode.InvalidFrame, "A 32-byte rejection result digest is required.", out rejection);
                }

                result = ProtocolFrame.CreateResult(
                    activeRequest,
                    ProtocolRequestState.Rejected,
                    (uint)resultCode,
                    resultPayloadDigest,
                    activeClaim.RequestBindingDigest);
                ClearTerminal();
                return true;
            }
        }

        public bool TryAbortSession(
            FixedValue presentedSessionNonce,
            FixedValue rejectionPayloadDigest,
            out ProtocolFrame rejectedOutstandingResult,
            out ProtocolRejection rejection)
        {
            lock (sync)
            {
                rejectedOutstandingResult = null;
                rejection = null;
                if (aborted)
                {
                    return Reject(ProtocolRejectionCode.SessionAborted, "The session was already aborted.", out rejection);
                }

                if (!sessionNonce.Equals(presentedSessionNonce))
                {
                    return Reject(ProtocolRejectionCode.CrossSession, "A cross-session abort was rejected.", out rejection);
                }

                if (!IsDigest(rejectionPayloadDigest))
                {
                    return Reject(ProtocolRejectionCode.InvalidFrame, "A 32-byte abort result digest is required.", out rejection);
                }

                if (activeRequest != null)
                {
                    if (activeState == ProtocolRequestState.Pending)
                    {
                        activeState = ProtocolRequestState.Claimed;
                        activeClaim = new RequestClaim(
                            activeRequest,
                            ProtocolFrameCodec.ComputeRequestBindingDigest(activeRequest));
                        lastTerminalPassedThroughClaimed = true;
                    }

                    rejectedOutstandingResult = ProtocolFrame.CreateResult(
                        activeRequest,
                        ProtocolRequestState.Rejected,
                        (uint)ProtocolRejectionCode.SessionAborted,
                        rejectionPayloadDigest,
                        activeClaim.RequestBindingDigest);
                    ClearTerminal();
                }

                aborted = true;
                return true;
            }
        }

        public bool TryRejectExpired(
            FixedValue rejectionPayloadDigest,
            out ProtocolFrame rejectedResult,
            out ProtocolRejection rejection)
        {
            lock (sync)
            {
                rejectedResult = null;
                rejection = null;
                if (aborted)
                {
                    return Reject(ProtocolRejectionCode.SessionAborted, "The session has been aborted.", out rejection);
                }

                if (activeRequest == null)
                {
                    return Reject(ProtocolRejectionCode.NoOutstandingRequest, "There is no request to expire.", out rejection);
                }

                long now;
                if (!TryObserveClock(out now, out rejection))
                {
                    return false;
                }

                if (activeRequest.ExpiresAtUtcTicks > now)
                {
                    return Reject(ProtocolRejectionCode.InvalidStateTransition, "The outstanding request has not expired.", out rejection);
                }

                if (!IsDigest(rejectionPayloadDigest))
                {
                    return Reject(ProtocolRejectionCode.InvalidFrame, "A 32-byte expiry result digest is required.", out rejection);
                }

                if (activeState == ProtocolRequestState.Pending)
                {
                    activeState = ProtocolRequestState.Claimed;
                    activeClaim = new RequestClaim(
                        activeRequest,
                        ProtocolFrameCodec.ComputeRequestBindingDigest(activeRequest));
                    lastTerminalPassedThroughClaimed = true;
                }

                if (activeState != ProtocolRequestState.Claimed || activeClaim == null)
                {
                    return Reject(ProtocolRejectionCode.InvalidStateTransition, "The outstanding request cannot be expired from its current state.", out rejection);
                }

                rejectedResult = ProtocolFrame.CreateResult(
                    activeRequest,
                    ProtocolRequestState.Rejected,
                    (uint)ProtocolRejectionCode.RequestExpired,
                    rejectionPayloadDigest,
                    activeClaim.RequestBindingDigest);
                ClearTerminal();
                return true;
            }
        }

        private bool TryValidateClaim(RequestClaim claim, out ProtocolRejection rejection)
        {
            rejection = null;
            if (aborted)
            {
                return Reject(ProtocolRejectionCode.SessionAborted, "The session has been aborted.", out rejection);
            }

            if (activeRequest == null || activeState != ProtocolRequestState.Claimed || activeClaim == null)
            {
                return Reject(ProtocolRejectionCode.InvalidStateTransition, "There is no claimed request to finalize.", out rejection);
            }

            if (!ReferenceEquals(activeClaim, claim)
                || !activeClaim.RequestBindingDigest.Equals(ProtocolFrameCodec.ComputeRequestBindingDigest(activeRequest)))
            {
                return Reject(ProtocolRejectionCode.RequestBindingMismatch, "The result claim is not bound to this request.", out rejection);
            }

            return true;
        }

        private void ClearTerminal()
        {
            activeRequest = null;
            activeState = null;
            activeClaim = null;
        }

        private bool TryObserveClock(out long now, out ProtocolRejection rejection)
        {
            now = clock.UtcNowTicks;
            rejection = null;
            if (now <= 0)
            {
                return Reject(ProtocolRejectionCode.ClockInvalid, "The protocol clock returned an invalid UTC tick value.", out rejection);
            }

            if (hasObservedClock && now < lastObservedClockTicks)
            {
                return Reject(ProtocolRejectionCode.ClockRegressed, "The protocol clock moved backwards.", out rejection);
            }

            lastObservedClockTicks = now;
            hasObservedClock = true;
            return true;
        }

        private static bool Reject(ProtocolRejectionCode code, string message, out ProtocolRejection rejection)
        {
            rejection = new ProtocolRejection(code, message);
            return false;
        }

        private static bool IsDigest(FixedValue value)
        {
            return value != null && value.Length == ProtocolWireLayout.DigestSize;
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
