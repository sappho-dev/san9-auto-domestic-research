using System;
using System.Security.Cryptography;

namespace San9AutoDomestic.P1Wire
{
    public static class P1WireCodec
    {
        public static byte[] Encode(P1WireFrame frame, byte[] hmacKey)
        {
            string error;
            if (!ValidateFrame(frame, out error))
            {
                throw new ArgumentException(error, "frame");
            }

            RequireKey(hmacKey);
            byte[] output = new byte[P1WireLayout.FrameSize];
            WireBinary.WriteUInt32(output, P1WireLayout.MagicOffset, P1WireLayout.Magic);
            WireBinary.WriteUInt16(output, P1WireLayout.SchemaMajorOffset, P1WireLayout.SchemaMajor);
            WireBinary.WriteUInt16(output, P1WireLayout.SchemaMinorOffset, P1WireLayout.SchemaMinor);
            WireBinary.WriteUInt32(output, P1WireLayout.DeclaredSizeOffset, P1WireLayout.FrameSize);
            WireBinary.WriteUInt16(output, P1WireLayout.KindOffset, (ushort)frame.Kind);
            WireBinary.WriteUInt16(output, P1WireLayout.StateOffset, (ushort)frame.State);
            WireBinary.WriteUInt32(output, P1WireLayout.FlagsOffset, frame.Flags);
            WireBinary.WriteUInt32(output, P1WireLayout.ResultCodeOffset, frame.ResultCode);
            WireBinary.WriteUInt64(output, P1WireLayout.SequenceOffset, frame.Sequence);
            WireBinary.WriteUInt64(output, P1WireLayout.IssuedAtMillisecondsOffset, frame.IssuedAtMilliseconds);
            WireBinary.WriteUInt64(output, P1WireLayout.ExpiresAtMillisecondsOffset, frame.ExpiresAtMilliseconds);
            WireBinary.WriteUInt32(output, P1WireLayout.GameProcessIdOffset, frame.GameProcessId);
            WireBinary.WriteUInt32(output, P1WireLayout.MainThreadIdOffset, frame.MainThreadId);
            WireBinary.WriteUInt32(output, P1WireLayout.GameWindowHandleOffset, frame.GameWindowHandle);
            WireBinary.WriteUInt32(output, P1WireLayout.HelperProcessIdOffset, frame.HelperProcessId);
            WireBinary.WriteUInt32(output, P1WireLayout.EasyLoaderProcessIdOffset, frame.EasyLoaderProcessId);
            WireBinary.WriteUInt64(output, P1WireLayout.GameCreationTimeOffset, frame.GameCreationTime);
            WireBinary.WriteUInt64(output, P1WireLayout.HelperCreationTimeOffset, frame.HelperCreationTime);
            WireBinary.WriteUInt64(output, P1WireLayout.EasyLoaderCreationTimeOffset, frame.EasyLoaderCreationTime);
            Copy(frame.SessionNonce, output, P1WireLayout.SessionNonceOffset);
            Copy(frame.RequestId, output, P1WireLayout.RequestIdOffset);
            Copy(frame.EasyEpochNonce, output, P1WireLayout.EasyEpochNonceOffset);
            Copy(frame.BuildDigest, output, P1WireLayout.BuildDigestOffset);
            Copy(frame.ProfileDigest, output, P1WireLayout.ProfileDigestOffset);
            Copy(frame.ManifestDigest, output, P1WireLayout.ManifestDigestOffset);
            Copy(frame.EasyEpochDigest, output, P1WireLayout.EasyEpochDigestOffset);
            Copy(frame.EasyTicketDigest, output, P1WireLayout.EasyTicketDigestOffset);
            Copy(frame.ContextDigest, output, P1WireLayout.ContextDigestOffset);
            Copy(frame.BridgeDigest, output, P1WireLayout.BridgeDigestOffset);
            Copy(frame.MappingDigest, output, P1WireLayout.MappingDigestOffset);
            Copy(frame.ChallengeDigest, output, P1WireLayout.ChallengeDigestOffset);
            Copy(frame.ResultDigest, output, P1WireLayout.ResultDigestOffset);

            byte[] mac = ComputeHmac(output, hmacKey);
            Buffer.BlockCopy(mac, 0, output, P1WireLayout.HmacOffset, mac.Length);
            Array.Clear(mac, 0, mac.Length);
            WireBinary.WriteUInt32(output, P1WireLayout.Crc32Offset, P1Crc32.Compute(output));
            return output;
        }

        public static P1WireDecodeStatus TryDecode(
            byte[] bytes,
            byte[] hmacKey,
            out P1WireFrame frame)
        {
            frame = null;
            if (bytes == null)
            {
                return P1WireDecodeStatus.NullFrame;
            }

            if (bytes.Length != P1WireLayout.FrameSize)
            {
                return P1WireDecodeStatus.SizeMismatch;
            }

            if (!KeyIsValid(hmacKey))
            {
                return P1WireDecodeStatus.KeyInvalid;
            }

            if (WireBinary.ReadUInt32(bytes, P1WireLayout.MagicOffset) != P1WireLayout.Magic)
            {
                return P1WireDecodeStatus.MagicMismatch;
            }

            if (WireBinary.ReadUInt16(bytes, P1WireLayout.SchemaMajorOffset) != P1WireLayout.SchemaMajor
                || WireBinary.ReadUInt16(bytes, P1WireLayout.SchemaMinorOffset) != P1WireLayout.SchemaMinor)
            {
                return P1WireDecodeStatus.SchemaMismatch;
            }

            if (WireBinary.ReadUInt32(bytes, P1WireLayout.DeclaredSizeOffset) != P1WireLayout.FrameSize)
            {
                return P1WireDecodeStatus.DeclaredSizeMismatch;
            }

            uint persistedCrc = WireBinary.ReadUInt32(bytes, P1WireLayout.Crc32Offset);
            if (persistedCrc != P1Crc32.Compute(bytes))
            {
                return P1WireDecodeStatus.CrcMismatch;
            }

            byte[] expectedMac = ComputeHmac(bytes, hmacKey);
            bool macMatches = ConstantTimeEqual(
                expectedMac,
                0,
                bytes,
                P1WireLayout.HmacOffset,
                P1WireLayout.DigestSize);
            Array.Clear(expectedMac, 0, expectedMac.Length);
            if (!macMatches)
            {
                return P1WireDecodeStatus.HmacMismatch;
            }

            if (WireBinary.ReadUInt32(bytes, P1WireLayout.FlagsOffset) != P1WireLayout.AllowedFlags
                || !RangeIsZero(bytes, P1WireLayout.ReservedHeaderOffset, P1WireLayout.ReservedHeaderSize)
                || !RangeIsZero(bytes, P1WireLayout.ReservedBindingOffset, P1WireLayout.ReservedBindingSize)
                || !RangeIsZero(bytes, P1WireLayout.ReservedTailOffset, P1WireLayout.ReservedTailSize))
            {
                return P1WireDecodeStatus.ReservedOrFlagsInvalid;
            }

            P1WireFrame decoded = new P1WireFrame
            {
                Kind = (P1WireKind)WireBinary.ReadUInt16(bytes, P1WireLayout.KindOffset),
                State = (P1WireState)WireBinary.ReadUInt16(bytes, P1WireLayout.StateOffset),
                Flags = WireBinary.ReadUInt32(bytes, P1WireLayout.FlagsOffset),
                ResultCode = WireBinary.ReadUInt32(bytes, P1WireLayout.ResultCodeOffset),
                Sequence = WireBinary.ReadUInt64(bytes, P1WireLayout.SequenceOffset),
                IssuedAtMilliseconds = WireBinary.ReadUInt64(bytes, P1WireLayout.IssuedAtMillisecondsOffset),
                ExpiresAtMilliseconds = WireBinary.ReadUInt64(bytes, P1WireLayout.ExpiresAtMillisecondsOffset),
                GameProcessId = WireBinary.ReadUInt32(bytes, P1WireLayout.GameProcessIdOffset),
                MainThreadId = WireBinary.ReadUInt32(bytes, P1WireLayout.MainThreadIdOffset),
                GameWindowHandle = WireBinary.ReadUInt32(bytes, P1WireLayout.GameWindowHandleOffset),
                HelperProcessId = WireBinary.ReadUInt32(bytes, P1WireLayout.HelperProcessIdOffset),
                EasyLoaderProcessId = WireBinary.ReadUInt32(bytes, P1WireLayout.EasyLoaderProcessIdOffset),
                GameCreationTime = WireBinary.ReadUInt64(bytes, P1WireLayout.GameCreationTimeOffset),
                HelperCreationTime = WireBinary.ReadUInt64(bytes, P1WireLayout.HelperCreationTimeOffset),
                EasyLoaderCreationTime = WireBinary.ReadUInt64(bytes, P1WireLayout.EasyLoaderCreationTimeOffset),
                SessionNonce = Slice(bytes, P1WireLayout.SessionNonceOffset, P1WireLayout.NonceSize),
                RequestId = Slice(bytes, P1WireLayout.RequestIdOffset, P1WireLayout.NonceSize),
                EasyEpochNonce = Slice(bytes, P1WireLayout.EasyEpochNonceOffset, P1WireLayout.NonceSize),
                BuildDigest = Slice(bytes, P1WireLayout.BuildDigestOffset, P1WireLayout.DigestSize),
                ProfileDigest = Slice(bytes, P1WireLayout.ProfileDigestOffset, P1WireLayout.DigestSize),
                ManifestDigest = Slice(bytes, P1WireLayout.ManifestDigestOffset, P1WireLayout.DigestSize),
                EasyEpochDigest = Slice(bytes, P1WireLayout.EasyEpochDigestOffset, P1WireLayout.DigestSize),
                EasyTicketDigest = Slice(bytes, P1WireLayout.EasyTicketDigestOffset, P1WireLayout.DigestSize),
                ContextDigest = Slice(bytes, P1WireLayout.ContextDigestOffset, P1WireLayout.DigestSize),
                BridgeDigest = Slice(bytes, P1WireLayout.BridgeDigestOffset, P1WireLayout.DigestSize),
                MappingDigest = Slice(bytes, P1WireLayout.MappingDigestOffset, P1WireLayout.DigestSize),
                ChallengeDigest = Slice(bytes, P1WireLayout.ChallengeDigestOffset, P1WireLayout.DigestSize),
                ResultDigest = Slice(bytes, P1WireLayout.ResultDigestOffset, P1WireLayout.DigestSize)
            };

            string validationError;
            if (!ValidateFrame(decoded, out validationError))
            {
                if (validationError == "KIND_STATE") return P1WireDecodeStatus.KindStateInvalid;
                if (validationError == "LIFETIME") return P1WireDecodeStatus.LifetimeInvalid;
                if (validationError == "REQUEST_SHAPE") return P1WireDecodeStatus.RequestShapeInvalid;
                if (validationError == "RESPONSE_SHAPE") return P1WireDecodeStatus.ResponseShapeInvalid;
                return P1WireDecodeStatus.RequiredFieldInvalid;
            }

            frame = decoded;
            return P1WireDecodeStatus.Accepted;
        }

        public static bool ConstantTimeEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            return ConstantTimeEqual(left, 0, right, 0, left.Length);
        }

        internal static bool ValidateFrame(P1WireFrame frame, out string error)
        {
            error = null;
            if (frame == null)
            {
                error = "FRAME_NULL";
                return false;
            }

            bool request = frame.Kind == P1WireKind.PingRequest && frame.State == P1WireState.Pending;
            bool response = frame.Kind == P1WireKind.PingResponse
                && (frame.State == P1WireState.Completed || frame.State == P1WireState.Rejected);
            if (!request && !response)
            {
                error = "KIND_STATE";
                return false;
            }

            if (frame.Flags != P1WireLayout.AllowedFlags
                || frame.Sequence == 0UL
                || frame.GameProcessId == 0U
                || frame.MainThreadId == 0U
                || frame.GameWindowHandle == 0U
                || frame.HelperProcessId == 0U
                || frame.EasyLoaderProcessId == 0U
                || frame.GameCreationTime == 0UL
                || frame.HelperCreationTime == 0UL
                || frame.EasyLoaderCreationTime == 0UL
                || !Required(frame.SessionNonce, P1WireLayout.NonceSize)
                || !Required(frame.RequestId, P1WireLayout.NonceSize)
                || !Required(frame.EasyEpochNonce, P1WireLayout.NonceSize)
                || !Required(frame.BuildDigest, P1WireLayout.DigestSize)
                || !Required(frame.ProfileDigest, P1WireLayout.DigestSize)
                || !Required(frame.ManifestDigest, P1WireLayout.DigestSize)
                || !Required(frame.EasyEpochDigest, P1WireLayout.DigestSize)
                || !Required(frame.EasyTicketDigest, P1WireLayout.DigestSize)
                || !Required(frame.ContextDigest, P1WireLayout.DigestSize)
                || !Required(frame.BridgeDigest, P1WireLayout.DigestSize)
                || !Required(frame.MappingDigest, P1WireLayout.DigestSize)
                || !Required(frame.ChallengeDigest, P1WireLayout.DigestSize)
                || frame.ResultDigest == null
                || frame.ResultDigest.Length != P1WireLayout.DigestSize)
            {
                error = "REQUIRED";
                return false;
            }

            if (frame.IssuedAtMilliseconds == 0UL
                || frame.ExpiresAtMilliseconds <= frame.IssuedAtMilliseconds
                || frame.ExpiresAtMilliseconds - frame.IssuedAtMilliseconds
                    > P1WireLayout.MaximumLifetimeMilliseconds)
            {
                error = "LIFETIME";
                return false;
            }

            if (request && (frame.ResultCode != 0U || !AllZero(frame.ResultDigest)))
            {
                error = "REQUEST_SHAPE";
                return false;
            }

            if (response && (AllZero(frame.ResultDigest)
                || (frame.State == P1WireState.Completed && frame.ResultCode != 0U)
                || (frame.State == P1WireState.Rejected && frame.ResultCode == 0U)))
            {
                error = "RESPONSE_SHAPE";
                return false;
            }

            return true;
        }

        private static byte[] ComputeHmac(byte[] bytes, byte[] key)
        {
            byte[] canonical = (byte[])bytes.Clone();
            Array.Clear(canonical, P1WireLayout.Crc32Offset, sizeof(uint));
            Array.Clear(canonical, P1WireLayout.HmacOffset, P1WireLayout.DigestSize);
            try
            {
                using (HMACSHA256 hmac = new HMACSHA256(key))
                {
                    return hmac.ComputeHash(canonical);
                }
            }
            finally
            {
                Array.Clear(canonical, 0, canonical.Length);
            }
        }

        private static bool ConstantTimeEqual(
            byte[] left,
            int leftOffset,
            byte[] right,
            int rightOffset,
            int length)
        {
            int difference = 0;
            for (int index = 0; index < length; index++)
            {
                difference |= left[leftOffset + index] ^ right[rightOffset + index];
            }

            return difference == 0;
        }

        private static void RequireKey(byte[] key)
        {
            if (!KeyIsValid(key))
            {
                throw new ArgumentException("A nonzero 32-byte HMAC key is required.", "hmacKey");
            }
        }

        private static bool KeyIsValid(byte[] key)
        {
            return key != null && key.Length == P1WireLayout.HmacKeySize && !AllZero(key);
        }

        private static bool Required(byte[] value, int size)
        {
            return value != null && value.Length == size && !AllZero(value);
        }

        private static bool AllZero(byte[] value)
        {
            if (value == null) return true;
            int aggregate = 0;
            for (int index = 0; index < value.Length; index++) aggregate |= value[index];
            return aggregate == 0;
        }

        private static bool RangeIsZero(byte[] value, int offset, int size)
        {
            int aggregate = 0;
            for (int index = 0; index < size; index++) aggregate |= value[offset + index];
            return aggregate == 0;
        }

        private static void Copy(byte[] source, byte[] target, int offset)
        {
            Buffer.BlockCopy(source, 0, target, offset, source.Length);
        }

        private static byte[] Slice(byte[] source, int offset, int size)
        {
            byte[] result = new byte[size];
            Buffer.BlockCopy(source, offset, result, 0, size);
            return result;
        }
    }

    internal static class P1Crc32
    {
        internal static uint Compute(byte[] bytes)
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
    }

    internal static class WireBinary
    {
        internal static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        internal static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));
        }

        internal static ulong ReadUInt64(byte[] bytes, int offset)
        {
            ulong value = 0UL;
            for (int index = 0; index < 8; index++) value |= (ulong)bytes[offset + index] << (index * 8);
            return value;
        }

        internal static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }

        internal static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        internal static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            for (int index = 0; index < 8; index++) bytes[offset + index] = (byte)(value >> (index * 8));
        }
    }
}
