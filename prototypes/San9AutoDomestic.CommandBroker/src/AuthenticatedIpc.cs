using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace San9AutoDomestic.CommandBroker
{
    public enum AuthenticatedIpcMessageType
    {
        StartSingleCommand = 1,
        StopRequested = 2,
        TerminalResult = 3,
        JournalStatus = 4
    }

    public sealed class AuthenticatedIpcMessage
    {
        private readonly byte[] payload;

        internal AuthenticatedIpcMessage(long sequence, AuthenticatedIpcMessageType messageType, byte[] payload)
        {
            Sequence = sequence;
            MessageType = messageType;
            this.payload = BinaryValue.Clone(payload);
        }

        public long Sequence { get; private set; }

        public AuthenticatedIpcMessageType MessageType { get; private set; }

        public byte[] GetPayloadCopy()
        {
            return BinaryValue.Clone(payload);
        }
    }

    public enum AuthenticatedIpcDecodeStatus
    {
        Accepted = 1,
        PartialFrame = 2,
        TrailingData = 3,
        Oversize = 4,
        InvalidLength = 5,
        WrongHmac = 6,
        WrongMagic = 7,
        WrongSchema = 8,
        WrongNonce = 9,
        Replay = 10,
        SequenceGap = 11,
        UnknownMessageType = 12,
        InvalidPayload = 13
    }

    public sealed class AuthenticatedIpcDecodeResult
    {
        internal AuthenticatedIpcDecodeResult(
            AuthenticatedIpcDecodeStatus status,
            AuthenticatedIpcMessage message)
        {
            Status = status;
            Message = message;
        }

        public AuthenticatedIpcDecodeStatus Status { get; private set; }

        public AuthenticatedIpcMessage Message { get; private set; }
    }

    public static class AuthenticatedIpcProtocol
    {
        public const int SchemaVersion = 1;
        public const int KeyBytes = 32;
        public const int NonceBytes = 32;
        public const int HmacBytes = 32;
        public const int MaximumPayloadBytes = 16 * 1024;
        public const int MaximumFrameBytes = 4 + 8 + 4 + 8 + 4 + NonceBytes + 4 + MaximumPayloadBytes + HmacBytes;

        internal const int FixedBodyBytes = 8 + 4 + 8 + 4 + NonceBytes + 4;
        internal static readonly byte[] Magic = Encoding.ASCII.GetBytes("S9CBIP01");
    }

    public sealed class AuthenticatedIpcSender : IDisposable
    {
        private readonly object sync;
        private readonly byte[] nonce;
        private byte[] key;
        private long nextSequence;

        public AuthenticatedIpcSender(byte[] key, byte[] nonce)
        {
            BinaryValue.RequireLength(key, AuthenticatedIpcProtocol.KeyBytes, "key");
            BinaryValue.RequireLength(nonce, AuthenticatedIpcProtocol.NonceBytes, "nonce");
            sync = new object();
            this.key = BinaryValue.Clone(key);
            this.nonce = BinaryValue.Clone(nonce);
            nextSequence = 1;
        }

        public long NextSequence
        {
            get
            {
                lock (sync)
                {
                    ThrowIfDisposed();
                    return nextSequence;
                }
            }
        }

        public byte[] EncodeNext(AuthenticatedIpcMessageType messageType, byte[] payload)
        {
            if (!Enum.IsDefined(typeof(AuthenticatedIpcMessageType), messageType))
            {
                throw new ArgumentOutOfRangeException("messageType");
            }

            byte[] actualPayload = payload ?? new byte[0];
            if (actualPayload.Length > AuthenticatedIpcProtocol.MaximumPayloadBytes)
            {
                throw new ArgumentException("IPC payload exceeds the protocol bound.", "payload");
            }

            lock (sync)
            {
                ThrowIfDisposed();
                if (nextSequence <= 0 || nextSequence == long.MaxValue)
                {
                    throw new InvalidOperationException("IPC sequence is exhausted.");
                }

                byte[] body = SerializeBody(nextSequence, messageType, nonce, actualPayload);
                byte[] mac;
                using (HMACSHA256 hmac = new HMACSHA256(key))
                {
                    mac = hmac.ComputeHash(body);
                }

                int declaredLength = checked(body.Length + mac.Length);
                byte[] frame = new byte[checked(sizeof(int) + declaredLength)];
                Buffer.BlockCopy(BitConverter.GetBytes(declaredLength), 0, frame, 0, sizeof(int));
                Buffer.BlockCopy(body, 0, frame, sizeof(int), body.Length);
                Buffer.BlockCopy(mac, 0, frame, sizeof(int) + body.Length, mac.Length);
                nextSequence++;
                return frame;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (key != null)
                {
                    Array.Clear(key, 0, key.Length);
                    key = null;
                }

                Array.Clear(nonce, 0, nonce.Length);
            }
        }

        private static byte[] SerializeBody(
            long sequence,
            AuthenticatedIpcMessageType messageType,
            byte[] nonce,
            byte[] payload)
        {
            using (MemoryStream memory = new MemoryStream(
                AuthenticatedIpcProtocol.FixedBodyBytes + payload.Length))
            using (BinaryWriter writer = new BinaryWriter(memory, Encoding.UTF8))
            {
                writer.Write(AuthenticatedIpcProtocol.Magic);
                writer.Write(AuthenticatedIpcProtocol.SchemaVersion);
                writer.Write(sequence);
                writer.Write((int)messageType);
                writer.Write(nonce);
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                return memory.ToArray();
            }
        }

        private void ThrowIfDisposed()
        {
            if (key == null)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }

    public sealed class AuthenticatedIpcReceiver : IDisposable
    {
        private readonly object sync;
        private readonly byte[] nonce;
        private byte[] key;
        private long expectedSequence;

        public AuthenticatedIpcReceiver(byte[] key, byte[] nonce)
        {
            BinaryValue.RequireLength(key, AuthenticatedIpcProtocol.KeyBytes, "key");
            BinaryValue.RequireLength(nonce, AuthenticatedIpcProtocol.NonceBytes, "nonce");
            sync = new object();
            this.key = BinaryValue.Clone(key);
            this.nonce = BinaryValue.Clone(nonce);
            expectedSequence = 1;
        }

        public long ExpectedSequence
        {
            get
            {
                lock (sync)
                {
                    ThrowIfDisposed();
                    return expectedSequence;
                }
            }
        }

        public AuthenticatedIpcDecodeResult Decode(byte[] frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            lock (sync)
            {
                ThrowIfDisposed();
                if (frame.Length < sizeof(int))
                {
                    return Result(AuthenticatedIpcDecodeStatus.PartialFrame);
                }

                int declaredLength = BitConverter.ToInt32(frame, 0);
                int minimumEnvelope = AuthenticatedIpcProtocol.FixedBodyBytes + AuthenticatedIpcProtocol.HmacBytes;
                int maximumEnvelope = AuthenticatedIpcProtocol.MaximumFrameBytes - sizeof(int);
                if (declaredLength > maximumEnvelope)
                {
                    return Result(AuthenticatedIpcDecodeStatus.Oversize);
                }

                if (declaredLength < minimumEnvelope)
                {
                    return Result(AuthenticatedIpcDecodeStatus.InvalidLength);
                }

                int expectedFrameLength = checked(sizeof(int) + declaredLength);
                if (frame.Length < expectedFrameLength)
                {
                    return Result(AuthenticatedIpcDecodeStatus.PartialFrame);
                }

                if (frame.Length > expectedFrameLength)
                {
                    return Result(AuthenticatedIpcDecodeStatus.TrailingData);
                }

                int bodyLength = declaredLength - AuthenticatedIpcProtocol.HmacBytes;
                byte[] body = new byte[bodyLength];
                byte[] persistedMac = new byte[AuthenticatedIpcProtocol.HmacBytes];
                Buffer.BlockCopy(frame, sizeof(int), body, 0, body.Length);
                Buffer.BlockCopy(
                    frame,
                    sizeof(int) + body.Length,
                    persistedMac,
                    0,
                    persistedMac.Length);

                byte[] computedMac;
                using (HMACSHA256 hmac = new HMACSHA256(key))
                {
                    computedMac = hmac.ComputeHash(body);
                }

                if (!BinaryValue.AreEqual(persistedMac, computedMac))
                {
                    return Result(AuthenticatedIpcDecodeStatus.WrongHmac);
                }

                long sequence;
                AuthenticatedIpcMessageType messageType;
                byte[] payload;
                AuthenticatedIpcDecodeStatus parseStatus = ParseAuthenticatedBody(
                    body,
                    out sequence,
                    out messageType,
                    out payload);
                if (parseStatus != AuthenticatedIpcDecodeStatus.Accepted)
                {
                    return Result(parseStatus);
                }

                if (sequence < expectedSequence)
                {
                    return Result(AuthenticatedIpcDecodeStatus.Replay);
                }

                if (sequence > expectedSequence)
                {
                    return Result(AuthenticatedIpcDecodeStatus.SequenceGap);
                }

                if (expectedSequence == long.MaxValue)
                {
                    return Result(AuthenticatedIpcDecodeStatus.SequenceGap);
                }

                AuthenticatedIpcMessage message = new AuthenticatedIpcMessage(
                    sequence,
                    messageType,
                    payload);
                expectedSequence++;
                return new AuthenticatedIpcDecodeResult(
                    AuthenticatedIpcDecodeStatus.Accepted,
                    message);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (key != null)
                {
                    Array.Clear(key, 0, key.Length);
                    key = null;
                }

                Array.Clear(nonce, 0, nonce.Length);
            }
        }

        private AuthenticatedIpcDecodeStatus ParseAuthenticatedBody(
            byte[] body,
            out long sequence,
            out AuthenticatedIpcMessageType messageType,
            out byte[] payload)
        {
            sequence = 0;
            messageType = 0;
            payload = null;
            try
            {
                using (MemoryStream memory = new MemoryStream(body, false))
                using (BinaryReader reader = new BinaryReader(memory, Encoding.UTF8))
                {
                    byte[] magic = reader.ReadBytes(AuthenticatedIpcProtocol.Magic.Length);
                    int schema = reader.ReadInt32();
                    sequence = reader.ReadInt64();
                    int rawMessageType = reader.ReadInt32();
                    byte[] persistedNonce = reader.ReadBytes(AuthenticatedIpcProtocol.NonceBytes);
                    int payloadLength = reader.ReadInt32();

                    if (!BinaryValue.AreEqual(magic, AuthenticatedIpcProtocol.Magic))
                    {
                        return AuthenticatedIpcDecodeStatus.WrongMagic;
                    }

                    if (schema != AuthenticatedIpcProtocol.SchemaVersion)
                    {
                        return AuthenticatedIpcDecodeStatus.WrongSchema;
                    }

                    if (!BinaryValue.AreEqual(persistedNonce, nonce))
                    {
                        return AuthenticatedIpcDecodeStatus.WrongNonce;
                    }

                    if (!Enum.IsDefined(typeof(AuthenticatedIpcMessageType), rawMessageType))
                    {
                        return AuthenticatedIpcDecodeStatus.UnknownMessageType;
                    }

                    if (sequence <= 0
                        || payloadLength < 0
                        || payloadLength > AuthenticatedIpcProtocol.MaximumPayloadBytes
                        || memory.Length - memory.Position != payloadLength)
                    {
                        return AuthenticatedIpcDecodeStatus.InvalidPayload;
                    }

                    payload = reader.ReadBytes(payloadLength);
                    if (payload.Length != payloadLength || memory.Position != memory.Length)
                    {
                        return AuthenticatedIpcDecodeStatus.InvalidPayload;
                    }

                    messageType = (AuthenticatedIpcMessageType)rawMessageType;
                    return AuthenticatedIpcDecodeStatus.Accepted;
                }
            }
            catch
            {
                return AuthenticatedIpcDecodeStatus.InvalidPayload;
            }
        }

        private static AuthenticatedIpcDecodeResult Result(AuthenticatedIpcDecodeStatus status)
        {
            return new AuthenticatedIpcDecodeResult(status, null);
        }

        private void ThrowIfDisposed()
        {
            if (key == null)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
