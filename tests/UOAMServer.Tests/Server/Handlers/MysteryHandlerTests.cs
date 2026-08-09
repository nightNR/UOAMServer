using UOAMServer.Domain;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class MysteryHandlerTests
{
    [Fact]
    public void BuildResponse_SetsClientIdAndType()
    {
        var client = new Client(0xCAFEBABE);

        var response = MysteryHandler.BuildResponse(client, sequence: 12);

        Assert.Equal(0x0f, response[2]);
        Assert.Equal(56, response.Length); // matches uoamhub.c's packet_response2
        Assert.Equal(0xCAFEBABEu, ReadUInt32LE(response, 20));
        Assert.Equal(12u, ReadUInt32LE(response, 12));
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
