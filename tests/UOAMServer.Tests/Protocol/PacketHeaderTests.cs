using UOAMServer.Protocol;
using Xunit;

namespace UOAMServer.Tests.Protocol;

public class PacketHeaderTests
{
    [Fact]
    public void TryParse_ValidHeader_ReturnsTrueAndFieldsMatch()
    {
        byte[] buffer =
        {
            0x05, 0x00, 0x0b, 0x03, 0x10, 0x00, 0x00, 0x00,
            0x20, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00,
        };

        var result = PacketHeader.TryParse(buffer, 0, buffer.Length, out var header);

        Assert.True(result);
        Assert.Equal(PacketType.Bind, header.Type);
        Assert.Equal(32u, header.Length);
        Assert.Equal(7u, header.Sequence);
    }

    [Fact]
    public void TryParse_TooFewBytesAvailable_ReturnsFalse()
    {
        byte[] buffer = { 0x05, 0x00, 0x0b, 0x03, 0x10, 0x00, 0x00, 0x00 };

        var result = PacketHeader.TryParse(buffer, 0, buffer.Length, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParse_WrongMagicBytes_ReturnsFalse()
    {
        byte[] buffer =
        {
            0x99, 0x00, 0x0b, 0x03, 0x10, 0x00, 0x00, 0x00,
            0x20, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00,
        };

        var result = PacketHeader.TryParse(buffer, 0, buffer.Length, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParse_DeclaredLengthBelowHeaderSize_ReturnsFalse()
    {
        byte[] buffer =
        {
            0x05, 0x00, 0x0b, 0x03, 0x10, 0x00, 0x00, 0x00,
            0x08, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00,
        };

        var result = PacketHeader.TryParse(buffer, 0, buffer.Length, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParse_NonZeroOffset_ParsesRelativeToOffset()
    {
        byte[] buffer =
        {
            0xAA, 0xAA, 0xAA, // junk prefix
            0x05, 0x00, 0x0b, 0x03, 0x10, 0x00, 0x00, 0x00,
            0x20, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00,
        };

        var result = PacketHeader.TryParse(buffer, 3, buffer.Length - 3, out var header);

        Assert.True(result);
        Assert.Equal(PacketType.Bind, header.Type);
    }

    [Fact]
    public void TryParse_DeclaredLengthAboveMaxPacketLength_ReturnsFalse()
    {
        // Robustness (design spec): a maliciously/accidentally huge declared
        // length must be rejected outright, not left to silently wedge a
        // fixed-size receive buffer forever waiting for bytes that can't come.
        byte[] buffer = new byte[16];
        buffer[0] = 0x05; buffer[1] = 0x00; buffer[2] = 0x0b; buffer[3] = 0x03; buffer[4] = 0x10;
        var tooLong = PacketLimits.MaxPacketLength + 1;
        buffer[8] = (byte)tooLong;
        buffer[9] = (byte)(tooLong >> 8);
        buffer[10] = (byte)(tooLong >> 16);
        buffer[11] = (byte)(tooLong >> 24);

        var result = PacketHeader.TryParse(buffer, 0, buffer.Length, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParse_DeclaredLengthExactlyAtMaxPacketLength_ReturnsTrue()
    {
        byte[] buffer = new byte[16];
        buffer[0] = 0x05; buffer[1] = 0x00; buffer[2] = 0x0b; buffer[3] = 0x03; buffer[4] = 0x10;
        var maxLength = PacketLimits.MaxPacketLength;
        buffer[8] = (byte)maxLength;
        buffer[9] = (byte)(maxLength >> 8);
        buffer[10] = (byte)(maxLength >> 16);
        buffer[11] = (byte)(maxLength >> 24);

        var result = PacketHeader.TryParse(buffer, 0, buffer.Length, out var header);

        Assert.True(result);
        Assert.Equal((uint)PacketLimits.MaxPacketLength, header.Length);
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
