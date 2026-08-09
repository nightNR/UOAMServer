namespace UOAMServer.Protocol;

public static class ResponseHeader
{
    /// Patches total length (offset 8) and sequence (offset 12) into a fully-built
    /// response buffer, and — only for DataResponse (0x02) packets, matching the
    /// original C server's respond() — the "stub length" (offset 16) = total-24.
    public static byte[] Finalize(byte[] buffer, uint sequence)
    {
        WriteUInt32At(buffer, 8, (uint)buffer.Length);
        WriteUInt32At(buffer, 12, sequence);

        if (buffer[2] == (byte)PacketType.DataResponse)
            WriteUInt32At(buffer, 16, (uint)(buffer.Length - 24));

        return buffer;
    }

    public static void WriteUInt32At(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
