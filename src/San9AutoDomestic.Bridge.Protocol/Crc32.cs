namespace San9AutoDomestic.Bridge.Protocol
{
    internal static class Crc32
    {
        private const uint Polynomial = 0xedb88320U;

        internal static uint ComputeWithZeroedChecksum(byte[] bytes)
        {
            uint crc = 0xffffffffU;
            for (int index = 0; index < bytes.Length; index++)
            {
                byte value = index >= ProtocolWireLayout.ChecksumOffset
                    && index < ProtocolWireLayout.ChecksumOffset + 4
                    ? (byte)0
                    : bytes[index];
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    uint mask = unchecked((uint)-(int)(crc & 1U));
                    crc = (crc >> 1) ^ (Polynomial & mask);
                }
            }

            return ~crc;
        }
    }

    internal static class LittleEndian
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
            uint low = ReadUInt32(bytes, offset);
            uint high = ReadUInt32(bytes, offset + 4);
            return low | ((ulong)high << 32);
        }

        internal static long ReadInt64(byte[] bytes, int offset)
        {
            return unchecked((long)ReadUInt64(bytes, offset));
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
            WriteUInt32(bytes, offset, (uint)value);
            WriteUInt32(bytes, offset + 4, (uint)(value >> 32));
        }

        internal static void WriteInt64(byte[] bytes, int offset, long value)
        {
            WriteUInt64(bytes, offset, unchecked((ulong)value));
        }
    }
}
