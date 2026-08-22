using System;

namespace San9AutoDomestic.Bridge.Protocol
{
    public static class ProtocolFrameCodec
    {
        public static byte[] Encode(ProtocolFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            byte[] bytes = new byte[ProtocolWireLayout.FrameSize];
            LittleEndian.WriteUInt32(bytes, ProtocolWireLayout.MagicOffset, ProtocolWireLayout.Magic);
            LittleEndian.WriteUInt16(bytes, ProtocolWireLayout.SchemaMajorOffset, ProtocolWireLayout.SchemaMajor);
            LittleEndian.WriteUInt16(bytes, ProtocolWireLayout.SchemaMinorOffset, ProtocolWireLayout.SchemaMinor);
            LittleEndian.WriteUInt32(bytes, ProtocolWireLayout.FrameSizeOffset, ProtocolWireLayout.FrameSize);
            LittleEndian.WriteUInt16(bytes, ProtocolWireLayout.FrameKindOffset, (ushort)frame.Kind);
            LittleEndian.WriteUInt16(bytes, ProtocolWireLayout.StateOffset, (ushort)frame.State);
            LittleEndian.WriteUInt32(bytes, ProtocolWireLayout.FlagsOffset, 0);
            Copy(frame.SessionNonce, bytes, ProtocolWireLayout.SessionNonceOffset);
            LittleEndian.WriteUInt64(bytes, ProtocolWireLayout.SequenceOffset, frame.Sequence);
            Copy(frame.RequestId, bytes, ProtocolWireLayout.RequestIdOffset);
            Copy(frame.TargetIdentityDigest, bytes, ProtocolWireLayout.TargetIdentityDigestOffset);
            Copy(frame.ContextTokenDigest, bytes, ProtocolWireLayout.ContextTokenDigestOffset);
            LittleEndian.WriteInt64(bytes, ProtocolWireLayout.CreatedAtUtcTicksOffset, frame.CreatedAtUtcTicks);
            LittleEndian.WriteInt64(bytes, ProtocolWireLayout.ExpiresAtUtcTicksOffset, frame.ExpiresAtUtcTicks);
            LittleEndian.WriteUInt32(bytes, ProtocolWireLayout.OperationCodeOffset, frame.OperationCode);
            LittleEndian.WriteUInt32(bytes, ProtocolWireLayout.ResultCodeOffset, frame.ResultCode);
            Copy(frame.RequestPayloadDigest, bytes, ProtocolWireLayout.RequestPayloadDigestOffset);
            Copy(frame.ResultPayloadDigest, bytes, ProtocolWireLayout.ResultPayloadDigestOffset);
            Copy(frame.RequestBindingDigest, bytes, ProtocolWireLayout.RequestBindingDigestOffset);
            uint checksum = Crc32.ComputeWithZeroedChecksum(bytes);
            LittleEndian.WriteUInt32(bytes, ProtocolWireLayout.ChecksumOffset, checksum);
            return bytes;
        }

        public static bool TryDecode(byte[] bytes, out ProtocolFrame frame, out string error)
        {
            frame = null;
            error = null;
            if (bytes == null)
            {
                error = "FRAME_NULL";
                return false;
            }

            if (bytes.Length != ProtocolWireLayout.FrameSize)
            {
                error = "FRAME_SIZE_MISMATCH";
                return false;
            }

            if (LittleEndian.ReadUInt32(bytes, ProtocolWireLayout.MagicOffset) != ProtocolWireLayout.Magic)
            {
                error = "MAGIC_MISMATCH";
                return false;
            }

            if (LittleEndian.ReadUInt16(bytes, ProtocolWireLayout.SchemaMajorOffset) != ProtocolWireLayout.SchemaMajor
                || LittleEndian.ReadUInt16(bytes, ProtocolWireLayout.SchemaMinorOffset) != ProtocolWireLayout.SchemaMinor)
            {
                error = "SCHEMA_MISMATCH";
                return false;
            }

            if (LittleEndian.ReadUInt32(bytes, ProtocolWireLayout.FrameSizeOffset) != ProtocolWireLayout.FrameSize)
            {
                error = "DECLARED_SIZE_MISMATCH";
                return false;
            }

            uint expectedChecksum = LittleEndian.ReadUInt32(bytes, ProtocolWireLayout.ChecksumOffset);
            if (expectedChecksum != Crc32.ComputeWithZeroedChecksum(bytes))
            {
                error = "CHECKSUM_MISMATCH";
                return false;
            }

            if (LittleEndian.ReadUInt32(bytes, ProtocolWireLayout.FlagsOffset) != ProtocolWireLayout.AllowedFlags
                || !RangeIsZero(bytes, ProtocolWireLayout.ReservedHeaderOffset, ProtocolWireLayout.ReservedHeaderSize)
                || !RangeIsZero(bytes, ProtocolWireLayout.ReservedTailOffset, ProtocolWireLayout.ReservedTailSize))
            {
                error = "RESERVED_FIELD_NONZERO";
                return false;
            }

            ProtocolFrameKind kind = (ProtocolFrameKind)LittleEndian.ReadUInt16(
                bytes,
                ProtocolWireLayout.FrameKindOffset);
            ProtocolRequestState state = (ProtocolRequestState)LittleEndian.ReadUInt16(
                bytes,
                ProtocolWireLayout.StateOffset);
            if (kind != ProtocolFrameKind.Request
                && kind != ProtocolFrameKind.Claim
                && kind != ProtocolFrameKind.Result)
            {
                error = "FRAME_KIND_INVALID";
                return false;
            }

            if ((kind == ProtocolFrameKind.Request && state != ProtocolRequestState.Pending)
                || (kind == ProtocolFrameKind.Claim && state != ProtocolRequestState.Claimed)
                || (kind == ProtocolFrameKind.Result
                    && state != ProtocolRequestState.Completed
                    && state != ProtocolRequestState.Rejected))
            {
                error = "FRAME_STATE_INVALID";
                return false;
            }

            ulong sequence = LittleEndian.ReadUInt64(bytes, ProtocolWireLayout.SequenceOffset);
            long createdAtUtcTicks = LittleEndian.ReadInt64(bytes, ProtocolWireLayout.CreatedAtUtcTicksOffset);
            long expiresAtUtcTicks = LittleEndian.ReadInt64(bytes, ProtocolWireLayout.ExpiresAtUtcTicksOffset);
            uint operationCode = LittleEndian.ReadUInt32(bytes, ProtocolWireLayout.OperationCodeOffset);
            uint resultCode = LittleEndian.ReadUInt32(bytes, ProtocolWireLayout.ResultCodeOffset);
            if (sequence == 0 || createdAtUtcTicks <= 0 || expiresAtUtcTicks <= createdAtUtcTicks || operationCode == 0)
            {
                error = "FRAME_VALUE_INVALID";
                return false;
            }

            bool resultPayloadIsZero = RangeIsZero(
                bytes,
                ProtocolWireLayout.ResultPayloadDigestOffset,
                ProtocolWireLayout.DigestSize);
            bool bindingIsZero = RangeIsZero(
                bytes,
                ProtocolWireLayout.RequestBindingDigestOffset,
                ProtocolWireLayout.DigestSize);
            if (kind == ProtocolFrameKind.Request && (resultCode != 0 || !resultPayloadIsZero || !bindingIsZero))
            {
                error = "REQUEST_RESULT_FIELDS_NONZERO";
                return false;
            }

            if (kind == ProtocolFrameKind.Claim
                && (resultCode != 0 || !resultPayloadIsZero || bindingIsZero))
            {
                error = "CLAIM_FIELDS_INVALID";
                return false;
            }

            if (kind == ProtocolFrameKind.Result
                && (resultPayloadIsZero
                    || bindingIsZero
                    || (state == ProtocolRequestState.Completed && resultCode != 0)
                    || (state == ProtocolRequestState.Rejected && resultCode == 0)))
            {
                error = "RESULT_FIELDS_INVALID";
                return false;
            }

            try
            {
                frame = ProtocolFrame.FromDecoded(
                    kind,
                    state,
                    FixedValue.FromWire(
                        bytes,
                        ProtocolWireLayout.SessionNonceOffset,
                        ProtocolWireLayout.NonceSize,
                        true),
                    sequence,
                    FixedValue.FromWire(
                        bytes,
                        ProtocolWireLayout.RequestIdOffset,
                        ProtocolWireLayout.RequestIdSize,
                        true),
                    FixedValue.FromWire(
                        bytes,
                        ProtocolWireLayout.TargetIdentityDigestOffset,
                        ProtocolWireLayout.DigestSize,
                        true),
                    FixedValue.FromWire(
                        bytes,
                        ProtocolWireLayout.ContextTokenDigestOffset,
                        ProtocolWireLayout.DigestSize,
                        true),
                    createdAtUtcTicks,
                    expiresAtUtcTicks,
                    operationCode,
                    resultCode,
                    FixedValue.FromWire(
                        bytes,
                        ProtocolWireLayout.RequestPayloadDigestOffset,
                        ProtocolWireLayout.DigestSize,
                        true),
                    kind == ProtocolFrameKind.Result
                        ? FixedValue.FromWire(
                            bytes,
                            ProtocolWireLayout.ResultPayloadDigestOffset,
                            ProtocolWireLayout.DigestSize,
                            true)
                        : null,
                    kind != ProtocolFrameKind.Request
                        ? FixedValue.FromWire(
                            bytes,
                            ProtocolWireLayout.RequestBindingDigestOffset,
                            ProtocolWireLayout.DigestSize,
                            true)
                        : null);
            }
            catch (ArgumentException exception)
            {
                error = "FIXED_VALUE_INVALID:" + exception.Message;
                return false;
            }

            return true;
        }

        public static FixedValue ComputeRequestBindingDigest(ProtocolFrame request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (request.Kind != ProtocolFrameKind.Request || request.State != ProtocolRequestState.Pending)
            {
                throw new ArgumentException("Only a pending request can be bound.", "request");
            }

            return FixedValue.Sha256(Encode(request));
        }

        public static bool TryValidateResultBinding(
            ProtocolFrame request,
            ProtocolFrame result,
            out string error)
        {
            error = null;
            if (request == null || result == null)
            {
                error = "BINDING_FRAME_NULL";
                return false;
            }

            if (request.Kind != ProtocolFrameKind.Request || result.Kind != ProtocolFrameKind.Result)
            {
                error = "BINDING_KIND_INVALID";
                return false;
            }

            FixedValue expectedBinding = ComputeRequestBindingDigest(request);
            if (!expectedBinding.Equals(result.RequestBindingDigest)
                || !request.SessionNonce.Equals(result.SessionNonce)
                || request.Sequence != result.Sequence
                || !request.RequestId.Equals(result.RequestId)
                || !request.TargetIdentityDigest.Equals(result.TargetIdentityDigest)
                || !request.ContextTokenDigest.Equals(result.ContextTokenDigest)
                || request.CreatedAtUtcTicks != result.CreatedAtUtcTicks
                || request.ExpiresAtUtcTicks != result.ExpiresAtUtcTicks
                || request.OperationCode != result.OperationCode
                || !request.RequestPayloadDigest.Equals(result.RequestPayloadDigest))
            {
                error = "RESULT_REQUEST_BINDING_MISMATCH";
                return false;
            }

            return true;
        }

        public static bool TryValidateClaimBinding(
            ProtocolFrame request,
            ProtocolFrame claim,
            out string error)
        {
            error = null;
            if (request == null || claim == null)
            {
                error = "BINDING_FRAME_NULL";
                return false;
            }

            if (request.Kind != ProtocolFrameKind.Request
                || claim.Kind != ProtocolFrameKind.Claim
                || claim.State != ProtocolRequestState.Claimed)
            {
                error = "BINDING_KIND_INVALID";
                return false;
            }

            FixedValue expectedBinding = ComputeRequestBindingDigest(request);
            if (!expectedBinding.Equals(claim.RequestBindingDigest)
                || !request.SessionNonce.Equals(claim.SessionNonce)
                || request.Sequence != claim.Sequence
                || !request.RequestId.Equals(claim.RequestId)
                || !request.TargetIdentityDigest.Equals(claim.TargetIdentityDigest)
                || !request.ContextTokenDigest.Equals(claim.ContextTokenDigest)
                || request.CreatedAtUtcTicks != claim.CreatedAtUtcTicks
                || request.ExpiresAtUtcTicks != claim.ExpiresAtUtcTicks
                || request.OperationCode != claim.OperationCode
                || !request.RequestPayloadDigest.Equals(claim.RequestPayloadDigest)
                || claim.ResultCode != 0
                || claim.ResultPayloadDigest != null)
            {
                error = "CLAIM_REQUEST_BINDING_MISMATCH";
                return false;
            }

            return true;
        }

        private static void Copy(FixedValue value, byte[] destination, int offset)
        {
            if (value == null)
            {
                return;
            }

            byte[] source = value.ToArray();
            Buffer.BlockCopy(source, 0, destination, offset, source.Length);
        }

        private static bool RangeIsZero(byte[] bytes, int offset, int length)
        {
            int aggregate = 0;
            for (int index = offset; index < offset + length; index++)
            {
                aggregate |= bytes[index];
            }

            return aggregate == 0;
        }
    }
}
