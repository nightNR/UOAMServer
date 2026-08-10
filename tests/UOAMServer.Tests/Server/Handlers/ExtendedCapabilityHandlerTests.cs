using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class ExtendedCapabilityHandlerTests
{
    private static byte[] BuildCapabilityPacket(byte[] magic, ushort version)
    {
        var writer = new PacketWriter();
        writer.WriteZeros(48); // common data prefix, contents not read by this handler
        writer.WriteBytes(magic);
        writer.WriteUInt16LE(version);
        return writer.ToArray();
    }

    [Fact]
    public void Handle_ValidMagic_SetsSupportsExtendedAndAcks()
    {
        var client = new Client(1);
        var packet = BuildCapabilityPacket(new byte[] { 0x43, 0x55, 0x4F, 0x58 }, version: 1);

        var response = ExtendedCapabilityHandler.Handle(client, packet, 0, sequence: 7);

        Assert.True(client.SupportsExtended);
        Assert.NotNull(response);
        Assert.Equal(0x02, response![2]);
        Assert.Equal(28, response.Length);
        Assert.Equal(7u, ReadUInt32LE(response, 12));
    }

    [Fact]
    public void Handle_WrongMagic_ReturnsNullAndDoesNotSetFlag()
    {
        var client = new Client(1);
        var packet = BuildCapabilityPacket(new byte[] { 0x00, 0x00, 0x00, 0x00 }, version: 1);

        var response = ExtendedCapabilityHandler.Handle(client, packet, 0, sequence: 1);

        Assert.False(client.SupportsExtended);
        Assert.Null(response);
    }

    [Fact]
    public void Handle_UnexpectedVersion_StillAccepted()
    {
        var client = new Client(1);
        var packet = BuildCapabilityPacket(new byte[] { 0x43, 0x55, 0x4F, 0x58 }, version: 999);

        var response = ExtendedCapabilityHandler.Handle(client, packet, 0, sequence: 1);

        Assert.True(client.SupportsExtended);
        Assert.NotNull(response);
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
