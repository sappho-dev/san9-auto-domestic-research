using System;
using System.Linq;
using System.Text;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal sealed class DecodedFixedBig5
    {
        internal string RawHex;
        internal string Decoded;
        internal string Normalized;
        internal bool HadTerminator;
        internal bool DecodeSucceeded;
        internal bool PrefixStripped;
        internal string Error;
    }

    internal static class Big5NameDecoder
    {
        private static readonly Encoding StrictBig5 = Encoding.GetEncoding(
            950,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        internal static DecodedFixedBig5 Decode(byte[] source, int offset, int length, bool stripHexPrefix)
        {
            DecodedFixedBig5 result = new DecodedFixedBig5();
            byte[] fixedBytes = new byte[length];
            Buffer.BlockCopy(source, offset, fixedBytes, 0, length);
            result.RawHex = ToHex(fixedBytes);

            int usedLength = Array.IndexOf(fixedBytes, (byte)0);
            result.HadTerminator = usedLength >= 0;
            if (usedLength < 0)
            {
                usedLength = fixedBytes.Length;
            }

            byte[] usedBytes = fixedBytes.Take(usedLength).ToArray();
            try
            {
                result.Decoded = StrictBig5.GetString(usedBytes);
                result.DecodeSucceeded = true;
            }
            catch (DecoderFallbackException exception)
            {
                result.Decoded = string.Empty;
                result.Normalized = string.Empty;
                result.Error = exception.Message;
                return result;
            }

            result.Normalized = result.Decoded;
            if (stripHexPrefix && CanConservativelyStripPrefix(usedBytes))
            {
                byte[] remainder = usedBytes.Skip(2).ToArray();
                try
                {
                    string decodedRemainder = StrictBig5.GetString(remainder);
                    if (!string.IsNullOrEmpty(decodedRemainder)
                        && decodedRemainder.All(IsCjkNameCharacter))
                    {
                        result.Normalized = decodedRemainder;
                        result.PrefixStripped = true;
                    }
                }
                catch (DecoderFallbackException)
                {
                    // Preserve the original decoded value. Prefix removal is optional and conservative.
                }
            }

            return result;
        }

        private static bool CanConservativelyStripPrefix(byte[] value)
        {
            return value.Length >= 4
                && IsAsciiHex(value[0])
                && IsAsciiHex(value[1])
                && value.Skip(2).Any(item => item >= 0x80);
        }

        private static bool IsAsciiHex(byte value)
        {
            return (value >= (byte)'0' && value <= (byte)'9')
                || (value >= (byte)'a' && value <= (byte)'f')
                || (value >= (byte)'A' && value <= (byte)'F');
        }

        private static bool IsCjkNameCharacter(char value)
        {
            return (value >= '\u3400' && value <= '\u4dbf')
                || (value >= '\u4e00' && value <= '\u9fff')
                || (value >= '\uf900' && value <= '\ufaff');
        }

        private static string ToHex(byte[] value)
        {
            return BitConverter.ToString(value).Replace("-", string.Empty);
        }
    }
}
