using UOAMServer.Protocol;
using Xunit;

namespace UOAMServer.Tests.Protocol;

public class ResponseHeaderTests
{
    [Fact]
    public void Finalize_DataResponseType_PatchesLengthSequenceAndStubLength()
    {
        // type 0x02 (DataResponse) at offset 2, 28-byte buffer
        var buffer = new byte[28];
        buffer[2] = (byte)PacketType.DataResponse;

        var result = ResponseHeader.Finalize(buffer, sequence: 99);

        Assert.Equal(28u, ReadUInt32LE(result, 8));
        Assert.Equal(99u, ReadUInt32LE(result, 12));
        Assert.Equal(4u, ReadUInt32LE(result, 16)); // 28 - 24
    }

    [Fact]
    public void Finalize_NonDataResponseType_DoesNotTouchOffset16()
    {
        var buffer = new byte[20];
        buffer[2] = (byte)PacketType.BindAck;
        buffer[16] = 0xEE; // sentinel, must survive untouched

        var result = ResponseHeader.Finalize(buffer, sequence: 5);

        Assert.Equal(20u, ReadUInt32LE(result, 8));
        Assert.Equal(5u, ReadUInt32LE(result, 12));
        Assert.Equal((byte)0xEE, result[16]);
    }

    [Fact]
    public void WriteUInt32At_WritesLittleEndianAtOffset()
    {
        var buffer = new byte[8];

        ResponseHeader.WriteUInt32At(buffer, 4, 0x12345678u);

        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, buffer[4..8]);
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
