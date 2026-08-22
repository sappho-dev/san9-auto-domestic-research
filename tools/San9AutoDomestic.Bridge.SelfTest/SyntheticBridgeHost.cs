using San9AutoDomestic.Bridge.Protocol;

namespace San9AutoDomestic.Bridge.SelfTest
{
    internal sealed class SyntheticBridgeHost
    {
        private readonly InMemoryBridgeTransport transport;
        private readonly BridgeProtocolStateMachine stateMachine;
        private ProtocolFrame activeRequest;
        private RequestClaim activeClaim;

        internal SyntheticBridgeHost(
            InMemoryBridgeTransport fakeTransport,
            BridgeProtocolStateMachine protocolStateMachine)
        {
            transport = fakeTransport;
            stateMachine = protocolStateMachine;
        }

        internal bool IsExecutionAuthorized
        {
            get { return false; }
        }

        internal bool TryReceiveAndClaim(out string error)
        {
            error = null;
            byte[] requestBytes;
            if (!transport.TryReceive(out requestBytes))
            {
                error = "NO_IN_MEMORY_REQUEST";
                return false;
            }

            ProtocolFrame request;
            if (!ProtocolFrameCodec.TryDecode(requestBytes, out request, out error))
            {
                return false;
            }

            ProtocolRejection rejection;
            if (!stateMachine.TrySubmit(request, out rejection))
            {
                error = RejectionText(rejection);
                return false;
            }

            RequestClaim claim;
            if (!stateMachine.TryClaim(
                request.SessionNonce,
                request.RequestId,
                request.Sequence,
                out claim,
                out rejection))
            {
                error = RejectionText(rejection);
                return false;
            }

            string bindingError;
            if (!ProtocolFrameCodec.TryValidateClaimBinding(request, claim.ClaimFrame, out bindingError))
            {
                error = bindingError;
                return false;
            }

            if (!transport.TrySend(ProtocolFrameCodec.Encode(claim.ClaimFrame)))
            {
                ProtocolFrame ignoredResult;
                ProtocolRejection ignoredRejection;
                stateMachine.TryAbortSession(
                    request.SessionNonce,
                    FixedValue.Sha256(new byte[] { 0x41, 0x42, 0x4f, 0x52, 0x54 }),
                    out ignoredResult,
                    out ignoredRejection);
                error = "IN_MEMORY_CLAIM_SEND_FAILED_SESSION_ABORTED";
                return false;
            }

            activeRequest = request;
            activeClaim = claim;
            return true;
        }

        internal bool TryComplete(FixedValue resultPayloadDigest, out string error)
        {
            error = null;
            if (activeRequest == null || activeClaim == null)
            {
                error = "NO_SYNTHETIC_CLAIM";
                return false;
            }

            ProtocolFrame result;
            ProtocolRejection rejection;
            bool completed = stateMachine.TryComplete(
                activeClaim,
                resultPayloadDigest,
                out result,
                out rejection);
            if (!completed && result == null)
            {
                error = RejectionText(rejection);
                return false;
            }

            string bindingError;
            if (!ProtocolFrameCodec.TryValidateResultBinding(activeRequest, result, out bindingError))
            {
                error = bindingError;
                return false;
            }

            activeRequest = null;
            activeClaim = null;
            if (!transport.TrySend(ProtocolFrameCodec.Encode(result)))
            {
                error = "IN_MEMORY_RESULT_SEND_FAILED";
                return false;
            }

            if (!completed)
            {
                error = RejectionText(rejection);
                return false;
            }

            return true;
        }

        private static string RejectionText(ProtocolRejection rejection)
        {
            return rejection == null
                ? "NO_REJECTION_DETAIL"
                : rejection.Code + ":" + rejection.Message;
        }
    }
}
