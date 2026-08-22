using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace San9AutoDomestic.P1Wire.SelfTest
{
    internal static class Program
    {
        private static int failures;
        private static int checks;

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || (args.Length == 1 && args[0] == "--self-test"))
                {
                    RunAll();
                    Console.WriteLine("P1WIRE_CSHARP_SELFTEST PASS checks={0}", checks);
                    return 0;
                }

                if (args.Length == 2 && args[0] == "--write-golden")
                {
                    RunAll();
                    byte[] golden = P1WireCodec.Encode(GoldenFixture.CreateRequest(), GoldenFixture.CreateKey());
                    File.WriteAllBytes(args[1], golden);
                    Console.WriteLine("P1WIRE_CSHARP_GOLDEN_WRITTEN {0} sha256={1}", args[1], Sha256Hex(golden));
                    return 0;
                }

                if (args.Length == 2 && args[0] == "--write-response-golden")
                {
                    RunAll();
                    P1WireFrame request = GoldenFixture.CreateRequest();
                    byte[] golden = P1WireCodec.Encode(
                        GoldenFixture.CreateResponse(request), GoldenFixture.CreateKey());
                    File.WriteAllBytes(args[1], golden);
                    Console.WriteLine(
                        "P1WIRE_CSHARP_RESPONSE_GOLDEN_WRITTEN {0} sha256={1}",
                        args[1], Sha256Hex(golden));
                    return 0;
                }

                if (args.Length == 2 && args[0] == "--verify-golden")
                {
                    byte[] candidate = File.ReadAllBytes(args[1]);
                    VerifyExternalGolden(candidate);
                    Console.WriteLine("P1WIRE_CSHARP_GOLDEN_VERIFIED {0} sha256={1}", args[1], Sha256Hex(candidate));
                    return 0;
                }

                if (args.Length == 2 && args[0] == "--verify-response-golden")
                {
                    byte[] candidate = File.ReadAllBytes(args[1]);
                    VerifyExternalResponseGolden(candidate);
                    Console.WriteLine(
                        "P1WIRE_CSHARP_RESPONSE_GOLDEN_VERIFIED {0} sha256={1}",
                        args[1], Sha256Hex(candidate));
                    return 0;
                }

                Console.Error.WriteLine(
                    "usage: selftest [--self-test | --write-golden PATH | --verify-golden PATH | " +
                    "--write-response-golden PATH | --verify-response-golden PATH]");
                return 2;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("P1WIRE_CSHARP_SELFTEST FAIL {0}", exception);
                return 1;
            }
        }

        private static void RunAll()
        {
            failures = 0;
            checks = 0;
            TestContractAndLayout();
            TestManagedAssemblyBoundary();
            TestGoldenRoundTrip();
            TestRawSingleByteMutations();
            TestAuthenticatedSingleByteMutations();
            TestAuthenticatedStructuralRejections();
            TestLengthsAndKeys();
            TestStrictShapes();
            TestRequestGate();
            TestResponseVerifier();
            TestAuthenticatedEntryPoints();
            if (failures != 0)
            {
                throw new InvalidOperationException(string.Format("{0} of {1} checks failed", failures, checks));
            }
        }

        private static void TestContractAndLayout()
        {
            Check(P1WireLayout.FrameSize == 512, "frame-size");
            Check(P1WireLayout.HmacOffset + P1WireLayout.DigestSize == P1WireLayout.ReservedTailOffset, "hmac-boundary");
            Check(P1WireLayout.ReservedTailOffset + P1WireLayout.ReservedTailSize == P1WireLayout.FrameSize, "tail-boundary");
            Check(!P1WireContract.LiveAuthorization, "live-authorization-false");
            Check(!P1WireContract.ContainsBusinessFields, "no-business-fields");
            Check(!P1WireContract.ContainsNativeAddresses, "no-native-addresses");
        }

        private static void TestGoldenRoundTrip()
        {
            byte[] key = GoldenFixture.CreateKey();
            byte[] golden = P1WireCodec.Encode(GoldenFixture.CreateRequest(), key);
            P1WireFrame decoded;
            Check(golden.Length == P1WireLayout.FrameSize, "golden-size");
            Check(P1WireCodec.TryDecode(golden, key, out decoded) == P1WireDecodeStatus.Accepted, "golden-decode");
            Check(decoded != null && decoded.Sequence == 1UL, "golden-sequence");
            Check(Equal(golden, P1WireCodec.Encode(decoded, key)), "golden-canonical-roundtrip");
            if (GoldenFixture.ExpectedGoldenSha256 != "PENDING")
            {
                Check(Sha256Hex(golden) == GoldenFixture.ExpectedGoldenSha256, "golden-sha256");
            }
            P1WireFrame request = GoldenFixture.CreateRequest();
            byte[] response = P1WireCodec.Encode(
                GoldenFixture.CreateResponse(request), key);
            Check(Sha256Hex(response) == GoldenFixture.ExpectedResponseGoldenSha256,
                "response-golden-sha256");
        }

        private static void TestManagedAssemblyBoundary()
        {
            Assembly contract = typeof(P1WireLayout).Assembly;
            Type[] types = contract.GetTypes();
            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                MethodInfo[] methods = types[typeIndex].GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
                {
                    Check((methods[methodIndex].Attributes & MethodAttributes.PinvokeImpl) == 0,
                        "managed-no-pinvoke-" + types[typeIndex].FullName + "." + methods[methodIndex].Name);
                }

                FieldInfo[] fields = types[typeIndex].GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    Type fieldType = fields[fieldIndex].FieldType;
                    Check(fieldType != typeof(IntPtr) && fieldType != typeof(UIntPtr) && !fieldType.IsPointer,
                        "managed-no-address-field-" + types[typeIndex].FullName + "." + fields[fieldIndex].Name);
                }
            }
            Check(typeof(P1WireRequestGate).GetMethod(
                    "TryAccept", BindingFlags.Public | BindingFlags.Instance) == null,
                "managed-raw-gate-is-not-public");
            Check(typeof(P1WireResponseVerifier).GetMethod(
                    "Verify", BindingFlags.Public | BindingFlags.Static) == null,
                "managed-raw-response-verifier-is-not-public");
        }

        private static void TestRawSingleByteMutations()
        {
            byte[] key = GoldenFixture.CreateKey();
            byte[] golden = P1WireCodec.Encode(GoldenFixture.CreateRequest(), key);
            for (int offset = 0; offset < golden.Length; offset++)
            {
                byte[] mutated = (byte[])golden.Clone();
                mutated[offset] ^= 0x01;
                P1WireFrame decoded;
                Check(P1WireCodec.TryDecode(mutated, key, out decoded) != P1WireDecodeStatus.Accepted,
                    "raw-mutation-" + offset);
            }
        }

        private static void TestAuthenticatedSingleByteMutations()
        {
            byte[] key = GoldenFixture.CreateKey();
            byte[] golden = P1WireCodec.Encode(GoldenFixture.CreateRequest(), key);
            for (int offset = 0; offset < golden.Length; offset++)
            {
                if (offset >= P1WireLayout.Crc32Offset && offset < P1WireLayout.Crc32Offset + sizeof(uint))
                {
                    continue;
                }

                byte[] mutated = (byte[])golden.Clone();
                mutated[offset] ^= 0x01;
                WriteUInt32(mutated, P1WireLayout.Crc32Offset, ComputeCrc(mutated));
                P1WireFrame decoded;
                P1WireDecodeStatus status = P1WireCodec.TryDecode(mutated, key, out decoded);
                Check(status != P1WireDecodeStatus.Accepted, "crc-repaired-mutation-" + offset);
            }
        }

        private static void TestLengthsAndKeys()
        {
            byte[] key = GoldenFixture.CreateKey();
            byte[] golden = P1WireCodec.Encode(GoldenFixture.CreateRequest(), key);
            for (int length = 0; length < P1WireLayout.FrameSize; length++)
            {
                byte[] shortFrame = new byte[length];
                Buffer.BlockCopy(golden, 0, shortFrame, 0, length);
                P1WireFrame decoded;
                Check(P1WireCodec.TryDecode(shortFrame, key, out decoded) == P1WireDecodeStatus.SizeMismatch,
                    "truncation-" + length);
            }

            byte[] overlong = new byte[P1WireLayout.FrameSize + 1];
            Buffer.BlockCopy(golden, 0, overlong, 0, golden.Length);
            P1WireFrame ignored;
            Check(P1WireCodec.TryDecode(overlong, key, out ignored) == P1WireDecodeStatus.SizeMismatch, "overlong-513");
            Check(P1WireCodec.TryDecode(new byte[P1WireLayout.FrameSize * 2], key, out ignored) == P1WireDecodeStatus.SizeMismatch, "overlong-1024");
            Check(P1WireCodec.TryDecode(null, key, out ignored) == P1WireDecodeStatus.NullFrame, "null-frame");
            Check(P1WireCodec.TryDecode(golden, new byte[P1WireLayout.HmacKeySize], out ignored) == P1WireDecodeStatus.KeyInvalid, "zero-key");
            Check(P1WireCodec.TryDecode(golden, new byte[31], out ignored) == P1WireDecodeStatus.KeyInvalid, "short-key");
            byte[] wrong = GoldenFixture.CreateKey();
            wrong[0] ^= 0x80;
            Check(P1WireCodec.TryDecode(golden, wrong, out ignored) == P1WireDecodeStatus.HmacMismatch, "wrong-key");
            Check(ignored == null, "decode-failure-clears-output");
        }

        private static void TestAuthenticatedStructuralRejections()
        {
            byte[] key = GoldenFixture.CreateKey();
            byte[] request = P1WireCodec.Encode(GoldenFixture.CreateRequest(), key);
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { frame[P1WireLayout.ReservedHeaderOffset] = 1; },
                P1WireDecodeStatus.ReservedOrFlagsInvalid, "auth-reserved-header");
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { frame[P1WireLayout.ReservedBindingOffset] = 1; },
                P1WireDecodeStatus.ReservedOrFlagsInvalid, "auth-reserved-binding");
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { frame[P1WireLayout.ReservedTailOffset] = 1; },
                P1WireDecodeStatus.ReservedOrFlagsInvalid, "auth-reserved-tail");
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { WriteUInt32(frame, P1WireLayout.FlagsOffset, 1U); },
                P1WireDecodeStatus.ReservedOrFlagsInvalid, "auth-flags");
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { WriteUInt16(frame, P1WireLayout.KindOffset, 3); },
                P1WireDecodeStatus.KindStateInvalid, "auth-kind");
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { WriteUInt64(frame, P1WireLayout.SequenceOffset, 0UL); },
                P1WireDecodeStatus.RequiredFieldInvalid, "auth-zero-sequence");
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { Array.Clear(frame, P1WireLayout.ManifestDigestOffset, P1WireLayout.DigestSize); },
                P1WireDecodeStatus.RequiredFieldInvalid, "auth-zero-manifest");
            CheckAuthenticatedStatus(request, key,
                delegate(byte[] frame) { WriteUInt64(frame, P1WireLayout.ExpiresAtMillisecondsOffset, 1005001UL); },
                P1WireDecodeStatus.LifetimeInvalid, "auth-lifetime");
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { WriteUInt32(frame, P1WireLayout.ResultCodeOffset, 1U); },
                P1WireDecodeStatus.RequestShapeInvalid, "auth-request-result-code");
            CheckAuthenticatedStatus(request, key, delegate(byte[] frame) { frame[P1WireLayout.ResultDigestOffset] = 1; },
                P1WireDecodeStatus.RequestShapeInvalid, "auth-request-result-digest");

            byte[] response = P1WireCodec.Encode(
                GoldenFixture.CreateResponse(GoldenFixture.CreateRequest()),
                key);
            CheckAuthenticatedStatus(response, key, delegate(byte[] frame) { WriteUInt32(frame, P1WireLayout.ResultCodeOffset, 1U); },
                P1WireDecodeStatus.ResponseShapeInvalid, "auth-completed-result-code");
            CheckAuthenticatedStatus(response, key,
                delegate(byte[] frame) { Array.Clear(frame, P1WireLayout.ResultDigestOffset, P1WireLayout.DigestSize); },
                P1WireDecodeStatus.ResponseShapeInvalid, "auth-zero-response-result-digest");
        }

        private static void TestStrictShapes()
        {
            byte[] key = GoldenFixture.CreateKey();
            ExpectEncodeRejected(Mutate(delegate(P1WireFrame frame) { frame.Kind = (P1WireKind)3; }), key, "unknown-kind");
            ExpectEncodeRejected(Mutate(delegate(P1WireFrame frame) { frame.State = P1WireState.Completed; }), key, "request-state");
            ExpectEncodeRejected(Mutate(delegate(P1WireFrame frame) { frame.Flags = 1U; }), key, "flags");
            ExpectEncodeRejected(Mutate(delegate(P1WireFrame frame) { frame.Sequence = 0UL; }), key, "zero-sequence");
            ExpectEncodeRejected(Mutate(delegate(P1WireFrame frame) { frame.ResultCode = 1U; }), key, "request-result-code");
            ExpectEncodeRejected(Mutate(delegate(P1WireFrame frame) { frame.ResultDigest[0] = 1; }), key, "request-result-digest");
            ExpectEncodeRejected(Mutate(delegate(P1WireFrame frame) { frame.ExpiresAtMilliseconds += 2000UL; }), key, "lifetime-too-long");
            ExpectEncodeRejected(Mutate(delegate(P1WireFrame frame) { frame.SessionNonce = new byte[16]; }), key, "zero-session");
            P1WireFrame response = GoldenFixture.CreateResponse(GoldenFixture.CreateRequest());
            Check(P1WireCodec.Encode(response, key).Length == 512, "valid-response");
            response.ResultDigest = new byte[P1WireLayout.DigestSize];
            ExpectEncodeRejected(response, key, "zero-response-result-digest");
        }

        private static void TestRequestGate()
        {
            P1WireFrame first = GoldenFixture.CreateRequest();
            P1WireRequestGate gate = new P1WireRequestGate(first);
            Check(!gate.LiveAuthorization, "gate-live-authorization-false");
            Check(gate.TryAccept(first, first.IssuedAtMilliseconds) == P1WireGateStatus.Accepted, "gate-first");
            Check(gate.TryAccept(first, first.IssuedAtMilliseconds) == P1WireGateStatus.Replay, "gate-sequence-replay");

            P1WireFrame gap = first.Clone();
            GoldenFixture.AdvanceRequest(gap, 3UL);
            Check(gate.TryAccept(gap, first.IssuedAtMilliseconds + 1UL) == P1WireGateStatus.SequenceGap, "gate-gap");

            P1WireFrame second = first.Clone();
            GoldenFixture.AdvanceRequest(second, 2UL);
            Check(gate.TryAccept(second, second.IssuedAtMilliseconds - 1UL) == P1WireGateStatus.FromFuture, "gate-future");
            Check(gate.TryAccept(second, second.IssuedAtMilliseconds) == P1WireGateStatus.Accepted, "gate-second");

            P1WireFrame repeatedId = second.Clone();
            GoldenFixture.AdvanceRequest(repeatedId, 3UL);
            repeatedId.RequestId = (byte[])first.RequestId.Clone();
            Check(gate.TryAccept(repeatedId, repeatedId.IssuedAtMilliseconds) == P1WireGateStatus.RequestIdReplay, "gate-request-id-replay");

            P1WireRequestGate expiryGate = new P1WireRequestGate(first);
            Check(expiryGate.TryAccept(first, first.ExpiresAtMilliseconds) == P1WireGateStatus.Expired, "gate-expired");

            P1WireRequestGate clockGate = new P1WireRequestGate(first);
            Check(clockGate.TryAccept(first, first.IssuedAtMilliseconds + 100UL) == P1WireGateStatus.Accepted,
                "gate-clock-first");
            P1WireFrame clockSecond = first.Clone();
            GoldenFixture.AdvanceRequest(clockSecond, 2UL);
            Check(clockGate.TryAccept(clockSecond, first.IssuedAtMilliseconds + 200UL) == P1WireGateStatus.Accepted,
                "gate-clock-second");
            P1WireFrame clockThird = first.Clone();
            GoldenFixture.AdvanceRequest(clockThird, 3UL);
            Check(clockGate.TryAccept(clockThird, first.IssuedAtMilliseconds + 199UL) == P1WireGateStatus.ClockRollback,
                "gate-clock-rollback");
            Check(clockGate.ClockFaulted, "gate-clock-fault-latched");
            Check(clockGate.TryAccept(clockThird, first.IssuedAtMilliseconds + 300UL) == P1WireGateStatus.ClockFaulted,
                "gate-clock-permanent-fault");

            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.GameProcessId++; }, "game-pid");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.MainThreadId++; }, "main-tid");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.GameWindowHandle++; }, "hwnd");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.HelperProcessId++; }, "helper-pid");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.EasyLoaderProcessId++; }, "loader-pid");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.GameCreationTime++; }, "game-generation");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.HelperCreationTime++; }, "helper-generation");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.EasyLoaderCreationTime++; }, "loader-generation");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.SessionNonce[0]++; }, "session-nonce");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.EasyEpochNonce[0]++; }, "easy-epoch-nonce");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.BuildDigest[0]++; }, "build-digest");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.ProfileDigest[0]++; }, "profile-digest");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.ManifestDigest[0]++; }, "manifest-digest");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.EasyEpochDigest[0]++; }, "epoch-digest");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.EasyTicketDigest[0]++; }, "ticket-digest");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.ContextDigest[0]++; }, "context-digest");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.BridgeDigest[0]++; }, "bridge-digest");
            CheckBindingMutationRejected(first, delegate(P1WireFrame frame) { frame.MappingDigest[0]++; }, "mapping-digest");

            P1WireRequestGate budgetGate = new P1WireRequestGate(first);
            for (ulong sequence = 1UL; sequence <= (ulong)P1WireLayout.MaximumSessionPings; sequence++)
            {
                P1WireFrame request = first.Clone();
                GoldenFixture.AdvanceRequest(request, sequence);
                Check(budgetGate.TryAccept(request, request.IssuedAtMilliseconds) == P1WireGateStatus.Accepted,
                    "gate-budget-accept-" + sequence);
            }

            P1WireFrame overBudget = first.Clone();
            GoldenFixture.AdvanceRequest(overBudget, (ulong)P1WireLayout.MaximumSessionPings + 1UL);
            Check(budgetGate.TryAccept(overBudget, overBudget.IssuedAtMilliseconds) == P1WireGateStatus.BudgetExhausted,
                "gate-budget-exhausted");
        }

        private static void TestResponseVerifier()
        {
            P1WireFrame request = GoldenFixture.CreateRequest();
            P1WireFrame response = GoldenFixture.CreateResponse(request);
            Check(P1WireResponseVerifier.Verify(request, response), "response-match");
            response.RequestId[0] ^= 1;
            Check(!P1WireResponseVerifier.Verify(request, response), "response-request-id-mismatch");
            response = GoldenFixture.CreateResponse(request);
            response.ChallengeDigest[0] ^= 1;
            Check(!P1WireResponseVerifier.Verify(request, response), "response-challenge-mismatch");
            response = GoldenFixture.CreateResponse(request);
            response.Sequence++;
            Check(!P1WireResponseVerifier.Verify(request, response), "response-sequence-mismatch");
            response = GoldenFixture.CreateResponse(request);
            response.ResultDigest = new byte[P1WireLayout.DigestSize];
            Check(!P1WireResponseVerifier.Verify(request, response), "response-zero-result");
        }

        private static void TestAuthenticatedEntryPoints()
        {
            byte[] key = GoldenFixture.CreateKey();
            byte[] wrongKey = GoldenFixture.CreateKey();
            wrongKey[0] ^= 0x80;
            P1WireFrame request = GoldenFixture.CreateRequest();
            byte[] requestBytes = P1WireCodec.Encode(request, key);
            P1WireRequestGate gate = new P1WireRequestGate(request);
            P1WireDecodeStatus decodeStatus;
            P1WireFrame accepted;
            Check(gate.TryDecodeAndAccept(
                    requestBytes, key, request.IssuedAtMilliseconds,
                    out decodeStatus, out accepted) == P1WireGateStatus.Accepted,
                "authenticated-decode-and-accept");
            Check(decodeStatus == P1WireDecodeStatus.Accepted
                    && accepted != null && accepted.Sequence == request.Sequence,
                "authenticated-accepted-output");

            Check(gate.TryDecodeAndAccept(
                    requestBytes, wrongKey, request.IssuedAtMilliseconds,
                    out decodeStatus, out accepted) == P1WireGateStatus.AuthenticatedFrameRejected,
                "authenticated-wrong-key-rejected-before-gate");
            Check(decodeStatus == P1WireDecodeStatus.HmacMismatch && accepted == null,
                "authenticated-rejection-clears-output");

            P1WireFrame response = GoldenFixture.CreateResponse(request);
            byte[] responseBytes = P1WireCodec.Encode(response, key);
            P1WireFrame verified;
            Check(P1WireResponseVerifier.TryDecodeAndVerify(
                    request, responseBytes, key, out decodeStatus, out verified),
                "authenticated-decode-and-verify-response");
            Check(decodeStatus == P1WireDecodeStatus.Accepted
                    && verified != null && verified.Sequence == request.Sequence,
                "authenticated-response-output");
            Check(!P1WireResponseVerifier.TryDecodeAndVerify(
                    request, responseBytes, wrongKey, out decodeStatus, out verified),
                "authenticated-response-wrong-key-rejected");
            Check(decodeStatus == P1WireDecodeStatus.HmacMismatch && verified == null,
                "authenticated-response-rejection-clears-output");
        }

        private static void VerifyExternalGolden(byte[] candidate)
        {
            byte[] expected = P1WireCodec.Encode(GoldenFixture.CreateRequest(), GoldenFixture.CreateKey());
            P1WireFrame decoded;
            Check(candidate != null && candidate.Length == P1WireLayout.FrameSize, "external-size");
            Check(P1WireCodec.TryDecode(candidate, GoldenFixture.CreateKey(), out decoded) == P1WireDecodeStatus.Accepted,
                "external-decode");
            Check(Equal(expected, candidate), "external-byte-exact");
            if (failures != 0)
            {
                throw new InvalidOperationException("external golden did not match");
            }
        }

        private static void VerifyExternalResponseGolden(byte[] candidate)
        {
            byte[] key = GoldenFixture.CreateKey();
            P1WireFrame request = GoldenFixture.CreateRequest();
            byte[] expected = P1WireCodec.Encode(GoldenFixture.CreateResponse(request), key);
            P1WireDecodeStatus decodeStatus;
            P1WireFrame decoded;
            Check(candidate != null && candidate.Length == P1WireLayout.FrameSize,
                "external-response-size");
            Check(P1WireResponseVerifier.TryDecodeAndVerify(
                    request, candidate, key, out decodeStatus, out decoded),
                "external-response-authenticated-decode-verify");
            Check(decodeStatus == P1WireDecodeStatus.Accepted && decoded != null,
                "external-response-output");
            Check(Equal(expected, candidate), "external-response-byte-exact");
            if (failures != 0)
            {
                throw new InvalidOperationException("external response golden did not match");
            }
        }

        private static void CheckBindingMutationRejected(P1WireFrame template, Action<P1WireFrame> mutation, string name)
        {
            P1WireFrame changed = template.Clone();
            GoldenFixture.AdvanceRequest(changed, 1UL);
            mutation(changed);
            P1WireRequestGate gate = new P1WireRequestGate(template);
            Check(gate.TryAccept(changed, changed.IssuedAtMilliseconds) == P1WireGateStatus.BindingMismatch,
                "gate-binding-" + name);
        }

        private static P1WireFrame Mutate(Action<P1WireFrame> mutation)
        {
            P1WireFrame frame = GoldenFixture.CreateRequest();
            mutation(frame);
            return frame;
        }

        private static void ExpectEncodeRejected(P1WireFrame frame, byte[] key, string name)
        {
            bool rejected = false;
            try
            {
                P1WireCodec.Encode(frame, key);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Check(rejected, "encode-reject-" + name);
        }

        private static uint ComputeCrc(byte[] bytes)
        {
            uint crc = 0xffffffffU;
            for (int index = 0; index < bytes.Length; index++)
            {
                byte value = index >= P1WireLayout.Crc32Offset
                    && index < P1WireLayout.Crc32Offset + sizeof(uint)
                    ? (byte)0
                    : bytes[index];
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    uint mask = unchecked((uint)-(int)(crc & 1U));
                    crc = (crc >> 1) ^ (0xedb88320U & mask);
                }
            }

            return ~crc;
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            for (int index = 0; index < 8; index++)
            {
                bytes[offset + index] = (byte)(value >> (index * 8));
            }
        }

        private static void CheckAuthenticatedStatus(
            byte[] original,
            byte[] key,
            Action<byte[]> mutation,
            P1WireDecodeStatus expected,
            string name)
        {
            byte[] changed = (byte[])original.Clone();
            mutation(changed);
            Reauthenticate(changed, key);
            P1WireFrame decoded;
            Check(P1WireCodec.TryDecode(changed, key, out decoded) == expected, name);
        }

        private static void Reauthenticate(byte[] frame, byte[] key)
        {
            Array.Clear(frame, P1WireLayout.Crc32Offset, sizeof(uint));
            Array.Clear(frame, P1WireLayout.HmacOffset, P1WireLayout.DigestSize);
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                byte[] digest = hmac.ComputeHash(frame);
                Buffer.BlockCopy(digest, 0, frame, P1WireLayout.HmacOffset, digest.Length);
                Array.Clear(digest, 0, digest.Length);
            }
            WriteUInt32(frame, P1WireLayout.Crc32Offset, ComputeCrc(frame));
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            return P1WireCodec.ConstantTimeEqual(left, right);
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void Check(bool condition, string name)
        {
            checks++;
            if (!condition)
            {
                failures++;
                Console.Error.WriteLine("FAILED {0}", name);
            }
        }
    }
}
