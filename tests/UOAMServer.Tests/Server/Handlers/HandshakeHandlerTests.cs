using UOAMServer.Domain;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class HandshakeHandlerTests
{
    private static readonly byte[] InterfaceGuid =
    {
        0x10, 0x66, 0x15, 0x9e, 0x5c, 0x7b, 0xd2, 0x11,
        0xb8, 0xcf, 0x00, 0x80, 0xc7, 0x97, 0x1b, 0xe1,
    };

    private static readonly byte[] ChatInterfaceGuid =
    {
        0xa2, 0xa0, 0x6e, 0xd7, 0xf0, 0x2c, 0xb9, 0x46,
        0xa8, 0x7b, 0xe4, 0x22, 0x77, 0xbc, 0x1e, 0xf2,
    };

    private static byte[] BuildValidPositionRequestPayload(uint masterId = 0)
    {
        // Minimal well-formed position-handshake body: header (16) + master-id (4) +
        // padding up to offset 36 + the interface GUID, well past the 24-byte
        // minimum. Exact total length/shape beyond that isn't asserted here —
        // see the "Handshake request template caution" note in the spec.
        var body = new byte[36 + InterfaceGuid.Length];
        body[20] = (byte)masterId;
        body[21] = (byte)(masterId >> 8);
        body[22] = (byte)(masterId >> 16);
        body[23] = (byte)(masterId >> 24);
        InterfaceGuid.CopyTo(body, 24);
        return body;
    }

    private static byte[] BuildValidChatBindRequestPayload(uint masterId)
    {
        // Exact 72-byte chat-bind shape: header(16) + masterId(4) + 8 filler
        // bytes up to offset 32 + ChatInterfaceGuid(16) + 28 more filler bytes
        // = 72 total. Only length and GUID placement matter to the validator.
        var body = new byte[72];
        body[20] = (byte)masterId;
        body[21] = (byte)(masterId >> 8);
        body[22] = (byte)(masterId >> 16);
        body[23] = (byte)(masterId >> 24);
        ChatInterfaceGuid.CopyTo(body, 32);
        return body;
    }

    [Fact]
    public void IsValidPositionRequest_TooShort_ReturnsFalse()
    {
        var data = new byte[10];

        Assert.False(HandshakeHandler.IsValidPositionRequest(data, 0, data.Length));
    }

    [Fact]
    public void IsValidPositionRequest_MissingInterfaceGuid_ReturnsFalse()
    {
        var data = new byte[40]; // long enough, but all zero — no GUID present

        Assert.False(HandshakeHandler.IsValidPositionRequest(data, 0, data.Length));
    }

    [Fact]
    public void IsValidPositionRequest_ContainsInterfaceGuid_ReturnsTrue()
    {
        var data = BuildValidPositionRequestPayload();

        Assert.True(HandshakeHandler.IsValidPositionRequest(data, 0, data.Length));
    }

    [Fact]
    public void IsValidChatBindRequest_WrongLength_ReturnsFalse()
    {
        var data = new byte[71];
        ChatInterfaceGuid.CopyTo(data, 32);

        Assert.False(HandshakeHandler.IsValidChatBindRequest(data, 0, data.Length));
    }

    [Fact]
    public void IsValidChatBindRequest_MissingChatInterfaceGuid_ReturnsFalse()
    {
        var data = new byte[72]; // right length, but all zero — no GUID present

        Assert.False(HandshakeHandler.IsValidChatBindRequest(data, 0, data.Length));
    }

    [Fact]
    public void IsValidChatBindRequest_ContainsChatInterfaceGuidAtOffset32_ReturnsTrue()
    {
        var data = BuildValidChatBindRequestPayload(masterId: 1);

        Assert.True(HandshakeHandler.IsValidChatBindRequest(data, 0, data.Length));
    }

    [Fact]
    public void TryGetMasterId_ReadsOffset20_OnPositionShape()
    {
        var data = BuildValidPositionRequestPayload(masterId: 0xDEADBEEF);

        var result = HandshakeHandler.TryGetMasterId(data, 0, out var masterId);

        Assert.True(result);
        Assert.Equal(0xDEADBEEFu, masterId);
    }

    [Fact]
    public void TryGetMasterId_ReadsOffset20_OnChatBindShape()
    {
        var data = BuildValidChatBindRequestPayload(masterId: 0x12345);

        var result = HandshakeHandler.TryGetMasterId(data, 0, out var masterId);

        Assert.True(result);
        Assert.Equal(0x12345u, masterId);
    }

    [Fact]
    public void BuildAck_SetsClientIdAndPortAndFinalizesHeader()
    {
        var client = new Client(0x11223344);

        var response = HandshakeHandler.BuildAck(client, sequence: 7, port: 2000);

        Assert.Equal(0x0c, response[2]); // BindAck type
        Assert.Equal(60, response.Length); // corrected size, see design spec
        Assert.Equal(0x11223344u, ReadUInt32LE(response, 20));
        Assert.Equal(5, response[24] | response[25] << 8); // sec_addr length prefix (port string + NUL)
        Assert.Equal("2000", System.Text.Encoding.ASCII.GetString(response, 26, 4));
        Assert.Equal(0, response[30]); // NUL terminator
        Assert.Equal(7u, ReadUInt32LE(response, 12)); // sequence patched
    }

    [Fact]
    public void BuildNak_HasBindNakType()
    {
        var response = HandshakeHandler.BuildNak(sequence: 3);

        Assert.Equal(0x0d, response[2]);
        Assert.Equal(3u, ReadUInt32LE(response, 12));
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
