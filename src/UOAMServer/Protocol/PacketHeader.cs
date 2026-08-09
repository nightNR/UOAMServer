namespace UOAMServer.Protocol;

public readonly struct PacketHeader
{
    public const int Size = 16;

    public PacketType Type { get; }
    public uint Length { get; }
    public uint Sequence { get; }

    public PacketHeader(PacketType type, uint length, uint sequence)
    {
        Type = type;
        Length = length;
        Sequence = sequence;
    }

    public static bool TryParse(byte[] buffer, int offset, int available, out PacketHeader header)
    {
        header = default;

        if (available < Size)
            return false;

        if (buffer[offset] != 0x05 || buffer[offset + 1] != 0x00 ||
            buffer[offset + 3] != 0x03 || buffer[offset + 4] != 0x10)
            return false;

        var type = (PacketType)buffer[offset + 2];
        var length = ReadUInt32LE(buffer, offset + 8);
        var sequence = ReadUInt32LE(buffer, offset + 12);

        if (length < Size || length > PacketLimits.MaxPacketLength)
            return false;

        header = new PacketHeader(type, length, sequence);
        return true;
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
