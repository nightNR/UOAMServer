using System.Text;
using UOAMServer.Domain;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class PositionHandlerTests
{
    private static byte[] BuildPositionPacket(string name, byte[] reserved, byte[] position)
    {
        // 48-byte common data prefix (contents besides length/major/minor don't
        // matter to PositionHandler) + 92-byte name/reserved/position block.
        var buffer = new byte[48 + 92];
        var nameBytes = Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, 0, buffer, 48, nameBytes.Length);
        Array.Copy(reserved, 0, buffer, 48 + 64, 12);
        Array.Copy(position, 0, buffer, 48 + 64 + 12, 16);
        return buffer;
    }

    [Fact]
    public void Handle_UpdatesClientNameReservedAndPosition()
    {
        var client = new Client(1);
        var reserved = new byte[12];
        var position = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var packet = BuildPositionPacket("Bob", reserved, position);

        PositionHandler.Handle(client, packet, 0, sequence: 1);

        Assert.Equal("Bob", client.Name);
        Assert.Equal(reserved, client.ReservedBlock);
        Assert.Equal(position, client.PositionBlock);
        Assert.True(client.HavePosition);
    }

    [Fact]
    public void Handle_UpdatesLastPositionUpdateAt()
    {
        var client = new Client(1);
        var before = DateTime.UtcNow;
        var packet = BuildPositionPacket("Bob", new byte[12], new byte[16]);

        PositionHandler.Handle(client, packet, 0, sequence: 1);

        Assert.True(client.LastPositionUpdateAt >= before);
    }

    [Fact]
    public void Handle_ReturnsPositionFamilyAckResponse()
    {
        var client = new Client(1);
        var packet = BuildPositionPacket("Bob", new byte[12], new byte[16]);

        var response = PositionHandler.Handle(client, packet, 0, sequence: 5);

        Assert.Equal(0x02, response[2]);
        Assert.Equal(28, response.Length);
        Assert.Equal(0u, ReadUInt32LE(response, 20)); // marker20 = 0 -> position-family Ack
        Assert.Equal(5u, ReadUInt32LE(response, 12));
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
