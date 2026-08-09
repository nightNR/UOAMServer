namespace UOAMServer.Protocol;

public sealed class PacketReader
{
    private readonly byte[] _buffer;
    private int _position;

    public PacketReader(byte[] buffer, int offset = 0)
    {
        _buffer = buffer;
        _position = offset;
    }

    public int Position => _position;

    public byte ReadByte() => _buffer[_position++];

    public ushort ReadUInt16LE()
    {
        ushort value = (ushort)(_buffer[_position] | _buffer[_position + 1] << 8);
        _position += 2;
        return value;
    }

    public uint ReadUInt32LE()
    {
        uint value = (uint)(_buffer[_position] | _buffer[_position + 1] << 8 |
                             _buffer[_position + 2] << 16 | _buffer[_position + 3] << 24);
        _position += 4;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        var result = new byte[count];
        Array.Copy(_buffer, _position, result, 0, count);
        _position += count;
        return result;
    }

    public void Skip(int count) => _position += count;
}
