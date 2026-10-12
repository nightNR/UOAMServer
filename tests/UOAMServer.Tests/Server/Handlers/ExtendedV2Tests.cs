using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class ExtendedV2Tests
{
    private static byte[] CapabilityPacket(ushort version)
    {
        var writer = new PacketWriter();
        writer.WriteZeros(48);
        writer.WriteBytes(new byte[] { 0x43, 0x55, 0x4F, 0x58 });
        writer.WriteUInt16LE(version);
        return writer.ToArray();
    }

    private static byte[] ExtendedPacket(uint serialId)
    {
        var writer = new PacketWriter();
        writer.WriteZeros(48);
        writer.WriteUInt32LE(serialId);
        writer.WriteZeros(32);
        return writer.ToArray();
    }

    [Fact]
    public void Capability_V1_NegotiatesV1AndAcks()
    {
        var client = new Client(1);

        var response = ExtendedCapabilityHandler.Handle(client, CapabilityPacket(1), 0, sequence: 1);

        Assert.Equal(1, client.ExtendedVersion);
        Assert.Equal(28, response!.Length);
    }

    [Fact]
    public void Capability_V2_NegotiatesV2AndRepliesWithMarker3AndVersion()
    {
        var client = new Client(1);

        var response = ExtendedCapabilityHandler.Handle(client, CapabilityPacket(2), 0, sequence: 5)!;

        Assert.Equal(2, client.ExtendedVersion);
        Assert.Equal(30, response.Length);
        Assert.Equal(0x02, response[2]);
        Assert.Equal(3, response[20]);
        Assert.Equal(new byte[] { 0x43, 0x55, 0x4F, 0x58 }, response[24..28]);
        Assert.Equal(2, response[28]);
        Assert.Equal(5, response[12]);
    }

    [Fact]
    public void Capability_HigherThanServerMax_IsClampedToMax()
    {
        var client = new Client(1);

        ExtendedCapabilityHandler.Handle(client, CapabilityPacket(999), 0, sequence: 1);

        Assert.Equal(ExtendedCapabilityHandler.MaxVersion, client.ExtendedVersion);
    }

    [Fact]
    public void V2Recipient_KeepsOnlyLatestPerSender_AndPollReturnsAllSendersInOneResponse()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var senderA = new Client(1);
        var senderB = new Client(2);
        var recipient = new Client(3) { SupportsExtended = true, ExtendedVersion = 2 };
        domain.TryAddClient(senderA);
        domain.TryAddClient(senderB);
        domain.TryAddClient(recipient);

        ExtendedPositionHandler.Handle(senderA, ExtendedPacket(0xA1), 0, 1);
        ExtendedPositionHandler.Handle(senderA, ExtendedPacket(0xA2), 0, 2); // replaces 0xA1
        ExtendedPositionHandler.Handle(senderB, ExtendedPacket(0xB1), 0, 3);

        var response = ExtendedPollHandler.Handle(recipient, sequence: 9);

        Assert.Equal(24 + 36 * 2, response.Length);
        Assert.Equal(0xA2, response[24]);
        Assert.Equal(0xB1, response[24 + 36]);
        Assert.Empty(recipient.ExtendedPending);
        Assert.Empty(recipient.ExtendedQueue);
        Assert.Equal(24, ExtendedPollHandler.Handle(recipient, sequence: 10).Length);
    }

    [Fact]
    public void V1Recipient_StillUsesLegacyQueue()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var recipient = new Client(2) { SupportsExtended = true, ExtendedVersion = 1 };
        domain.TryAddClient(sender);
        domain.TryAddClient(recipient);

        ExtendedPositionHandler.Handle(sender, ExtendedPacket(1), 0, 1);

        Assert.Single(recipient.ExtendedQueue);
        Assert.Empty(recipient.ExtendedPending);
    }
}
