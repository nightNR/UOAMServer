using UOAMServer.Protocol;
using Xunit;

namespace UOAMServer.Tests.Protocol;

public class PacketReaderTests
{
    [Fact]
    public void ReadByte_AdvancesPositionByOne()
    {
        var reader = new PacketReader(new byte[] { 0x05, 0x06 });

        var value = reader.ReadByte();

        Assert.Equal((byte)0x05, value);
        Assert.Equal(1, reader.Position);
    }

    [Fact]
    public void ReadUInt16LE_ReadsLittleEndian()
    {
        var reader = new PacketReader(new byte[] { 0x34, 0x12 });

        Assert.Equal((ushort)0x1234, reader.ReadUInt16LE());
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void ReadUInt32LE_ReadsLittleEndian()
    {
        var reader = new PacketReader(new byte[] { 0x78, 0x56, 0x34, 0x12 });

        Assert.Equal(0x12345678u, reader.ReadUInt32LE());
        Assert.Equal(4, reader.Position);
    }

    [Fact]
    public void ReadBytes_ReturnsCopyAndAdvances()
    {
        var reader = new PacketReader(new byte[] { 1, 2, 3, 4, 5 });

        var bytes = reader.ReadBytes(3);

        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.Equal(3, reader.Position);
    }

    [Fact]
    public void StartingAtNonZeroOffset_PositionStartsThere()
    {
        var reader = new PacketReader(new byte[] { 0, 0, 0, 42 }, offset: 3);

        Assert.Equal(3, reader.Position);
        Assert.Equal((byte)42, reader.ReadByte());
    }

    [Fact]
    public void Skip_AdvancesPositionWithoutReading()
    {
        var reader = new PacketReader(new byte[] { 1, 2, 3, 4 });

        reader.Skip(2);

        Assert.Equal(2, reader.Position);
        Assert.Equal((byte)3, reader.ReadByte());
    }
}
