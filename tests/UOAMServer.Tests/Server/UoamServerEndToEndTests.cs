using System.Net;
using System.Net.Sockets;
using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server;
using Xunit;

namespace UOAMServer.Tests.Server;

public class UoamServerEndToEndTests
{
    private static readonly byte[] InterfaceGuid =
    {
        0x10, 0x66, 0x15, 0x9e, 0x5c, 0x7b, 0xd2, 0x11,
        0xb8, 0xcf, 0x00, 0x80, 0xc7, 0x97, 0x1b, 0xe1,
    };

    private static byte[] BuildHandshakeRequest(uint sequence)
    {
        var body = new byte[40];
        body[0] = 0x05; body[1] = 0x00; body[2] = 0x0b; body[3] = 0x03; body[4] = 0x10;
        WriteUInt32LE(body, 8, (uint)body.Length);
        WriteUInt32LE(body, 12, sequence);
        InterfaceGuid.CopyTo(body, 20);
        return body;
    }

    private static async Task<(TcpListener listener, int port)> StartTestServerAsync(Host host, CancellationTokenSource cts)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // release the port for UoamServer's own socket to bind

        var server = new UoamServer(host, port);
        _ = server.RunAsync(cts.Token);
        await Task.Delay(200); // give the accept loop time to start listening

        return (listener, port);
    }

    [Fact]
    public async Task Handshake_MalformedRequest_ReceivesBindNak()
    {
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        var badRequest = new byte[] { 0x05, 0x00, 0x0b, 0x03, 0x10, 0x00, 0x00, 0x00, 0x10, 0, 0, 0, 1, 0, 0, 0 };
        await stream.WriteAsync(badRequest);

        var responseBuffer = new byte[16];
        var read = await ReadExactAsync(stream, responseBuffer, cts);

        Assert.Equal(16, read);
        Assert.Equal(0x0d, responseBuffer[2]);

        cts.Cancel();
    }

    [Fact]
    public async Task Handshake_ThenMystery_ThenPositionThenQueryList_RoundTripsSentPosition()
    {
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();

        // 1. Handshake
        await stream.WriteAsync(BuildHandshakeRequest(sequence: 1));
        var ackBuffer = new byte[60];
        await ReadExactAsync(stream, ackBuffer, cts);
        Assert.Equal(0x0c, ackBuffer[2]);
        var clientId = ReadUInt32LE(ackBuffer, 20);

        // 2. Mystery packet
        var mystery = new byte[72];
        mystery[0] = 0x05; mystery[1] = 0x00; mystery[2] = 0x0e; mystery[3] = 0x03; mystery[4] = 0x10;
        WriteUInt32LE(mystery, 8, (uint)mystery.Length);
        WriteUInt32LE(mystery, 12, 2);
        WriteUInt32LE(mystery, 16, clientId);
        await stream.WriteAsync(mystery);
        var mysteryAckBuffer = new byte[56];
        await ReadExactAsync(stream, mysteryAckBuffer, cts);
        Assert.Equal(0x0f, mysteryAckBuffer[2]);

        // 3. Position update: X=1234, Y=5678, Z=5, name="Tester"
        var position = BuildPositionPacket(sequence: 3, password: "friends", name: "Tester", x: 1234, y: 5678, z: 5);
        await stream.WriteAsync(position);
        var ackBuf = new byte[28];
        await ReadExactAsync(stream, ackBuf, cts);
        Assert.Equal(0x02, ackBuf[2]);

        // 4. Query list — expect our own position echoed back
        var queryList = BuildQueryListPacket(sequence: 4, password: "friends");
        await stream.WriteAsync(queryList);
        var listHeader = new byte[36];
        await ReadExactAsync(stream, listHeader, cts);
        Assert.Equal(1u, ReadUInt32LE(listHeader, 24)); // one player: ourselves
        var playerBlock = new byte[92];
        await ReadExactAsync(stream, playerBlock, cts);
        var name = System.Text.Encoding.ASCII.GetString(playerBlock, 0, 6);
        Assert.Equal("Tester", name);

        cts.Cancel();
    }

    [Fact]
    public async Task TwoIndependentPositionConnections_SameNameAndPassword_BothShowInQueryList()
    {
        // Models the real protocol's "redundant position connection" behavior
        // (spec: "Redundant position connection") — two fully independent
        // connections, own handshake each, no master_id relationship between
        // them at all. Neither should be rejected as a duplicate login.
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        using var leg1 = new TcpClient();
        await leg1.ConnectAsync(IPAddress.Loopback, port);
        var leg1Stream = leg1.GetStream();
        await leg1Stream.WriteAsync(BuildHandshakeRequest(sequence: 1));
        await ReadExactAsync(leg1Stream, new byte[60], cts);
        var leg1Position = BuildPositionPacket(sequence: 2, password: "friends", name: "Dual", x: 1, y: 1, z: 0);
        await leg1Stream.WriteAsync(leg1Position);
        await ReadExactAsync(leg1Stream, new byte[28], cts);

        using var leg2 = new TcpClient();
        await leg2.ConnectAsync(IPAddress.Loopback, port);
        var leg2Stream = leg2.GetStream();
        await leg2Stream.WriteAsync(BuildHandshakeRequest(sequence: 1));
        var leg2Ack = new byte[60];
        await ReadExactAsync(leg2Stream, leg2Ack, cts);
        var leg2Position = BuildPositionPacket(sequence: 2, password: "friends", name: "Dual", x: 1, y: 1, z: 0);
        await leg2Stream.WriteAsync(leg2Position);
        await ReadExactAsync(leg2Stream, new byte[28], cts);

        // Both legs got their own independently-assigned client id (not a merge).
        Assert.NotEqual(0u, ReadUInt32LE(leg2Ack, 20));

        var queryList = BuildQueryListPacket(sequence: 3, password: "friends");
        await leg1Stream.WriteAsync(queryList);
        var listHeader = new byte[36];
        await ReadExactAsync(leg1Stream, listHeader, cts);
        Assert.Equal(2u, ReadUInt32LE(listHeader, 24)); // both legs visible

        cts.Cancel();
    }

    [Fact]
    public async Task ChatBind_ThenPollOnChatConnection_GetsNormalNoChatNotFault()
    {
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        using var position = new TcpClient();
        await position.ConnectAsync(IPAddress.Loopback, port);
        var positionStream = position.GetStream();
        await positionStream.WriteAsync(BuildHandshakeRequest(sequence: 1));
        var positionAck = new byte[60];
        await ReadExactAsync(positionStream, positionAck, cts);
        var masterId = ReadUInt32LE(positionAck, 20);

        using var chat = new TcpClient();
        await chat.ConnectAsync(IPAddress.Loopback, port);
        var chatStream = chat.GetStream();
        await chatStream.WriteAsync(BuildChatBindRequest(sequence: 1, masterId));
        var chatAck = new byte[60];
        await ReadExactAsync(chatStream, chatAck, cts);
        Assert.Equal(0x0c, chatAck[2]);
        Assert.Equal(masterId, ReadUInt32LE(chatAck, 20)); // echoes the position leg's own id

        var poll = BuildChatPollPacket(sequence: 2, password: "friends", playerName: "Chatter");
        await chatStream.WriteAsync(poll);
        var pollResponse = new byte[44];
        await ReadExactAsync(chatStream, pollResponse, cts);
        Assert.Equal(0x02, pollResponse[2]); // normal NoChat, not a Fault (0x03)
        Assert.Equal(44, pollResponse.Length);

        cts.Cancel();
    }

    [Fact]
    public async Task ChatPollOnPositionConnection_GetsFault()
    {
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(BuildHandshakeRequest(sequence: 1));
        await ReadExactAsync(stream, new byte[60], cts);

        var poll = BuildChatPollPacket(sequence: 2, password: "friends", playerName: "Chatter");
        await stream.WriteAsync(poll);
        var response = new byte[32];
        await ReadExactAsync(stream, response, cts);
        Assert.Equal(0x03, response[2]); // Fault

        cts.Cancel();
    }

    [Fact]
    public async Task ChatBind_UnknownMasterId_DropsConnectionWithoutAck()
    {
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(BuildChatBindRequest(sequence: 1, masterId: 0xFFFFFFFF)); // no client has this id

        var responseBuffer = new byte[16];
        var readTask = ReadExactAsync(stream, responseBuffer, cts);
        var completed = await Task.WhenAny(readTask, Task.Delay(500));
        Assert.Same(readTask, completed);
        Assert.Equal(0, await readTask); // 0 bytes = connection closed, not acked

        cts.Cancel();
    }

    [Fact]
    public async Task ClosingChatConnection_DoesNotRemovePositionConnectionsPlayer()
    {
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        using var position = new TcpClient();
        await position.ConnectAsync(IPAddress.Loopback, port);
        var positionStream = position.GetStream();
        await positionStream.WriteAsync(BuildHandshakeRequest(sequence: 1));
        var positionAck = new byte[60];
        await ReadExactAsync(positionStream, positionAck, cts);
        var masterId = ReadUInt32LE(positionAck, 20);
        var positionPacket = BuildPositionPacket(sequence: 2, password: "friends", name: "Stays", x: 1, y: 1, z: 0);
        await positionStream.WriteAsync(positionPacket);
        await ReadExactAsync(positionStream, new byte[28], cts);

        var chat = new TcpClient();
        await chat.ConnectAsync(IPAddress.Loopback, port);
        var chatStream = chat.GetStream();
        await chatStream.WriteAsync(BuildChatBindRequest(sequence: 1, masterId));
        await ReadExactAsync(chatStream, new byte[60], cts);
        chat.Close(); // the chat connection dies; the position connection is still open

        await Task.Delay(200); // give the server's cleanup a moment to run

        var queryList = BuildQueryListPacket(sequence: 3, password: "friends");
        await positionStream.WriteAsync(queryList);
        var listHeader = new byte[36];
        await ReadExactAsync(positionStream, listHeader, cts);
        Assert.Equal(1u, ReadUInt32LE(listHeader, 24)); // still there

        cts.Cancel();
    }

    [Fact]
    public async Task ConnectionsBeyondPerAddressLimit_AreClosedWithoutAnyResponse()
    {
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        var accepted = new List<TcpClient>();
        try
        {
            for (var i = 0; i < ConnectionLimits.MaxPerAddress; i++)
            {
                var c = new TcpClient();
                await c.ConnectAsync(IPAddress.Loopback, port);
                accepted.Add(c);
            }

            using var rejected = new TcpClient();
            await rejected.ConnectAsync(IPAddress.Loopback, port);
            var rejectedStream = rejected.GetStream();

            // No protocol response of any kind — the socket should just close.
            var buffer = new byte[1];
            var readTask = rejectedStream.ReadAsync(buffer, cts.Token).AsTask();
            var completed = await Task.WhenAny(readTask, Task.Delay(500));
            Assert.Same(readTask, completed);
            Assert.Equal(0, await readTask); // 0 bytes = orderly close, not data
        }
        finally
        {
            foreach (var c in accepted)
                c.Dispose();
        }

        cts.Cancel();
    }

    [Fact]
    public async Task EmptyNonDomainZeroDomain_GetsReapedByMaintenanceSweep()
    {
        var host = new Host(lockedPassword: null);
        host.CreateDomainZero();
        using var cts = new CancellationTokenSource();
        var (_, port) = await StartTestServerAsync(host, cts);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(BuildHandshakeRequest(sequence: 1));
        await ReadExactAsync(stream, new byte[60], cts);
        var position = BuildPositionPacket(sequence: 2, password: "temp-room", name: "Ghost", x: 1, y: 1, z: 0);
        await stream.WriteAsync(position);
        await ReadExactAsync(stream, new byte[28], cts);

        Assert.NotNull(host.GetDomainByPassword("temp-room"));

        client.Close(); // its only client leaves — domain becomes empty
        await Task.Delay(3000); // longer than the maintenance sweep interval

        Assert.Null(host.GetDomainByPassword("temp-room"));

        cts.Cancel();
    }

    private static byte[] BuildChatBindRequest(uint sequence, uint masterId)
    {
        var body = new byte[72];
        body[0] = 0x05; body[1] = 0x00; body[2] = 0x0b; body[3] = 0x03; body[4] = 0x10;
        WriteUInt32LE(body, 8, (uint)body.Length);
        WriteUInt32LE(body, 12, sequence);
        WriteUInt32LE(body, 20, masterId);
        ChatInterfaceGuid.CopyTo(body, 32);
        return body;
    }

    private static byte[] BuildChatPollPacket(uint sequence, string password, string playerName)
    {
        var writer = new PacketWriter();
        writer.WriteBytes(new byte[16]); // framing header, patched below
        writer.WriteZeros(4);
        writer.WriteUInt16LE(1); // major = 1 (chat)
        writer.WriteUInt16LE(1); // minor = 1 (poll)
        var passwordBytes = new byte[20];
        var pw = System.Text.Encoding.ASCII.GetBytes(password);
        Array.Copy(pw, 0, passwordBytes, 0, pw.Length);
        writer.WriteBytes(passwordBytes);
        writer.WriteZeros(4); // local IP marker
        writer.WriteBytes(new byte[] { 0x00, 0x00, 0x02, 0x00 });
        writer.WriteNdrString(playerName, pad: false);

        var buffer = writer.ToArray();
        buffer[0] = 0x05; buffer[1] = 0x00; buffer[2] = 0x00; buffer[3] = 0x03; buffer[4] = 0x10;
        WriteUInt32LE(buffer, 8, (uint)buffer.Length);
        WriteUInt32LE(buffer, 12, sequence);
        return buffer;
    }

    private static readonly byte[] ChatInterfaceGuid =
    {
        0xa2, 0xa0, 0x6e, 0xd7, 0xf0, 0x2c, 0xb9, 0x46,
        0xa8, 0x7b, 0xe4, 0x22, 0x77, 0xbc, 0x1e, 0xf2,
    };

    private static byte[] BuildPositionPacket(uint sequence, string password, string name, uint x, uint y, byte z)
    {
        // 48-byte common prefix (header + stublen + major/minor + password +
        // marker) followed directly by the 92-byte name/reserved/position
        // block — matches the real 140-byte wire packet.
        var buffer = new byte[48 + 92];
        buffer[0] = 0x05; buffer[1] = 0x00; buffer[2] = 0x00; buffer[3] = 0x03; buffer[4] = 0x10;
        WriteUInt32LE(buffer, 8, (uint)buffer.Length);
        WriteUInt32LE(buffer, 12, sequence);
        buffer[20] = 0; buffer[21] = 0; // major = 0
        buffer[22] = 1; buffer[23] = 0; // minor = 1 (position)
        var pw = System.Text.Encoding.ASCII.GetBytes(password);
        Array.Copy(pw, 0, buffer, 24, pw.Length);
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, 0, buffer, 48, nameBytes.Length);
        var positionOffset = 48 + 64 + 12;
        buffer[positionOffset + 3] = z;
        WriteUInt32LE(buffer, positionOffset + 4, x);
        WriteUInt32LE(buffer, positionOffset + 8, y);
        return buffer;
    }

    private static byte[] BuildQueryListPacket(uint sequence, string password)
    {
        var buffer = new byte[16 + 48];
        buffer[0] = 0x05; buffer[1] = 0x00; buffer[2] = 0x00; buffer[3] = 0x03; buffer[4] = 0x10;
        WriteUInt32LE(buffer, 8, (uint)buffer.Length);
        WriteUInt32LE(buffer, 12, sequence);
        buffer[16 + 6] = 2; // minor = 2 (query list)
        var pw = System.Text.Encoding.ASCII.GetBytes(password);
        Array.Copy(pw, 0, buffer, 16 + 8, pw.Length);
        return buffer;
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationTokenSource cts)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cts.Token);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
