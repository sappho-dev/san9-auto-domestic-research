using System;
using System.Collections.Generic;
using San9AutoDomestic.Bridge.Protocol;

namespace San9AutoDomestic.Bridge.SelfTest
{
    internal static class SyntheticBridgeProtocolTests
    {
        private const long BaseTicks = 639270000000000000L;
        private static readonly List<string> Failures = new List<string>();
        private static int passed;

        internal static int RunAll()
        {
            passed = 0;
            Failures.Clear();
            Run("fixed-width frame round trip", VerifyFixedWidthRoundTrip);
            Run("wire layout has no gaps or overlaps", VerifyWireLayoutCoverage);
            Run("checksum corruption rejected", VerifyChecksumCorruptionRejected);
            Run("schema size and reserved bytes rejected", VerifyLayoutGuards);
            Run("exact target identity digest is canonical", VerifyExactTargetDigest);
            Run("pending claimed completed lifecycle", VerifyHappyLifecycle);
            Run("synthetic host performs in-memory round trip", VerifySyntheticHostRoundTrip);
            Run("synthetic host delivers expiry rejection", VerifySyntheticHostExpiryRejection);
            Run("only one unresolved request", VerifyOnlyOneUnresolved);
            Run("duplicate request id rejected", VerifyDuplicateRejected);
            Run("replayed and skipped sequences rejected", VerifySequenceGuards);
            Run("expired future and overlong requests rejected", VerifyTimeGuards);
            Run("clock regression is fail-closed", VerifyClockRegressionRejected);
            Run("cross-session target and context mismatch rejected", VerifySessionContextGuards);
            Run("pending abort passes claimed then rejected", VerifyAbortFlow);
            Run("cross-session abort has no effect", VerifyCrossSessionAbortRejected);
            Run("foreign claim cannot finalize request", VerifyForeignClaimRejected);
            Run("duplicate claim and terminal are rejected", VerifyDuplicateTransitionsRejected);
            Run("explicit rejected result is request-bound", VerifyExplicitReject);
            Run("result cannot bind to another request", VerifyResultCrossBindingRejected);
            Run("completion after expiry is fail-closed", VerifyCompletionAfterExpiryRejected);
            Run("pending expiry becomes a bound rejection", VerifyPendingExpiryRejected);
            Run("wrong-width terminal digests fail closed", VerifyWrongWidthTerminalDigestsRejected);
            Run("in-memory duplex clones and bounds frames", VerifyInMemoryTransport);
            Run("zero binding values rejected", VerifyZeroBindingsRejected);

            Console.WriteLine(
                "Bridge protocol synthetic summary: {0} passed, {1} failed.",
                passed,
                Failures.Count);
            foreach (string failure in Failures)
            {
                Console.Error.WriteLine("FAIL: " + failure);
            }

            return Failures.Count == 0 ? 0 : 1;
        }

        private static void VerifyFixedWidthRoundTrip()
        {
            Fixture fixture = new Fixture();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            byte[] bytes = ProtocolFrameCodec.Encode(request);
            Assert(bytes.Length == ProtocolWireLayout.FrameSize, "The wire frame is not fixed width.");
            Assert(ReadUInt32(bytes, ProtocolWireLayout.MagicOffset) == ProtocolWireLayout.Magic,
                "The wire magic is wrong.");
            Assert(ReadUInt16(bytes, ProtocolWireLayout.SchemaMajorOffset) == ProtocolWireLayout.SchemaMajor,
                "The schema major version is missing.");
            Assert(ReadUInt16(bytes, ProtocolWireLayout.SchemaMinorOffset) == ProtocolWireLayout.SchemaMinor,
                "The schema minor version is missing.");
            Assert(ReadUInt32(bytes, ProtocolWireLayout.FrameSizeOffset) == ProtocolWireLayout.FrameSize,
                "The declared frame size is wrong.");
            Assert(ReadUInt32(bytes, ProtocolWireLayout.ChecksumOffset) != 0, "The checksum is absent.");

            ProtocolFrame decoded;
            string error;
            Assert(ProtocolFrameCodec.TryDecode(bytes, out decoded, out error), error);
            Assert(decoded.Kind == ProtocolFrameKind.Request, "Wrong decoded kind.");
            Assert(decoded.State == ProtocolRequestState.Pending, "Wrong decoded state.");
            Assert(decoded.Sequence == 1, "Wrong decoded sequence.");
            Assert(decoded.SessionNonce.Equals(fixture.SessionNonce), "Nonce did not round trip.");
            Assert(decoded.TargetIdentityDigest.Equals(fixture.TargetDigest), "Target digest did not round trip.");
            Assert(decoded.ContextTokenDigest.Equals(fixture.ContextDigest), "Context digest did not round trip.");
            Assert(!decoded.IsExecutionAuthorized, "A wire frame exposed execution authorization.");
        }

        private static void VerifyWireLayoutCoverage()
        {
            int[,] fields = new int[,]
            {
                { ProtocolWireLayout.MagicOffset, 4 },
                { ProtocolWireLayout.SchemaMajorOffset, 2 },
                { ProtocolWireLayout.SchemaMinorOffset, 2 },
                { ProtocolWireLayout.FrameSizeOffset, 4 },
                { ProtocolWireLayout.FrameKindOffset, 2 },
                { ProtocolWireLayout.StateOffset, 2 },
                { ProtocolWireLayout.ChecksumOffset, 4 },
                { ProtocolWireLayout.FlagsOffset, 4 },
                { ProtocolWireLayout.ReservedHeaderOffset, ProtocolWireLayout.ReservedHeaderSize },
                { ProtocolWireLayout.SessionNonceOffset, ProtocolWireLayout.NonceSize },
                { ProtocolWireLayout.SequenceOffset, 8 },
                { ProtocolWireLayout.RequestIdOffset, ProtocolWireLayout.RequestIdSize },
                { ProtocolWireLayout.TargetIdentityDigestOffset, ProtocolWireLayout.DigestSize },
                { ProtocolWireLayout.ContextTokenDigestOffset, ProtocolWireLayout.DigestSize },
                { ProtocolWireLayout.CreatedAtUtcTicksOffset, 8 },
                { ProtocolWireLayout.ExpiresAtUtcTicksOffset, 8 },
                { ProtocolWireLayout.OperationCodeOffset, 4 },
                { ProtocolWireLayout.ResultCodeOffset, 4 },
                { ProtocolWireLayout.RequestPayloadDigestOffset, ProtocolWireLayout.DigestSize },
                { ProtocolWireLayout.ResultPayloadDigestOffset, ProtocolWireLayout.DigestSize },
                { ProtocolWireLayout.RequestBindingDigestOffset, ProtocolWireLayout.DigestSize },
                { ProtocolWireLayout.ReservedTailOffset, ProtocolWireLayout.ReservedTailSize }
            };
            bool[] covered = new bool[ProtocolWireLayout.FrameSize];
            for (int field = 0; field < fields.GetLength(0); field++)
            {
                int offset = fields[field, 0];
                int length = fields[field, 1];
                Assert(offset >= 0 && length > 0 && offset + length <= covered.Length,
                    "A wire field exceeds the declared frame.");
                for (int index = offset; index < offset + length; index++)
                {
                    Assert(!covered[index], "Wire fields overlap at byte " + index + ".");
                    covered[index] = true;
                }
            }

            for (int index = 0; index < covered.Length; index++)
            {
                Assert(covered[index], "The wire layout has a gap at byte " + index + ".");
            }
        }

        private static void VerifyChecksumCorruptionRejected()
        {
            Fixture fixture = new Fixture();
            byte[] bytes = ProtocolFrameCodec.Encode(fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000));
            bytes[ProtocolWireLayout.RequestPayloadDigestOffset + 3] ^= 0x40;
            AssertDecodeFailure(bytes, "CHECKSUM_MISMATCH");
        }

        private static void VerifyLayoutGuards()
        {
            Fixture fixture = new Fixture();
            byte[] schema = ProtocolFrameCodec.Encode(fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000));
            schema[ProtocolWireLayout.SchemaMajorOffset]++;
            AssertDecodeFailure(schema, "SCHEMA_MISMATCH");

            byte[] size = ProtocolFrameCodec.Encode(fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000));
            size[ProtocolWireLayout.FrameSizeOffset]--;
            AssertDecodeFailure(size, "DECLARED_SIZE_MISMATCH");

            byte[] reserved = ProtocolFrameCodec.Encode(fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000));
            reserved[ProtocolWireLayout.ReservedTailOffset] = 1;
            RewriteChecksum(reserved);
            AssertDecodeFailure(reserved, "RESERVED_FIELD_NONZERO");
        }

        private static void VerifyExactTargetDigest()
        {
            byte[] executableDigest = Bytes(ProtocolWireLayout.DigestSize, 0x41);
            ExactTargetIdentity first = new ExactTargetIdentity(
                @"D:\三国志9\10101749\San9PK.exe",
                executableDigest,
                0x00400000,
                0x01759000,
                2636800,
                1,
                0,
                1,
                0);
            ExactTargetIdentity same = new ExactTargetIdentity(
                @"d:/三国志9/10101749/san9pk.EXE",
                executableDigest,
                0x00400000,
                0x01759000,
                2636800,
                1,
                0,
                1,
                0);
            ExactTargetIdentity changed = new ExactTargetIdentity(
                @"D:\三国志9\10101749\San9PK.exe",
                executableDigest,
                0x00400000,
                0x01759001,
                2636800,
                1,
                0,
                1,
                0);
            Assert(first.ComputeDigest().Equals(same.ComputeDigest()), "Canonical identity digest is unstable.");
            Assert(!first.ComputeDigest().Equals(changed.ComputeDigest()), "An exact identity field was not bound.");
            ExactTargetIdentity changedPath = new ExactTargetIdentity(
                @"D:\Other\San9PK.exe",
                executableDigest,
                0x00400000,
                0x01759000,
                2636800,
                1,
                0,
                1,
                0);
            Assert(!first.ComputeDigest().Equals(changedPath.ComputeDigest()),
                "The canonical executable path was not bound.");
        }

        private static void VerifyHappyLifecycle()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(request, out rejection), RejectionText(rejection));
            Assert(machine.ActiveState == ProtocolRequestState.Pending, "Submit did not enter Pending.");
            RequestClaim claim;
            Assert(machine.TryClaim(
                fixture.SessionNonce,
                request.RequestId,
                request.Sequence,
                out claim,
                out rejection), RejectionText(rejection));
            Assert(machine.ActiveState == ProtocolRequestState.Claimed, "Claim did not enter Claimed.");
            byte[] claimBytes = ProtocolFrameCodec.Encode(claim.ClaimFrame);
            ProtocolFrame decodedClaim;
            string bindingError;
            Assert(ProtocolFrameCodec.TryDecode(claimBytes, out decodedClaim, out bindingError), bindingError);
            Assert(ProtocolFrameCodec.TryValidateClaimBinding(request, decodedClaim, out bindingError), bindingError);
            ProtocolFrame result;
            Assert(machine.TryComplete(
                claim,
                Digest(0xa1),
                out result,
                out rejection), RejectionText(rejection));
            Assert(result.State == ProtocolRequestState.Completed, "Completion did not produce Completed.");
            Assert(ProtocolFrameCodec.TryValidateResultBinding(request, result, out bindingError), bindingError);
            byte[] resultBytes = ProtocolFrameCodec.Encode(result);
            ProtocolFrame decodedResult;
            Assert(ProtocolFrameCodec.TryDecode(resultBytes, out decodedResult, out bindingError), bindingError);
            Assert(ProtocolFrameCodec.TryValidateResultBinding(request, decodedResult, out bindingError), bindingError);
            Assert(!machine.HasUnresolvedRequest, "Terminal completion left a request unresolved.");
            Assert(machine.LastTerminalPassedThroughClaimed, "The terminal transition skipped Claimed.");
            Assert(!machine.IsExecutionAuthorized && !claim.IsExecutionAuthorized && !result.IsExecutionAuthorized,
                "The offline lifecycle exposed execution authorization.");
        }

        private static void VerifySyntheticHostRoundTrip()
        {
            Fixture fixture = new Fixture();
            InMemoryBridgeTransport clientTransport;
            InMemoryBridgeTransport hostTransport;
            InMemoryBridgeTransport.CreateDuplex(4, out clientTransport, out hostTransport);
            SyntheticBridgeHost host = new SyntheticBridgeHost(hostTransport, fixture.CreateMachine());
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            Assert(clientTransport.TrySend(ProtocolFrameCodec.Encode(request)),
                "The client could not enqueue its synthetic request.");
            string error;
            Assert(host.TryReceiveAndClaim(out error), error);

            byte[] claimBytes;
            Assert(clientTransport.TryReceive(out claimBytes), "The client did not receive Claim.");
            ProtocolFrame claim;
            Assert(ProtocolFrameCodec.TryDecode(claimBytes, out claim, out error), error);
            Assert(ProtocolFrameCodec.TryValidateClaimBinding(request, claim, out error), error);

            Assert(host.TryComplete(Digest(0xac), out error), error);
            byte[] resultBytes;
            Assert(clientTransport.TryReceive(out resultBytes), "The client did not receive Result.");
            ProtocolFrame result;
            Assert(ProtocolFrameCodec.TryDecode(resultBytes, out result, out error), error);
            Assert(result.State == ProtocolRequestState.Completed, "Synthetic host returned a non-completed result.");
            Assert(ProtocolFrameCodec.TryValidateResultBinding(request, result, out error), error);
            Assert(!host.IsExecutionAuthorized && !clientTransport.IsGameConnected && !hostTransport.IsGameConnected,
                "The synthetic host or fake transport claims a game capability.");
        }

        private static void VerifySyntheticHostExpiryRejection()
        {
            Fixture fixture = new Fixture();
            InMemoryBridgeTransport clientTransport;
            InMemoryBridgeTransport hostTransport;
            InMemoryBridgeTransport.CreateDuplex(4, out clientTransport, out hostTransport);
            SyntheticBridgeHost host = new SyntheticBridgeHost(hostTransport, fixture.CreateMachine());
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 10);
            Assert(clientTransport.TrySend(ProtocolFrameCodec.Encode(request)),
                "The client could not enqueue the expiring request.");
            string error;
            Assert(host.TryReceiveAndClaim(out error), error);
            byte[] ignoredClaim;
            Assert(clientTransport.TryReceive(out ignoredClaim), "The client did not receive Claim.");
            fixture.Clock.UtcNowTicks = BaseTicks + 10;
            Assert(!host.TryComplete(Digest(0xad), out error),
                "The synthetic host completed an expired request.");
            Assert(error != null && error.StartsWith("RequestExpired", StringComparison.Ordinal),
                "The synthetic host reported the wrong expiry error: " + error);
            byte[] resultBytes;
            Assert(clientTransport.TryReceive(out resultBytes),
                "The synthetic host did not deliver the expiry rejection.");
            ProtocolFrame result;
            Assert(ProtocolFrameCodec.TryDecode(resultBytes, out result, out error), error);
            Assert(result.State == ProtocolRequestState.Rejected
                && result.ResultCode == (uint)ProtocolRejectionCode.RequestExpired,
                "The delivered expiry terminal is not Rejected/RequestExpired.");
            Assert(ProtocolFrameCodec.TryValidateResultBinding(request, result, out error), error);
        }

        private static void VerifyOnlyOneUnresolved()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000), out rejection),
                RejectionText(rejection));
            Assert(!machine.TrySubmit(fixture.CreateRequest(2, 2, BaseTicks, BaseTicks + 1000), out rejection),
                "A second unresolved request was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.OutstandingRequestExists);
        }

        private static void VerifyDuplicateRejected()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame first = fixture.CreateRequest(7, 1, BaseTicks, BaseTicks + 1000);
            Complete(machine, fixture, first);
            ProtocolRejection rejection;
            ProtocolFrame duplicate = fixture.CreateRequest(7, 2, BaseTicks, BaseTicks + 1000);
            Assert(!machine.TrySubmit(duplicate, out rejection), "A duplicate request id was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.DuplicateRequest);
        }

        private static void VerifySequenceGuards()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame first = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            Complete(machine, fixture, first);
            ProtocolRejection rejection;
            Assert(!machine.TrySubmit(fixture.CreateRequest(2, 1, BaseTicks, BaseTicks + 1000), out rejection),
                "A replayed sequence was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.SequenceReplay);
            Assert(!machine.TrySubmit(fixture.CreateRequest(3, 3, BaseTicks, BaseTicks + 1000), out rejection),
                "A sequence gap was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.SequenceGap);
        }

        private static void VerifyTimeGuards()
        {
            Fixture fixture = new Fixture();
            ProtocolRejection rejection;
            BridgeProtocolStateMachine expiredMachine = fixture.CreateMachine();
            Assert(!expiredMachine.TrySubmit(
                fixture.CreateRequest(1, 1, BaseTicks - 1000, BaseTicks),
                out rejection), "An expired request was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.RequestExpired);

            BridgeProtocolStateMachine futureMachine = fixture.CreateMachine();
            Assert(!futureMachine.TrySubmit(
                fixture.CreateRequest(2, 1, BaseTicks + 1, BaseTicks + 1000),
                out rejection), "A future request was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.RequestFromFuture);

            BridgeProtocolStateMachine longMachine = fixture.CreateMachine();
            Assert(!longMachine.TrySubmit(
                fixture.CreateRequest(
                    3,
                    1,
                    BaseTicks,
                    BaseTicks + ProtocolWireLayout.MaximumRequestLifetimeTicks + 1),
                out rejection), "An overlong request was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.LifetimeInvalid);
        }

        private static void VerifyClockRegressionRejected()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(request, out rejection), RejectionText(rejection));
            fixture.Clock.UtcNowTicks = BaseTicks - 1;
            RequestClaim claim;
            Assert(!machine.TryClaim(
                fixture.SessionNonce,
                request.RequestId,
                request.Sequence,
                out claim,
                out rejection), "A request was claimed after the clock moved backwards.");
            AssertCode(rejection, ProtocolRejectionCode.ClockRegressed);
            Assert(machine.ActiveState == ProtocolRequestState.Pending,
                "Clock regression mutated the pending request.");
        }

        private static void VerifySessionContextGuards()
        {
            Fixture fixture = new Fixture();
            ProtocolRejection rejection;
            BridgeProtocolStateMachine crossSession = fixture.CreateMachine();
            ProtocolFrame wrongSession = ProtocolFrame.CreateRequest(
                Nonce(0xe1),
                1,
                RequestId(1),
                fixture.TargetDigest,
                fixture.ContextDigest,
                BaseTicks,
                BaseTicks + 1000,
                1,
                Digest(0x91));
            Assert(!crossSession.TrySubmit(wrongSession, out rejection), "A cross-session request was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.CrossSession);

            BridgeProtocolStateMachine wrongTargetMachine = fixture.CreateMachine();
            ProtocolFrame wrongTarget = ProtocolFrame.CreateRequest(
                fixture.SessionNonce,
                1,
                RequestId(2),
                Digest(0xe2),
                fixture.ContextDigest,
                BaseTicks,
                BaseTicks + 1000,
                1,
                Digest(0x92));
            Assert(!wrongTargetMachine.TrySubmit(wrongTarget, out rejection), "A wrong-target request was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.TargetIdentityMismatch);

            BridgeProtocolStateMachine wrongContextMachine = fixture.CreateMachine();
            ProtocolFrame wrongContext = ProtocolFrame.CreateRequest(
                fixture.SessionNonce,
                1,
                RequestId(3),
                fixture.TargetDigest,
                Digest(0xe3),
                BaseTicks,
                BaseTicks + 1000,
                1,
                Digest(0x93));
            Assert(!wrongContextMachine.TrySubmit(wrongContext, out rejection), "A stale-context request was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.ContextTokenMismatch);
        }

        private static void VerifyAbortFlow()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(request, out rejection), RejectionText(rejection));
            ProtocolFrame result;
            Assert(machine.TryAbortSession(fixture.SessionNonce, Digest(0xa2), out result, out rejection),
                RejectionText(rejection));
            Assert(machine.IsAborted, "Abort did not close the session.");
            Assert(!machine.HasUnresolvedRequest, "Abort left the request unresolved.");
            Assert(machine.LastTerminalPassedThroughClaimed, "Abort bypassed Pending -> Claimed -> Rejected.");
            Assert(result != null && result.State == ProtocolRequestState.Rejected,
                "Abort did not produce a rejected result.");
            Assert(result.ResultCode == (uint)ProtocolRejectionCode.SessionAborted,
                "Abort result has the wrong code.");
            string bindingError;
            Assert(ProtocolFrameCodec.TryValidateResultBinding(request, result, out bindingError), bindingError);
            Assert(!machine.TrySubmit(fixture.CreateRequest(2, 2, BaseTicks, BaseTicks + 1000), out rejection),
                "An aborted session accepted a request.");
            AssertCode(rejection, ProtocolRejectionCode.SessionAborted);
            Assert(!machine.TryAbortSession(fixture.SessionNonce, Digest(0xa3), out result, out rejection),
                "A duplicate abort was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.SessionAborted);
        }

        private static void VerifyCrossSessionAbortRejected()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame result;
            ProtocolRejection rejection;
            Assert(!machine.TryAbortSession(Nonce(0xe4), Digest(0xa4), out result, out rejection),
                "A cross-session abort was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.CrossSession);
            Assert(!machine.IsAborted, "A cross-session abort changed the session.");
        }

        private static void VerifyForeignClaimRejected()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine firstMachine = fixture.CreateMachine();
            BridgeProtocolStateMachine secondMachine = fixture.CreateMachine();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            ProtocolRejection rejection;
            Assert(firstMachine.TrySubmit(request, out rejection), RejectionText(rejection));
            Assert(secondMachine.TrySubmit(request, out rejection), RejectionText(rejection));
            RequestClaim firstClaim;
            RequestClaim foreignClaim;
            Assert(firstMachine.TryClaim(fixture.SessionNonce, request.RequestId, 1, out firstClaim, out rejection),
                RejectionText(rejection));
            Assert(secondMachine.TryClaim(fixture.SessionNonce, request.RequestId, 1, out foreignClaim, out rejection),
                RejectionText(rejection));
            ProtocolFrame result;
            Assert(!firstMachine.TryComplete(foreignClaim, Digest(0xa5), out result, out rejection),
                "A claim from another state machine finalized the request.");
            AssertCode(rejection, ProtocolRejectionCode.RequestBindingMismatch);
            Assert(firstMachine.HasUnresolvedRequest, "A failed foreign claim cleared the valid request.");
        }

        private static void VerifyDuplicateTransitionsRejected()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(request, out rejection), RejectionText(rejection));
            RequestClaim claim;
            Assert(machine.TryClaim(fixture.SessionNonce, request.RequestId, 1, out claim, out rejection),
                RejectionText(rejection));
            RequestClaim duplicateClaim;
            Assert(!machine.TryClaim(
                fixture.SessionNonce,
                request.RequestId,
                1,
                out duplicateClaim,
                out rejection), "A duplicate claim was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.InvalidStateTransition);
            ProtocolFrame result;
            Assert(machine.TryComplete(claim, Digest(0xa9), out result, out rejection),
                RejectionText(rejection));
            Assert(!machine.TryComplete(claim, Digest(0xaa), out result, out rejection),
                "A duplicate terminal transition was accepted.");
            AssertCode(rejection, ProtocolRejectionCode.InvalidStateTransition);
        }

        private static void VerifyExplicitReject()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(request, out rejection), RejectionText(rejection));
            RequestClaim claim;
            Assert(machine.TryClaim(fixture.SessionNonce, request.RequestId, 1, out claim, out rejection),
                RejectionText(rejection));
            ProtocolFrame result;
            Assert(machine.TryReject(
                claim,
                ProtocolRejectionCode.InvalidFrame,
                Digest(0xa6),
                out result,
                out rejection), RejectionText(rejection));
            Assert(result.State == ProtocolRequestState.Rejected, "Explicit rejection state is wrong.");
            string bindingError;
            Assert(ProtocolFrameCodec.TryValidateResultBinding(request, result, out bindingError), bindingError);
        }

        private static void VerifyResultCrossBindingRejected()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine firstMachine = fixture.CreateMachine();
            ProtocolFrame first = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            ProtocolFrame firstResult = Complete(firstMachine, fixture, first);

            BridgeProtocolStateMachine secondMachine = fixture.CreateMachine();
            ProtocolFrame second = fixture.CreateRequest(2, 1, BaseTicks, BaseTicks + 1000);
            ProtocolFrame secondResult = Complete(secondMachine, fixture, second);
            string error;
            Assert(!ProtocolFrameCodec.TryValidateResultBinding(first, secondResult, out error),
                "A result bound to another request was accepted.");
            Assert(string.Equals(error, "RESULT_REQUEST_BINDING_MISMATCH", StringComparison.Ordinal),
                "Wrong cross-binding error: " + error);
            Assert(ProtocolFrameCodec.TryValidateResultBinding(first, firstResult, out error), error);
        }

        private static void VerifyCompletionAfterExpiryRejected()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 10);
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(request, out rejection), RejectionText(rejection));
            RequestClaim claim;
            Assert(machine.TryClaim(fixture.SessionNonce, request.RequestId, 1, out claim, out rejection),
                RejectionText(rejection));
            fixture.Clock.UtcNowTicks = BaseTicks + 10;
            ProtocolFrame result;
            Assert(!machine.TryComplete(claim, Digest(0xa7), out result, out rejection),
                "An expired claimed request completed.");
            AssertCode(rejection, ProtocolRejectionCode.RequestExpired);
            Assert(result != null && result.State == ProtocolRequestState.Rejected,
                "An expired completion did not emit a rejection result.");
            string bindingError;
            Assert(ProtocolFrameCodec.TryValidateResultBinding(request, result, out bindingError), bindingError);
            Assert(!machine.HasUnresolvedRequest, "The expired request remained unresolved.");
        }

        private static void VerifyPendingExpiryRejected()
        {
            Fixture fixture = new Fixture();
            BridgeProtocolStateMachine machine = fixture.CreateMachine();
            ProtocolFrame request = fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 10);
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(request, out rejection), RejectionText(rejection));
            fixture.Clock.UtcNowTicks = BaseTicks + 10;
            ProtocolFrame result;
            Assert(machine.TryRejectExpired(Digest(0xa8), out result, out rejection), RejectionText(rejection));
            Assert(result != null && result.State == ProtocolRequestState.Rejected,
                "Pending expiry did not produce Rejected.");
            Assert(machine.LastTerminalPassedThroughClaimed,
                "Pending expiry bypassed Pending -> Claimed -> Rejected.");
            string bindingError;
            Assert(ProtocolFrameCodec.TryValidateResultBinding(request, result, out bindingError), bindingError);
            Assert(!machine.HasUnresolvedRequest, "Pending expiry left an unresolved request.");
        }

        private static void VerifyWrongWidthTerminalDigestsRejected()
        {
            FixedValue wrongWidth = Nonce(0xb1);

            Fixture completionFixture = new Fixture();
            BridgeProtocolStateMachine completionMachine = completionFixture.CreateMachine();
            ProtocolFrame completionRequest = completionFixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            ProtocolRejection rejection;
            Assert(completionMachine.TrySubmit(completionRequest, out rejection), RejectionText(rejection));
            RequestClaim completionClaim;
            Assert(completionMachine.TryClaim(
                completionFixture.SessionNonce,
                completionRequest.RequestId,
                1,
                out completionClaim,
                out rejection), RejectionText(rejection));
            ProtocolFrame result;
            Assert(!completionMachine.TryComplete(completionClaim, wrongWidth, out result, out rejection),
                "TryComplete accepted a 16-byte result digest.");
            AssertCode(rejection, ProtocolRejectionCode.InvalidFrame);
            Assert(result == null && completionMachine.HasUnresolvedRequest,
                "Invalid completion digest mutated the request.");

            Fixture rejectFixture = new Fixture();
            BridgeProtocolStateMachine rejectMachine = rejectFixture.CreateMachine();
            ProtocolFrame rejectRequest = rejectFixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            Assert(rejectMachine.TrySubmit(rejectRequest, out rejection), RejectionText(rejection));
            RequestClaim rejectClaim;
            Assert(rejectMachine.TryClaim(
                rejectFixture.SessionNonce,
                rejectRequest.RequestId,
                1,
                out rejectClaim,
                out rejection), RejectionText(rejection));
            Assert(!rejectMachine.TryReject(
                rejectClaim,
                ProtocolRejectionCode.InvalidFrame,
                wrongWidth,
                out result,
                out rejection), "TryReject accepted a 16-byte result digest.");
            AssertCode(rejection, ProtocolRejectionCode.InvalidFrame);
            Assert(result == null && rejectMachine.HasUnresolvedRequest,
                "Invalid rejection digest mutated the request.");

            Fixture abortFixture = new Fixture();
            BridgeProtocolStateMachine abortMachine = abortFixture.CreateMachine();
            ProtocolFrame abortRequest = abortFixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000);
            Assert(abortMachine.TrySubmit(abortRequest, out rejection), RejectionText(rejection));
            Assert(!abortMachine.TryAbortSession(
                abortFixture.SessionNonce,
                wrongWidth,
                out result,
                out rejection), "TryAbortSession accepted a 16-byte result digest.");
            AssertCode(rejection, ProtocolRejectionCode.InvalidFrame);
            Assert(result == null && !abortMachine.IsAborted && abortMachine.HasUnresolvedRequest,
                "Invalid abort digest mutated the session.");

            Fixture expiryFixture = new Fixture();
            BridgeProtocolStateMachine expiryMachine = expiryFixture.CreateMachine();
            ProtocolFrame expiryRequest = expiryFixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 10);
            Assert(expiryMachine.TrySubmit(expiryRequest, out rejection), RejectionText(rejection));
            expiryFixture.Clock.UtcNowTicks = BaseTicks + 10;
            Assert(!expiryMachine.TryRejectExpired(wrongWidth, out result, out rejection),
                "TryRejectExpired accepted a 16-byte result digest.");
            AssertCode(rejection, ProtocolRejectionCode.InvalidFrame);
            Assert(result == null && expiryMachine.HasUnresolvedRequest,
                "Invalid expiry digest mutated the request.");
        }

        private static void VerifyInMemoryTransport()
        {
            InMemoryBridgeTransport first;
            InMemoryBridgeTransport second;
            InMemoryBridgeTransport.CreateDuplex(1, out first, out second);
            Fixture fixture = new Fixture();
            byte[] original = ProtocolFrameCodec.Encode(fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000));
            byte expectedFirstByte = original[0];
            Assert(first.TrySend(original), "The first in-memory send failed.");
            Assert(!first.TrySend(original), "The bounded queue accepted an overflow frame.");
            Assert(!first.TrySend(new byte[3]), "A non-frame payload was accepted.");
            original[0] ^= 0xff;
            byte[] received;
            Assert(second.TryReceive(out received), "The peer did not receive the frame.");
            Assert(received[0] == expectedFirstByte, "The transport did not clone on send.");
            received[1] ^= 0xff;
            Assert(second.PendingReceiveCount == 0, "Receive did not dequeue exactly one frame.");
            Assert(!first.IsGameConnected && !second.IsGameConnected,
                "The fake transport claims a game connection.");
        }

        private static void VerifyZeroBindingsRejected()
        {
            bool threw = false;
            try
            {
                FixedValue.Nonce(new byte[ProtocolWireLayout.NonceSize]);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Assert(threw, "An all-zero session nonce was accepted.");

            Fixture fixture = new Fixture();
            byte[] wire = ProtocolFrameCodec.Encode(
                fixture.CreateRequest(1, 1, BaseTicks, BaseTicks + 1000));
            Array.Clear(wire, ProtocolWireLayout.SessionNonceOffset, ProtocolWireLayout.NonceSize);
            RewriteChecksum(wire);
            ProtocolFrame ignored;
            string error;
            Assert(!ProtocolFrameCodec.TryDecode(wire, out ignored, out error),
                "An all-zero wire nonce was decoded.");
            Assert(error != null && error.StartsWith("FIXED_VALUE_INVALID", StringComparison.Ordinal),
                "Wrong zero-wire binding error: " + error);
        }

        private static ProtocolFrame Complete(
            BridgeProtocolStateMachine machine,
            Fixture fixture,
            ProtocolFrame request)
        {
            ProtocolRejection rejection;
            Assert(machine.TrySubmit(request, out rejection), RejectionText(rejection));
            RequestClaim claim;
            Assert(machine.TryClaim(
                fixture.SessionNonce,
                request.RequestId,
                request.Sequence,
                out claim,
                out rejection), RejectionText(rejection));
            ProtocolFrame result;
            Assert(machine.TryComplete(claim, Digest(0xaf), out result, out rejection), RejectionText(rejection));
            return result;
        }

        private static void AssertDecodeFailure(byte[] bytes, string expectedError)
        {
            ProtocolFrame frame;
            string error;
            Assert(!ProtocolFrameCodec.TryDecode(bytes, out frame, out error),
                "A malformed frame was decoded.");
            Assert(string.Equals(error, expectedError, StringComparison.Ordinal),
                "Expected " + expectedError + ", got " + error + ".");
        }

        private static void AssertCode(ProtocolRejection rejection, ProtocolRejectionCode expected)
        {
            Assert(rejection != null && rejection.Code == expected,
                "Expected " + expected + ", got " + RejectionText(rejection) + ".");
        }

        private static string RejectionText(ProtocolRejection rejection)
        {
            return rejection == null ? "No rejection detail." : rejection.Code + ":" + rejection.Message;
        }

        private static void Run(string name, Action action)
        {
            try
            {
                action();
                passed++;
                Console.WriteLine("PASS: " + name);
            }
            catch (Exception exception)
            {
                Failures.Add(name + " - " + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static FixedValue Nonce(byte marker)
        {
            return FixedValue.Nonce(Bytes(ProtocolWireLayout.NonceSize, marker));
        }

        private static FixedValue RequestId(byte marker)
        {
            return FixedValue.RequestId(Bytes(ProtocolWireLayout.RequestIdSize, marker));
        }

        private static FixedValue Digest(byte marker)
        {
            return FixedValue.Digest(Bytes(ProtocolWireLayout.DigestSize, marker));
        }

        private static byte[] Bytes(int size, byte marker)
        {
            byte[] bytes = new byte[size];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = unchecked((byte)(marker + index));
            }

            return bytes;
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void RewriteChecksum(byte[] bytes)
        {
            WriteUInt32(bytes, ProtocolWireLayout.ChecksumOffset, 0);
            uint crc = 0xffffffffU;
            for (int index = 0; index < bytes.Length; index++)
            {
                crc ^= bytes[index];
                for (int bit = 0; bit < 8; bit++)
                {
                    uint mask = unchecked((uint)-(int)(crc & 1U));
                    crc = (crc >> 1) ^ (0xedb88320U & mask);
                }
            }

            WriteUInt32(bytes, ProtocolWireLayout.ChecksumOffset, ~crc);
        }

        private sealed class Fixture
        {
            internal Fixture()
            {
                SessionNonce = Nonce(0x11);
                TargetDigest = Digest(0x31);
                ContextDigest = Digest(0x51);
                Clock = new ManualClock { UtcNowTicks = BaseTicks };
            }

            internal FixedValue SessionNonce { get; private set; }

            internal FixedValue TargetDigest { get; private set; }

            internal FixedValue ContextDigest { get; private set; }

            internal ManualClock Clock { get; private set; }

            internal BridgeProtocolStateMachine CreateMachine()
            {
                return new BridgeProtocolStateMachine(SessionNonce, TargetDigest, ContextDigest, Clock);
            }

            internal ProtocolFrame CreateRequest(
                byte requestMarker,
                ulong sequence,
                long createdAtUtcTicks,
                long expiresAtUtcTicks)
            {
                return ProtocolFrame.CreateRequest(
                    SessionNonce,
                    sequence,
                    RequestId(requestMarker),
                    TargetDigest,
                    ContextDigest,
                    createdAtUtcTicks,
                    expiresAtUtcTicks,
                    1,
                    Digest(unchecked((byte)(0x70 + requestMarker))));
            }
        }

        private sealed class ManualClock : IProtocolClock
        {
            public long UtcNowTicks { get; set; }
        }
    }
}
