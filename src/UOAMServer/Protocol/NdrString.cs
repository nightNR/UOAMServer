using System.Text;

namespace UOAMServer.Protocol;

public static class NdrString
{
    public const uint ChatTextOffsetMarker = 0x00020004;

    public static byte[] Encode(string value, uint offsetField = 0)
    {
        var count = (uint)(value.Length + 1); // + NUL terminator
        var bytes = new byte[12 + count];

        WriteUInt32LE(bytes, 0, count);
        WriteUInt32LE(bytes, 4, offsetField);
        WriteUInt32LE(bytes, 8, count);

        var ascii = Encoding.ASCII.GetBytes(value);
        Array.Copy(ascii, 0, bytes, 12, ascii.Length);
        // remaining byte(s) up to and including index (12 + count - 1) stay zero (NUL terminator)

        return bytes;
    }

    public static string Decode(byte[] buffer, int offset, out int bytesConsumed)
    {
        var actualCount = (int)ReadUInt32LE(buffer, offset + 8);
        var stringStart = offset + 12;

        var length = 0;
        while (length < actualCount && buffer[stringStart + length] != 0)
            length++;

        bytesConsumed = 12 + actualCount;
        return Encoding.ASCII.GetString(buffer, stringStart, length);
    }

    public static int PaddedLength(int rawLength) => (rawLength + 3) / 4 * 4;

    private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
