using System.Text;
using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server;
using Xunit;

namespace UOAMServer.Tests.Server;

public class PacketDispatcherTests
{
    private static byte[] BuildDataPacket(ushort major, ushort minor, string password, byte[] payload)
    {
        var writer = new PacketWriter();
        writer.WriteBytes(new byte[16]); // framing header, unused by dispatcher body parsing
        writer.WriteZeros(4); // offset16-19 stub length, unused here
        writer.WriteUInt16LE(major);
        writer.WriteUInt16LE(minor);

        var passwordBytes = new byte[20];
        var ascii = Encoding.ASCII.GetBytes(password);
        Array.Copy(ascii, 0, passwordBytes, 0, ascii.Length);
        writer.WriteBytes(passwordBytes);

        writer.WriteZeros(4); // local IP marker, unused by dispatcher
        writer.WriteBytes(payload);
        return writer.ToArray();
    }

    private static PacketHeader MakeHeader(byte[] data) =>
        new(PacketType.Data, (uint)data.Length, sequence: 1);

    [Fact]
    public void Dispatch_EmptyPassword_SetsShouldDestroyAndReturnsNull()
    {
        var host = new Host(lockedPassword: null);
        var domainZero = host.CreateDomainZero();
        var client = new Client(1);
        domainZero.TryAddClient(client);
        client.Handshaken = true;
        var dispatcher = new PacketDispatcher(host);
        var packet = BuildDataPacket(major: 0, minor: 1, password: "", payload: new byte[92]);

        var response = dispatcher.Dispatch(client, packet, 0, MakeHeader(packet));

        Assert.Null(response);
        Assert.True(client.ShouldDestroy);
    }

    [Fact]
    public void Dispatch_NewPassword_CreatesDomainAndMovesClient()
    {
        var host = new Host(lockedPassword: null);
        var domainZero = host.CreateDomainZero();
        var client = new Client(1);
        domainZero.TryAddClient(client);
        client.Handshaken = true;
        var dispatcher = new PacketDispatcher(host);
        var packet = BuildDataPacket(major: 0, minor: 1, password: "friends", payload: new byte[92]);

        var response = dispatcher.Dispatch(client, packet, 0, MakeHeader(packet));

        Assert.NotNull(response);
        Assert.True(client.Authorized);
        Assert.Equal("friends", client.Domain!.Password);
    }

    [Fact]
    public void Dispatch_PositionMajorMinor_RoutesToPositionHandler()
    {
        var host = new Host(lockedPassword: null);
        var domainZero = host.CreateDomainZero();
        var client = new Client(1);
        domainZero.TryAddClient(client);
        client.Handshaken = true;
        var dispatcher = new PacketDispatcher(host);
        var packet = BuildDataPacket(major: 0, minor: 1, password: "pw", payload: new byte[92]);

        var response = dispatcher.Dispatch(client, packet, 0, MakeHeader(packet));

        Assert.NotNull(response);
        Assert.True(client.HavePosition);
    }

    [Fact]
    public void Dispatch_QueryListMajorMinor_RoutesToQueryListHandler()
    {
        var host = new Host(lockedPassword: null);
        var domainZero = host.CreateDomainZero();
        var client = new Client(1);
        domainZero.TryAddClient(client);
        client.Handshaken = true;
        var dispatcher = new PacketDispatcher(host);
        var packet = BuildDataPacket(major: 0, minor: 2, password: "pw", payload: Array.Empty<byte>());

        var response = dispatcher.Dispatch(client, packet, 0, MakeHeader(packet));

        Assert.NotNull(response);
        Assert.Equal(0x02, response![2]);
        Assert.Equal(0u, ReadUInt32LE(response, 20)); // PlayerList marker20
    }

    [Fact]
    public void Dispatch_UnknownMajorMinor_ReturnsNullWithoutKillingConnection()
    {
        var host = new Host(lockedPassword: null);
        var domainZero = host.CreateDomainZero();
        var client = new Client(1);
        domainZero.TryAddClient(client);
        client.Handshaken = true;
        var dispatcher = new PacketDispatcher(host);
        var packet = BuildDataPacket(major: 9, minor: 9, password: "pw", payload: Array.Empty<byte>());

        var response = dispatcher.Dispatch(client, packet, 0, MakeHeader(packet));

        Assert.Null(response);
        Assert.False(client.ShouldDestroy);
    }

    [Fact]
    public void Dispatch_ExtendedCapabilityMajorMinor_RoutesToExtendedCapabilityHandler()
    {
        var host = new Host(lockedPassword: null);
        var domainZero = host.CreateDomainZero();
        var client = new Client(1);
        domainZero.TryAddClient(client);
        client.Handshaken = true;
        var dispatcher = new PacketDispatcher(host);
        var payload = new byte[] { 0x43, 0x55, 0x4F, 0x58, 0x01, 0x00 }; // "CUOX" + version 1
        var packet = BuildDataPacket(major: 2, minor: 0, password: "pw", payload: payload);

        var response = dispatcher.Dispatch(client, packet, 0, MakeHeader(packet));

        Assert.NotNull(response);
        Assert.True(client.SupportsExtended);
    }

    [Fact]
    public void Dispatch_ExtendedPositionMajorMinor_RoutesToExtendedPositionHandler()
    {
        var host = new Host(lockedPassword: null);
        var domainZero = host.CreateDomainZero();
        var client = new Client(1);
        domainZero.TryAddClient(client);
        client.Handshaken = true;
        var dispatcher = new PacketDispatcher(host);
        var payload = new byte[36]; // all-zero extended fields is valid, just untested values here
        var packet = BuildDataPacket(major: 2, minor: 1, password: "pw", payload: payload);

        var response = dispatcher.Dispatch(client, packet, 0, MakeHeader(packet));

        Assert.NotNull(response);
        Assert.True(client.LastPositionUpdateAt > DateTime.MinValue);
    }

    [Fact]
    public void Dispatch_ExtendedPollMajorMinor_RoutesToExtendedPollHandler()
    {
        var host = new Host(lockedPassword: null);
        var domainZero = host.CreateDomainZero();
        var client = new Client(1);
        domainZero.TryAddClient(client);
        client.Handshaken = true;
        var dispatcher = new PacketDispatcher(host);
        var packet = BuildDataPacket(major: 2, minor: 3, password: "pw", payload: Array.Empty<byte>());

        var response = dispatcher.Dispatch(client, packet, 0, MakeHeader(packet));

        Assert.NotNull(response);
        Assert.Equal(24, response!.Length); // empty ExtendedQueue -> no body appended
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
