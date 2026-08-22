using System;

namespace San9AutoDomestic.V8Transaction
{
    /// <summary>
    /// Immutable, non-zero 32-byte binding value.  A digest is evidence of
    /// identity only; possessing one never grants execution authority.
    /// </summary>
    public sealed class FixedDigest : IEquatable<FixedDigest>
    {
        public const int ByteCount = 32;
        private readonly byte[] _bytes;

        private FixedDigest(byte[] bytes)
        {
            _bytes = bytes;
        }

        public static FixedDigest Create(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }

            if (bytes.Length != ByteCount)
            {
                throw new ArgumentException("A V8 binding digest must contain exactly 32 bytes.", "bytes");
            }

            bool anyNonZero = false;
            byte[] copy = new byte[bytes.Length];
            for (int index = 0; index < bytes.Length; index++)
            {
                copy[index] = bytes[index];
                anyNonZero |= bytes[index] != 0;
            }

            if (!anyNonZero)
            {
                throw new ArgumentException("An all-zero digest is an absent binding, not a valid digest.", "bytes");
            }

            return new FixedDigest(copy);
        }

        public byte[] ToArray()
        {
            return (byte[])_bytes.Clone();
        }

        public bool Equals(FixedDigest other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < _bytes.Length; index++)
            {
                difference |= _bytes[index] ^ other._bytes[index];
            }

            return difference == 0;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FixedDigest);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < _bytes.Length; index += 4)
                {
                    hash = (hash * 31) ^ _bytes[index];
                }

                return hash;
            }
        }

        public override string ToString()
        {
            char[] chars = new char[_bytes.Length * 2];
            const string Hex = "0123456789ABCDEF";
            for (int index = 0; index < _bytes.Length; index++)
            {
                chars[index * 2] = Hex[_bytes[index] >> 4];
                chars[(index * 2) + 1] = Hex[_bytes[index] & 0x0f];
            }

            return new string(chars);
        }
    }
}

