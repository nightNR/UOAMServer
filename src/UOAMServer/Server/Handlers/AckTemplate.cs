using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class AckTemplate
{
    // Copied verbatim from uoamhub.c's packet_ack. marker20 (offset 20-23) is
    // 0 here — the position-family value. The chat-family variant is the
    // same bytes with that one field set to 1 (see BuildChatFamily).
    private static readonly byte[] Template =
    {
        0x05, 0x00, 0x02, 0x03, 0x10, 0x00, 0x00, 0x00,
        0x1c, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    };

    public static byte[] Build(uint sequence)
    {
        var response = (byte[])Template.Clone();
        return ResponseHeader.Finalize(response, sequence);
    }

    public static byte[] BuildChatFamily(uint sequence)
    {
        var response = (byte[])Template.Clone();
        ResponseHeader.WriteUInt32At(response, 20, 1);
        return ResponseHeader.Finalize(response, sequence);
    }
}
