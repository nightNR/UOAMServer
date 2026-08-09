using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class HandshakeHandler
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

    private const int MinPositionRequestLength = 24;
    private const int ChatBindRequestLength = 72;
    private const int ChatInterfaceGuidOffset = 32;
    private const int MasterIdOffset = 20;

    public static bool IsValidPositionRequest(byte[] data, int offset, int length)
    {
        if (length < MinPositionRequestLength)
            return false;

        return new ReadOnlySpan<byte>(data, offset, length).IndexOf(InterfaceGuid) >= 0;
    }

    public static bool IsValidChatBindRequest(byte[] data, int offset, int length)
    {
        // Length alone (72, checked by the caller before this even runs)
        // already discriminates chat-bind from the ~116-byte position shape
        // — real uoam_Orion clients were observed sending the *position*
        // InterfaceGuid here instead of the documented ChatInterfaceGuid, so
        // this only requires *a* known interface GUID, not a specific one.
        if (length != ChatBindRequestLength)
            return false;

        var guidStart = offset + ChatInterfaceGuidOffset;
        var guid = new ReadOnlySpan<byte>(data, guidStart, ChatInterfaceGuid.Length);
        return guid.SequenceEqual(ChatInterfaceGuid) || guid.SequenceEqual(InterfaceGuid);
    }

    public static bool TryGetMasterId(byte[] data, int offset, out uint masterId)
    {
        masterId = 0;
        if (offset + MasterIdOffset + 4 > data.Length)
            return false;

        var reader = new PacketReader(data, offset + MasterIdOffset);
        masterId = reader.ReadUInt32LE();
        return true;
    }

    public static byte[] BuildAck(Client client, uint sequence, int port)
    {
        var response = (byte[])HandshakeTemplates.AckTemplate.Clone();

        ResponseHeader.WriteUInt32At(response, 20, client.Id);

        // offset 24-31 is a DCE/RPC sec_addr: a 2-byte length prefix (string
        // length including the NUL terminator), then the NUL-terminated port
        // string, then zero padding out to the fixed 8-byte slot. The
        // template's own bytes for port "2000" are 05 00 '2' '0' '0' '0' 00
        // 00 — a bare ASCII copy here (no length prefix) corrupts that
        // length field and the real client rejects the handshake.
        var portBytes = System.Text.Encoding.ASCII.GetBytes(port.ToString());
        var portSpecLength = (ushort)(portBytes.Length + 1);
        Array.Clear(response, 24, 8);
        response[24] = (byte)portSpecLength;
        response[25] = (byte)(portSpecLength >> 8);
        Array.Copy(portBytes, 0, response, 26, portBytes.Length);

        return ResponseHeader.Finalize(response, sequence);
    }

    public static byte[] BuildNak(uint sequence)
    {
        var response = (byte[])HandshakeTemplates.NakTemplate.Clone();
        return ResponseHeader.Finalize(response, sequence);
    }
}
