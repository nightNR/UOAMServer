using UOAMServer.Protocol;
using Xunit;

namespace UOAMServer.Tests.Protocol;

public class NdrStringTests
{
    [Fact]
    public void Encode_SimpleName_HasCorrectHeaderAndNulTerminator()
    {
        var encoded = NdrString.Encode("Bob");

        // max_count = actual_count = 4 (3 chars + NUL)
        Assert.Equal(4u, ReadUInt32LE(encoded, 0));
        Assert.Equal(0u, ReadUInt32LE(encoded, 4)); // offset field, default 0
        Assert.Equal(4u, ReadUInt32LE(encoded, 8));
        Assert.Equal((byte)'B', encoded[12]);
        Assert.Equal((byte)'o', encoded[13]);
        Assert.Equal((byte)'b', encoded[14]);
        Assert.Equal((byte)0, encoded[15]); // NUL terminator
        Assert.Equal(16, encoded.Length); // 12-byte header + 4-byte string
    }

    [Fact]
    public void Encode_WithCustomOffsetField_UsesIt()
    {
        var encoded = NdrString.Encode("hi", NdrString.ChatTextOffsetMarker);

        Assert.Equal(NdrString.ChatTextOffsetMarker, ReadUInt32LE(encoded, 4));
    }

    [Fact]
    public void Decode_RoundTripsWithEncode()
    {
        var encoded = NdrString.Encode("Ahoj svet");

        var decoded = NdrString.Decode(encoded, 0, out var consumed);

        Assert.Equal("Ahoj svet", decoded);
        Assert.Equal(encoded.Length, consumed);
    }

    [Fact]
    public void Decode_AtNonZeroOffset_ReadsRelativeToOffset()
    {
        var prefix = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA };
        var encoded = NdrString.Encode("x");
        var buffer = new byte[prefix.Length + encoded.Length];
        prefix.CopyTo(buffer, 0);
        encoded.CopyTo(buffer, prefix.Length);

        var decoded = NdrString.Decode(buffer, prefix.Length, out var consumed);

        Assert.Equal("x", decoded);
        Assert.Equal(encoded.Length, consumed);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(16, 16)]
    [InlineData(17, 20)]
    public void PaddedLength_RoundsUpToFourByteBoundary(int raw, int expected)
    {
        Assert.Equal(expected, NdrString.PaddedLength(raw));
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
