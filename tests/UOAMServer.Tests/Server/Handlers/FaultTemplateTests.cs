using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class FaultTemplateTests
{
    [Fact]
    public void Build_HasFaultTypeAndCorrectLength()
    {
        var response = FaultTemplate.Build(sequence: 3);

        Assert.Equal(0x03, response[2]);
        Assert.Equal(32, response.Length);
        Assert.Equal(3u, ReadUInt32LE(response, 12));
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
