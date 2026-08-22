using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace San9AutoDomestic.CommandBroker
{
    public sealed class ProcessSessionIdentity : IEquatable<ProcessSessionIdentity>
    {
        private readonly byte[] executableSha256;

        public ProcessSessionIdentity(int processId, long creationFileTimeUtc, string executableSha256)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException("processId", "Process id must be positive.");
            }

            if (creationFileTimeUtc <= 0)
            {
                throw new ArgumentOutOfRangeException("creationFileTimeUtc", "Creation FILETIME must be positive.");
            }

            ProcessId = processId;
            CreationFileTimeUtc = creationFileTimeUtc;
            this.executableSha256 = BinaryValue.ParseSha256(executableSha256, "executableSha256");
        }

        internal ProcessSessionIdentity(int processId, long creationFileTimeUtc, byte[] executableSha256)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }

            if (creationFileTimeUtc <= 0)
            {
                throw new ArgumentOutOfRangeException("creationFileTimeUtc");
            }

            BinaryValue.RequireLength(executableSha256, BinaryValue.Sha256Length, "executableSha256");
            ProcessId = processId;
            CreationFileTimeUtc = creationFileTimeUtc;
            this.executableSha256 = BinaryValue.Clone(executableSha256);
        }

        public int ProcessId { get; private set; }

        public long CreationFileTimeUtc { get; private set; }

        public string ExecutableSha256
        {
            get { return BinaryValue.ToHex(executableSha256); }
        }

        internal byte[] GetExecutableSha256Bytes()
        {
            return BinaryValue.Clone(executableSha256);
        }

        internal byte[] GetCanonicalBytes()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(ProcessId);
                writer.Write(CreationFileTimeUtc);
                writer.Write(executableSha256);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public bool Equals(ProcessSessionIdentity other)
        {
            return other != null
                && ProcessId == other.ProcessId
                && CreationFileTimeUtc == other.CreationFileTimeUtc
                && BinaryValue.AreEqual(executableSha256, other.executableSha256);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ProcessSessionIdentity);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ProcessId;
                hash = (hash * 397) ^ CreationFileTimeUtc.GetHashCode();
                for (int index = 0; index < 4; index++)
                {
                    hash = (hash * 397) ^ executableSha256[index];
                }

                return hash;
            }
        }
    }

    internal static class BinaryValue
    {
        internal const int Sha256Length = 32;

        internal static byte[] ParseSha256(string value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (value.Length != Sha256Length * 2)
            {
                throw new ArgumentException("SHA-256 values must contain exactly 64 hexadecimal characters.", parameterName);
            }

            byte[] result = new byte[Sha256Length];
            for (int index = 0; index < result.Length; index++)
            {
                int high = HexNibble(value[index * 2]);
                int low = HexNibble(value[(index * 2) + 1]);
                if (high < 0 || low < 0)
                {
                    throw new ArgumentException("SHA-256 values must contain hexadecimal characters only.", parameterName);
                }

                result[index] = (byte)((high << 4) | low);
            }

            return result;
        }

        internal static string ToHex(byte[] value)
        {
            char[] characters = new char[value.Length * 2];
            const string Alphabet = "0123456789ABCDEF";
            for (int index = 0; index < value.Length; index++)
            {
                characters[index * 2] = Alphabet[value[index] >> 4];
                characters[(index * 2) + 1] = Alphabet[value[index] & 15];
            }

            return new string(characters);
        }

        internal static byte[] Clone(byte[] value)
        {
            byte[] clone = new byte[value.Length];
            Buffer.BlockCopy(value, 0, clone, 0, value.Length);
            return clone;
        }

        internal static void RequireLength(byte[] value, int length, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (value.Length != length)
            {
                throw new ArgumentException("Unexpected binary value length.", parameterName);
            }
        }

        internal static bool AreEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        internal static byte[] ComputeSha256(byte[] value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return algorithm.ComputeHash(value);
            }
        }

        internal static byte[] Utf8(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        private static int HexNibble(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            return -1;
        }
    }
}
