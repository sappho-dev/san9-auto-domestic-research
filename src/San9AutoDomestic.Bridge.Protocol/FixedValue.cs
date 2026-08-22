using System;
using System.Security.Cryptography;

namespace San9AutoDomestic.Bridge.Protocol
{
    public sealed class FixedValue : IEquatable<FixedValue>
    {
        private readonly byte[] bytes;

        private FixedValue(byte[] value, int expectedSize, bool rejectZero)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            if (value.Length != expectedSize)
            {
                throw new ArgumentException("Unexpected fixed-value width.", "value");
            }

            if (rejectZero && IsAllZero(value))
            {
                throw new ArgumentException("A security binding value cannot be all zero.", "value");
            }

            bytes = (byte[])value.Clone();
        }

        public int Length
        {
            get { return bytes.Length; }
        }

        public static FixedValue Nonce(byte[] value)
        {
            return new FixedValue(value, ProtocolWireLayout.NonceSize, true);
        }

        public static FixedValue CreateRandomNonce()
        {
            return CreateRandom(ProtocolWireLayout.NonceSize);
        }

        public static FixedValue RequestId(byte[] value)
        {
            return new FixedValue(value, ProtocolWireLayout.RequestIdSize, true);
        }

        public static FixedValue CreateRandomRequestId()
        {
            return CreateRandom(ProtocolWireLayout.RequestIdSize);
        }

        public static FixedValue Digest(byte[] value)
        {
            return new FixedValue(value, ProtocolWireLayout.DigestSize, true);
        }

        public static FixedValue Sha256(byte[] canonicalBytes)
        {
            if (canonicalBytes == null)
            {
                throw new ArgumentNullException("canonicalBytes");
            }

            using (SHA256 algorithm = SHA256.Create())
            {
                return Digest(algorithm.ComputeHash(canonicalBytes));
            }
        }

        public byte[] ToArray()
        {
            return (byte[])bytes.Clone();
        }

        public bool Equals(FixedValue other)
        {
            if (ReferenceEquals(other, null) || bytes.Length != other.bytes.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < bytes.Length; index++)
            {
                difference |= bytes[index] ^ other.bytes[index];
            }

            return difference == 0;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FixedValue);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < bytes.Length; index++)
                {
                    hash = (hash * 31) + bytes[index];
                }

                return hash;
            }
        }

        public string ToHex()
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        internal static FixedValue FromWire(byte[] buffer, int offset, int size, bool rejectZero)
        {
            byte[] value = new byte[size];
            Buffer.BlockCopy(buffer, offset, value, 0, size);
            return new FixedValue(value, size, rejectZero);
        }

        internal static bool IsAllZero(byte[] value)
        {
            int aggregate = 0;
            for (int index = 0; index < value.Length; index++)
            {
                aggregate |= value[index];
            }

            return aggregate == 0;
        }

        private static FixedValue CreateRandom(int size)
        {
            byte[] value = new byte[size];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                do
                {
                    generator.GetBytes(value);
                }
                while (IsAllZero(value));
            }

            return new FixedValue(value, size, true);
        }
    }
}
