using System;

namespace San9AutoDomestic.P1Wire.SelfTest
{
    internal static class GoldenFixture
    {
        internal static readonly string ExpectedGoldenSha256 =
            "acadf1f1c5cf70672484632e32865871058e3193b84ffc5c233990985fbe80d5";
        internal static readonly string ExpectedResponseGoldenSha256 =
            "2dcef30e7c5b5219f255d97c7a260019748551d2279c868e62539ce204a0e87e";

        internal static byte[] CreateKey()
        {
            return Fill(P1WireLayout.HmacKeySize, 0xd0);
        }

        internal static P1WireFrame CreateRequest()
        {
            return new P1WireFrame
            {
                Kind = P1WireKind.PingRequest,
                State = P1WireState.Pending,
                Flags = 0U,
                ResultCode = 0U,
                Sequence = 1UL,
                IssuedAtMilliseconds = 1000000UL,
                ExpiresAtMilliseconds = 1004000UL,
                GameProcessId = 0x11223344U,
                MainThreadId = 0x12345678U,
                GameWindowHandle = 0x00123456U,
                HelperProcessId = 0x55667788U,
                EasyLoaderProcessId = 0x01020304U,
                GameCreationTime = 0x0102030405060708UL,
                HelperCreationTime = 0x1112131415161718UL,
                EasyLoaderCreationTime = 0x2122232425262728UL,
                SessionNonce = Fill(P1WireLayout.NonceSize, 0x10),
                RequestId = Fill(P1WireLayout.NonceSize, 0x20),
                EasyEpochNonce = Fill(P1WireLayout.NonceSize, 0x30),
                BuildDigest = Fill(P1WireLayout.DigestSize, 0x40),
                ProfileDigest = Fill(P1WireLayout.DigestSize, 0x50),
                ManifestDigest = Fill(P1WireLayout.DigestSize, 0x60),
                EasyEpochDigest = Fill(P1WireLayout.DigestSize, 0x70),
                EasyTicketDigest = Fill(P1WireLayout.DigestSize, 0x80),
                ContextDigest = Fill(P1WireLayout.DigestSize, 0x90),
                BridgeDigest = Fill(P1WireLayout.DigestSize, 0xa0),
                MappingDigest = Fill(P1WireLayout.DigestSize, 0xb0),
                ChallengeDigest = Fill(P1WireLayout.DigestSize, 0xc0),
                ResultDigest = new byte[P1WireLayout.DigestSize]
            };
        }

        internal static P1WireFrame CreateResponse(P1WireFrame request)
        {
            P1WireFrame response = request.Clone();
            response.Kind = P1WireKind.PingResponse;
            response.State = P1WireState.Completed;
            response.ResultCode = 0U;
            response.ResultDigest = Fill(P1WireLayout.DigestSize, 0xe0);
            return response;
        }

        internal static byte[] Fill(int size, int seed)
        {
            byte[] value = new byte[size];
            for (int index = 0; index < value.Length; index++)
            {
                value[index] = (byte)(seed + (index * 17));
            }

            return value;
        }

        internal static void AdvanceRequest(P1WireFrame request, ulong sequence)
        {
            request.Sequence = sequence;
            request.IssuedAtMilliseconds = 1000000UL + (sequence * 10UL);
            request.ExpiresAtMilliseconds = request.IssuedAtMilliseconds + 4000UL;
            request.RequestId = Fill(P1WireLayout.NonceSize, unchecked((int)(0x20U + (uint)sequence)));
        }
    }
}
