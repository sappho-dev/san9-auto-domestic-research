using System;
using System.Collections.Generic;

namespace San9AutoDomestic.P1Wire
{
    public sealed class P1WireRequestGate
    {
        private readonly P1WireFrame binding;
        private readonly List<byte[]> acceptedRequestIds;
        private ulong lastSequence;
        private ulong lastObservedNow;
        private bool hasObservedNow;
        private bool clockFaulted;

        public P1WireRequestGate(P1WireFrame bindingTemplate)
        {
            string error;
            if (!P1WireCodec.ValidateFrame(bindingTemplate, out error)
                || bindingTemplate.Kind != P1WireKind.PingRequest)
            {
                throw new ArgumentException("A valid ping request binding template is required.", "bindingTemplate");
            }

            binding = bindingTemplate.Clone();
            acceptedRequestIds = new List<byte[]>(P1WireLayout.MaximumSessionPings);
        }

        public ulong LastAcceptedSequence { get { return lastSequence; } }
        public ulong LastObservedNow { get { return lastObservedNow; } }
        public int AcceptedCount { get { return acceptedRequestIds.Count; } }
        public bool ClockFaulted { get { return clockFaulted; } }
        public bool LiveAuthorization { get { return false; } }

        public P1WireGateStatus TryDecodeAndAccept(
            byte[] bytes,
            byte[] hmacKey,
            ulong nowMilliseconds,
            out P1WireDecodeStatus decodeStatus,
            out P1WireFrame acceptedRequest)
        {
            acceptedRequest = null;
            P1WireFrame decoded;
            decodeStatus = P1WireCodec.TryDecode(bytes, hmacKey, out decoded);
            if (decodeStatus != P1WireDecodeStatus.Accepted)
            {
                return P1WireGateStatus.AuthenticatedFrameRejected;
            }

            P1WireGateStatus status = TryAccept(decoded, nowMilliseconds);
            if (status == P1WireGateStatus.Accepted)
            {
                acceptedRequest = decoded;
            }
            return status;
        }

        internal P1WireGateStatus TryAccept(P1WireFrame request, ulong nowMilliseconds)
        {
            if (clockFaulted)
            {
                return P1WireGateStatus.ClockFaulted;
            }

            if (hasObservedNow && nowMilliseconds < lastObservedNow)
            {
                clockFaulted = true;
                return P1WireGateStatus.ClockRollback;
            }

            hasObservedNow = true;
            lastObservedNow = nowMilliseconds;
            string validationError;
            if (!P1WireCodec.ValidateFrame(request, out validationError)
                || request.Kind != P1WireKind.PingRequest
                || request.State != P1WireState.Pending)
            {
                return P1WireGateStatus.WrongShape;
            }

            if (!BindingMatches(binding, request))
            {
                return P1WireGateStatus.BindingMismatch;
            }

            if (acceptedRequestIds.Count >= P1WireLayout.MaximumSessionPings)
            {
                return P1WireGateStatus.BudgetExhausted;
            }

            if (request.Sequence <= lastSequence)
            {
                return P1WireGateStatus.Replay;
            }

            if (request.Sequence != lastSequence + 1UL)
            {
                return P1WireGateStatus.SequenceGap;
            }

            if (nowMilliseconds < request.IssuedAtMilliseconds)
            {
                return P1WireGateStatus.FromFuture;
            }

            if (nowMilliseconds >= request.ExpiresAtMilliseconds)
            {
                return P1WireGateStatus.Expired;
            }

            for (int index = 0; index < acceptedRequestIds.Count; index++)
            {
                if (P1WireCodec.ConstantTimeEqual(acceptedRequestIds[index], request.RequestId))
                {
                    return P1WireGateStatus.RequestIdReplay;
                }
            }

            acceptedRequestIds.Add((byte[])request.RequestId.Clone());
            lastSequence = request.Sequence;
            return P1WireGateStatus.Accepted;
        }

        internal static bool BindingMatches(P1WireFrame expected, P1WireFrame actual)
        {
            return expected.GameProcessId == actual.GameProcessId
                && expected.MainThreadId == actual.MainThreadId
                && expected.GameWindowHandle == actual.GameWindowHandle
                && expected.HelperProcessId == actual.HelperProcessId
                && expected.EasyLoaderProcessId == actual.EasyLoaderProcessId
                && expected.GameCreationTime == actual.GameCreationTime
                && expected.HelperCreationTime == actual.HelperCreationTime
                && expected.EasyLoaderCreationTime == actual.EasyLoaderCreationTime
                && P1WireCodec.ConstantTimeEqual(expected.SessionNonce, actual.SessionNonce)
                && P1WireCodec.ConstantTimeEqual(expected.EasyEpochNonce, actual.EasyEpochNonce)
                && P1WireCodec.ConstantTimeEqual(expected.BuildDigest, actual.BuildDigest)
                && P1WireCodec.ConstantTimeEqual(expected.ProfileDigest, actual.ProfileDigest)
                && P1WireCodec.ConstantTimeEqual(expected.ManifestDigest, actual.ManifestDigest)
                && P1WireCodec.ConstantTimeEqual(expected.EasyEpochDigest, actual.EasyEpochDigest)
                && P1WireCodec.ConstantTimeEqual(expected.EasyTicketDigest, actual.EasyTicketDigest)
                && P1WireCodec.ConstantTimeEqual(expected.ContextDigest, actual.ContextDigest)
                && P1WireCodec.ConstantTimeEqual(expected.BridgeDigest, actual.BridgeDigest)
                && P1WireCodec.ConstantTimeEqual(expected.MappingDigest, actual.MappingDigest);
        }
    }

    public static class P1WireResponseVerifier
    {
        public static bool TryDecodeAndVerify(
            P1WireFrame request,
            byte[] bytes,
            byte[] hmacKey,
            out P1WireDecodeStatus decodeStatus,
            out P1WireFrame verifiedResponse)
        {
            verifiedResponse = null;
            P1WireFrame decoded;
            decodeStatus = P1WireCodec.TryDecode(bytes, hmacKey, out decoded);
            if (decodeStatus != P1WireDecodeStatus.Accepted || !Verify(request, decoded))
            {
                return false;
            }
            verifiedResponse = decoded;
            return true;
        }

        internal static bool Verify(P1WireFrame request, P1WireFrame response)
        {
            string requestError;
            string responseError;
            if (!P1WireCodec.ValidateFrame(request, out requestError)
                || !P1WireCodec.ValidateFrame(response, out responseError)
                || request.Kind != P1WireKind.PingRequest
                || request.State != P1WireState.Pending
                || response.Kind != P1WireKind.PingResponse
                || (response.State != P1WireState.Completed && response.State != P1WireState.Rejected)
                || !P1WireRequestGate.BindingMatches(request, response)
                || request.Sequence != response.Sequence
                || request.IssuedAtMilliseconds != response.IssuedAtMilliseconds
                || request.ExpiresAtMilliseconds != response.ExpiresAtMilliseconds
                || !P1WireCodec.ConstantTimeEqual(request.RequestId, response.RequestId)
                || !P1WireCodec.ConstantTimeEqual(request.ChallengeDigest, response.ChallengeDigest))
            {
                return false;
            }

            return response.State == P1WireState.Completed
                    ? response.ResultCode == 0U
                    : response.ResultCode != 0U;
        }
    }
}
