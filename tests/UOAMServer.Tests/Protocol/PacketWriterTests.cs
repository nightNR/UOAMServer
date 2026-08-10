using UOAMServer.Protocol;
using Xunit;

namespace UOAMServer.Tests.Protocol;

public class PacketWriterTests
{
    [Fact]
    public void WriteUInt32LE_WritesLittleEndian()
    {
        var writer = new PacketWriter();

        writer.WriteUInt32LE(0x12345678u);

        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, writer.ToArray());
    }

    [Fact]
    public void WriteUInt16LE_WritesLittleEndian()
    {
        var writer = new PacketWriter();

        writer.WriteUInt16LE(0x1234);

        Assert.Equal(new byte[] { 0x34, 0x12 }, writer.ToArray());
    }

    [Fact]
    public void WriteZeros_WritesGivenCountOfZeroBytes()
    {
        var writer = new PacketWriter();

        writer.WriteZeros(3);

        Assert.Equal(new byte[] { 0, 0, 0 }, writer.ToArray());
        Assert.Equal(3, writer.Length);
    }

    [Fact]
    public void WriteNdrString_Unpadded_MatchesRawEncodeLength()
    {
        var writer = new PacketWriter();

        writer.WriteNdrString("Bob", pad: false);

        Assert.Equal(16, writer.Length); // 12-byte header + 4-byte "Bob\0"
    }

    [Fact]
    public void WriteNdrString_Padded_RoundsUpToFourByteBoundary()
    {
        var writer = new PacketWriter();

        writer.WriteNdrString("Bobby", pad: true); // 6-byte string -> 18 raw -> 20 padded

        Assert.Equal(20, writer.Length);
    }

    [Fact]
    public void WriteBytes_AppendsRawBytes()
    {
        var writer = new PacketWriter();

        writer.WriteBytes(new byte[] { 0xAA, 0xBB });

        Assert.Equal(new byte[] { 0xAA, 0xBB }, writer.ToArray());
    }

    [Fact]
    public void WriteInt16LE_WritesLittleEndianSignedValue()
    {
        var writer = new PacketWriter();

        writer.WriteInt16LE(-1);

        Assert.Equal(new byte[] { 0xFF, 0xFF }, writer.ToArray());
    }

    [Fact]
    public void WriteUInt64LE_WritesLittleEndian()
    {
        var writer = new PacketWriter();

        writer.WriteUInt64LE(0x0102030405060708UL);

        Assert.Equal(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }, writer.ToArray());
    }
}
