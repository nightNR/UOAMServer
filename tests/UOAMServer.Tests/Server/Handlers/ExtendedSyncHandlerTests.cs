using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class ExtendedSyncHandlerTests
{
    private static byte[] SyncPacket(uint serial, byte positionMarker)
    {
        var writer = new PacketWriter();
        writer.WriteZeros(48);
        writer.WriteUInt32LE(serial);
        writer.WriteZeros(32);
        var position = new byte[16];
        position[0] = positionMarker;
        writer.WriteBytes(position);
        return writer.ToArray();
    }

    private static (UOAMServer.Domain.Domain domain, Client a, Client b) Pair()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var a = new Client(1) { Name = "Alice", SupportsExtended = true, ExtendedVersion = 3 };
        var b = new Client(2) { Name = "Bob", SupportsExtended = true, ExtendedVersion = 3 };
        domain.TryAddClient(a);
        domain.TryAddClient(b);
        return (domain, a, b);
    }

    [Fact]
    public void Handle_StoresSenderStateAndPosition()
    {
        var (_, a, _) = Pair();
        var packet = SyncPacket(0xA1, positionMarker: 7);

        ExtendedSyncHandler.Handle(a, packet, 0, packet.Length, 1);

        Assert.Equal(0xA1u, a.SerialId);
        Assert.Equal(7, a.PositionBlock[0]);
        Assert.True(a.HavePosition);
        Assert.NotNull(a.ExtendedBody);
    }

    [Fact]
    public void Handle_ReturnsOtherFreshPlayersWithExtendedBodies_ExcludingSelf()
    {
        var (_, a, b) = Pair();
        var bp = SyncPacket(0xB1, positionMarker: 9);
        ExtendedSyncHandler.Handle(b, bp, 0, bp.Length, 1);
        var ap = SyncPacket(0xA1, positionMarker: 7);

        var response = ExtendedSyncHandler.Handle(a, ap, 0, ap.Length, 5)!;

        Assert.Equal(0x02, response[2]);
        Assert.Equal(4, response[20]); // marker20 = sync
        Assert.Equal(5, response[12]);
        Assert.Equal(1, response[24]); // count
        Assert.Equal(3, response[28]); // "Bob"
        Assert.Equal((byte)'B', response[29]);
        Assert.Equal(9, response[32]); // position block starts after name
        Assert.Equal(1, response[48]); // hasExtended
        Assert.Equal(0xB1, response[49]); // body
        Assert.Equal(24 + 4 + 1 + 3 + 16 + 1 + 36, response.Length);
    }

    [Fact]
    public void Handle_StockPeer_IsListedWithoutExtendedBody()
    {
        var (domain, a, _) = Pair();
        var stock = new Client(3) { Name = "Stocky", PositionBlock = new byte[16] };
        domain.TryAddClient(stock);
        stock.TouchPosition();
        var ap = SyncPacket(0xA1, 1);

        var response = ExtendedSyncHandler.Handle(a, ap, 0, ap.Length, 1)!;

        Assert.Equal(1, response[24]);
        Assert.Equal(6, response[28]);
        Assert.Equal(0, response[28 + 1 + 6 + 16]); // hasExtended = 0
        Assert.Equal(24 + 4 + 1 + 6 + 16 + 1, response.Length);
    }

    [Fact]
    public void Handle_StalePeer_IsOmitted()
    {
        var (_, a, b) = Pair();
        b.LastPositionUpdateAt = DateTime.UtcNow - TimeSpan.FromSeconds(30);
        var ap = SyncPacket(0xA1, 1);

        var response = ExtendedSyncHandler.Handle(a, ap, 0, ap.Length, 1)!;

        Assert.Equal(0, response[24]);
        Assert.Equal(28, response.Length);
    }

    [Fact]
    public void Handle_NotNegotiatedV3_OrTruncated_ReturnsNull()
    {
        var (_, a, _) = Pair();
        var packet = SyncPacket(1, 1);

        a.ExtendedVersion = 2;
        Assert.Null(ExtendedSyncHandler.Handle(a, packet, 0, packet.Length, 1));

        a.ExtendedVersion = 3;
        Assert.Null(ExtendedSyncHandler.Handle(a, packet, 0, packet.Length - 1, 1));
    }

    [Fact]
    public void SyncSender_RelaysToV2PollRecipient_ButNotToV3()
    {
        var (domain, a, b) = Pair(); // b is v3
        var v2 = new Client(3) { Name = "Old", SupportsExtended = true, ExtendedVersion = 2 };
        domain.TryAddClient(v2);
        var packet = SyncPacket(0xA1, 1);

        ExtendedSyncHandler.Handle(a, packet, 0, packet.Length, 1);

        Assert.Single(v2.ExtendedPending);
        Assert.Empty(b.ExtendedPending);
        Assert.Empty(b.ExtendedQueue);
    }
}
