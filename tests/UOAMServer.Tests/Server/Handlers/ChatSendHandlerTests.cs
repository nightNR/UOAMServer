using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class ChatSendHandlerTests
{
    private static byte[] BuildChatSendPacket(string sender, string text, uint subtype = 1)
    {
        var writer = new PacketWriter();
        writer.WriteZeros(48); // common data prefix, contents not read by this handler
        writer.WriteZeros(4);
        writer.WriteUInt32LE(subtype);
        writer.WriteBytes(new byte[] { 0x00, 0x00, 0x02, 0x00 });
        writer.WriteNdrString(sender, pad: true);
        writer.WriteNdrString(text, pad: false, offsetField: NdrString.ChatTextOffsetMarker);
        return writer.ToArray();
    }

    [Fact]
    public void Handle_BroadcastsToOtherClientsInSameDomain_AndToSender()
    {
        // Real uoam_Orion clients don't echo their own sent message locally
        // — they only display it once it comes back through their own next
        // poll, so the sender needs it queued too.
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var peer = new Client(2);
        domain.TryAddClient(sender);
        domain.TryAddClient(peer);
        var packet = BuildChatSendPacket("Alice", "hello");

        ChatSendHandler.Handle(sender, packet, 0, packet.Length, sequence: 1);

        Assert.Single(sender.ChatQueue);
        Assert.Single(peer.ChatQueue);
    }

    [Fact]
    public void Handle_DoesNotBroadcastToOtherDomains()
    {
        var host = new Host(lockedPassword: null);
        var domainA = host.CreateDomain("a")!;
        var domainB = host.CreateDomain("b")!;
        var sender = new Client(1);
        var outsider = new Client(2);
        domainA.TryAddClient(sender);
        domainB.TryAddClient(outsider);
        var packet = BuildChatSendPacket("Alice", "hello");

        ChatSendHandler.Handle(sender, packet, 0, packet.Length, sequence: 1);

        Assert.Empty(outsider.ChatQueue);
    }

    [Fact]
    public void Handle_QueueAtMaxChats_DropsForThatRecipientOnly()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var fullPeer = new Client(2);
        domain.TryAddClient(sender);
        domain.TryAddClient(fullPeer);
        for (var i = 0; i < Limits.MaxChats; i++)
            fullPeer.ChatQueue.Enqueue(Array.Empty<byte>());
        var packet = BuildChatSendPacket("Alice", "hello");

        ChatSendHandler.Handle(sender, packet, 0, packet.Length, sequence: 1);

        Assert.Equal(Limits.MaxChats, fullPeer.ChatQueue.Count);
    }

    [Fact]
    public void Handle_ReturnsChatFamilyAckToSender()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        domain.TryAddClient(sender);
        var packet = BuildChatSendPacket("Alice", "hello");

        var response = ChatSendHandler.Handle(sender, packet, 0, packet.Length, sequence: 3);

        Assert.Equal(0x02, response[2]);
        Assert.Equal(28, response.Length);
        Assert.Equal(1u, ReadUInt32LE(response, 20)); // marker20 = 1 -> chat-family Ack
    }

    [Fact]
    public void Handle_QueuedPayloadStartsWithSubtypeAndConstant()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var peer = new Client(2);
        domain.TryAddClient(sender);
        domain.TryAddClient(peer);
        var packet = BuildChatSendPacket("Alice", "hi", subtype: 1);

        ChatSendHandler.Handle(sender, packet, 0, packet.Length, sequence: 1);

        var queued = peer.ChatQueue.Peek();
        Assert.Equal(1u, ReadUInt32LE(queued, 0));
        Assert.Equal(0x00020000u, ReadUInt32LE(queued, 4));
    }

    [Fact]
    public void Handle_Subtype2Registration_DoesNotBroadcast()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var peer = new Client(2);
        domain.TryAddClient(sender);
        domain.TryAddClient(peer);
        var packet = BuildChatSendPacket("Alice", "ignored", subtype: 2);

        var response = ChatSendHandler.Handle(sender, packet, 0, packet.Length, sequence: 1);

        Assert.Empty(peer.ChatQueue);
        Assert.Equal(28, response.Length); // still acks
        Assert.Equal(1u, ReadUInt32LE(response, 20)); // still chat-family Ack
    }

    [Fact]
    public void Handle_OversizedSharedBuffer_DoesNotRelayTrailingGarbage()
    {
        // `data` is the connection's shared, oversized receive buffer, reused
        // across reads and never cleared between packets — it can be much
        // larger than any one packet. The relayed body must be sized from
        // the packet's own declared length, not data.Length, or a recipient
        // gets stale bytes appended after the real payload (e.g. leftover
        // password bytes from an earlier packet in this same buffer).
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var sender = new Client(1);
        var peer = new Client(2);
        domain.TryAddClient(sender);
        domain.TryAddClient(peer);
        var realPacket = BuildChatSendPacket("Alice", "hi");
        var oversizedBuffer = new byte[realPacket.Length + 500];
        Array.Copy(realPacket, oversizedBuffer, realPacket.Length);
        for (var i = realPacket.Length; i < oversizedBuffer.Length; i++)
            oversizedBuffer[i] = 0xAA; // stand-in for stale bytes from a prior packet

        ChatSendHandler.Handle(sender, oversizedBuffer, 0, realPacket.Length, sequence: 1);

        var queued = peer.ChatQueue.Peek();
        Assert.Equal(realPacket.Length - ChatSendHandler.PayloadOffset, queued.Length);
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}

