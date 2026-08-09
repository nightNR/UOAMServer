namespace UOAMServer.Protocol;

public sealed class PacketWriter
{
    private readonly List<byte> _buffer = new();

    public int Length => _buffer.Count;

    public void WriteByte(byte value) => _buffer.Add(value);

    public void WriteBytes(byte[] bytes) => _buffer.AddRange(bytes);

    public void WriteUInt16LE(ushort value)
    {
        _buffer.Add((byte)value);
        _buffer.Add((byte)(value >> 8));
    }

    public void WriteUInt32LE(uint value)
    {
        _buffer.Add((byte)value);
        _buffer.Add((byte)(value >> 8));
        _buffer.Add((byte)(value >> 16));
        _buffer.Add((byte)(value >> 24));
    }

    public void WriteZeros(int count)
    {
        for (var i = 0; i < count; i++)
            _buffer.Add(0);
    }

    public void WriteNdrString(string value, bool pad, uint offsetField = 0)
    {
        var encoded = NdrString.Encode(value, offsetField);
        WriteBytes(encoded);

        if (pad)
        {
            var paddedLength = NdrString.PaddedLength(encoded.Length);
            WriteZeros(paddedLength - encoded.Length);
        }
    }

    public byte[] ToArray() => _buffer.ToArray();
}
