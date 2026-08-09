using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class AckTemplateTests
{
    [Fact]
    public void Build_PositionFamily_Marker20IsZero()
    {
        var response = AckTemplate.Build(sequence: 5);

        Assert.Equal(0x02, response[2]);
        Assert.Equal(28, response.Length);
        Assert.Equal(0u, ReadUInt32LE(response, 20));
        Assert.Equal(5u, ReadUInt32LE(response, 12));
    }

    [Fact]
    public void BuildChatFamily_Marker20IsOne()
    {
        var response = AckTemplate.BuildChatFamily(sequence: 9);

        Assert.Equal(0x02, response[2]);
        Assert.Equal(28, response.Length);
        Assert.Equal(1u, ReadUInt32LE(response, 20));
        Assert.Equal(9u, ReadUInt32LE(response, 12));
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
