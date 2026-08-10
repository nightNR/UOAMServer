using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class ExtendedPositionHandlerTests
{
    private static byte[] BuildExtendedPositionPacket(
        uint serialId, ushort hp, ushort maxHp, ushort mana, ushort maxMana,
        ushort stamina, ushort maxStamina, byte facet, byte notoriety,
        uint guildId, ushort flags, short velocityX, short velocityY, ulong timestamp)
    {
        var writer = new PacketWriter();
        writer.WriteZeros(48); // common data prefix, contents not read by this handler
        writer.WriteUInt32LE(serialId);
        writer.WriteUInt16LE(hp);
        writer.WriteUInt16LE(maxHp);
        writer.WriteUInt16LE(mana);
        writer.WriteUInt16LE(maxMana);
        writer.WriteUInt16LE(stamina);
        writer.WriteUInt16LE(maxStamina);
        writer.WriteByte(facet);
        writer.WriteByte(notoriety);
        writer.WriteUInt32LE(guildId);
        writer.WriteUInt16LE(flags);
        writer.WriteInt16LE(velocityX);
        writer.WriteInt16LE(velocityY);
        writer.WriteUInt64LE(timestamp);
        return writer.ToArray();
    }

    [Fact]
    public void Handle_UpdatesAllExtendedFieldsOnSender()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        domain.TryAddClient(sender);
        var packet = BuildExtendedPositionPacket(
            serialId: 0x40001234, hp: 90, maxHp: 100, mana: 40, maxMana: 50,
            stamina: 80, maxStamina: 80, facet: 1, notoriety: 3,
            guildId: 555, flags: 0b101, velocityX: -50, velocityY: 25, timestamp: 1234567890UL);

        ExtendedPositionHandler.Handle(sender, packet, 0, sequence: 1);

        Assert.Equal(0x40001234u, sender.SerialId);
        Assert.Equal((ushort)90, sender.Hp);
        Assert.Equal((ushort)100, sender.MaxHp);
        Assert.Equal((ushort)40, sender.Mana);
        Assert.Equal((ushort)50, sender.MaxMana);
        Assert.Equal((ushort)80, sender.Stamina);
        Assert.Equal((ushort)80, sender.MaxStamina);
        Assert.Equal((byte)1, sender.Facet);
        Assert.Equal((byte)3, sender.Notoriety);
        Assert.Equal(555u, sender.GuildId);
        Assert.Equal((ushort)0b101, sender.ExtendedFlags);
        Assert.Equal((short)-50, sender.VelocityX);
        Assert.Equal((short)25, sender.VelocityY);
        Assert.Equal(1234567890UL, sender.ExtendedTimestamp);
    }

    [Fact]
    public void Handle_TouchesPositionStalenessTimestamp()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        domain.TryAddClient(sender);
        var packet = BuildExtendedPositionPacket(1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0);

        ExtendedPositionHandler.Handle(sender, packet, 0, sequence: 1);

        Assert.True(sender.LastPositionUpdateAt > DateTime.MinValue);
    }

    [Fact]
    public void Handle_RelaysOnlyToOtherExtendedCapableClientsInSameDomain()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var extendedPeer = new Client(2) { SupportsExtended = true };
        var stockPeer = new Client(3); // SupportsExtended stays false
        domain.TryAddClient(sender);
        domain.TryAddClient(extendedPeer);
        domain.TryAddClient(stockPeer);
        var packet = BuildExtendedPositionPacket(1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0);

        ExtendedPositionHandler.Handle(sender, packet, 0, sequence: 1);

        Assert.Single(extendedPeer.ExtendedQueue);
        Assert.Empty(stockPeer.ExtendedQueue);
        Assert.Empty(sender.ExtendedQueue); // sender doesn't receive its own update
    }

    [Fact]
    public void Handle_DoesNotRelayToOtherDomains()
    {
        var host = new Host(lockedPassword: null);
        var domainA = host.CreateDomain("a")!;
        var domainB = host.CreateDomain("b")!;
        var sender = new Client(1);
        var outsider = new Client(2) { SupportsExtended = true };
        domainA.TryAddClient(sender);
        domainB.TryAddClient(outsider);
        var packet = BuildExtendedPositionPacket(1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0);

        ExtendedPositionHandler.Handle(sender, packet, 0, sequence: 1);

        Assert.Empty(outsider.ExtendedQueue);
    }

    [Fact]
    public void Handle_QueueAtMaxExtended_DropsForThatRecipientOnly()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var fullPeer = new Client(2) { SupportsExtended = true };
        domain.TryAddClient(sender);
        domain.TryAddClient(fullPeer);
        for (var i = 0; i < Limits.MaxExtended; i++)
            fullPeer.ExtendedQueue.Enqueue(Array.Empty<byte>());
        var packet = BuildExtendedPositionPacket(1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0);

        ExtendedPositionHandler.Handle(sender, packet, 0, sequence: 1);

        Assert.Equal(Limits.MaxExtended, fullPeer.ExtendedQueue.Count);
    }

    [Fact]
    public void Handle_RelayedBodyIs36BytesMatchingWireOrder()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var peer = new Client(2) { SupportsExtended = true };
        domain.TryAddClient(sender);
        domain.TryAddClient(peer);
        var packet = BuildExtendedPositionPacket(
            serialId: 0x11223344, hp: 1, maxHp: 2, mana: 3, maxMana: 4,
            stamina: 5, maxStamina: 6, facet: 7, notoriety: 8,
            guildId: 9, flags: 10, velocityX: 11, velocityY: 12, timestamp: 13);

        ExtendedPositionHandler.Handle(sender, packet, 0, sequence: 1);

        var body = peer.ExtendedQueue.Peek();
        Assert.Equal(36, body.Length);
        var reader = new PacketReader(body);
        Assert.Equal(0x11223344u, reader.ReadUInt32LE());
    }

    [Fact]
    public void Handle_ReturnsAck()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        domain.TryAddClient(sender);
        var packet = BuildExtendedPositionPacket(1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0);

        var response = ExtendedPositionHandler.Handle(sender, packet, 0, sequence: 42);

        Assert.Equal(0x02, response[2]);
        Assert.Equal(28, response.Length);
    }
}
