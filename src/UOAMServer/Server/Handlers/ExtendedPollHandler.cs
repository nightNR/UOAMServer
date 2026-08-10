using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class ExtendedPollHandler
{
    // 16-byte framing header + offset16-19 (unused, zero) + marker20 = 2
    // (extended-family — see AckTemplate for 0 = position, BuildChatFamily
    // for 1 = chat). Reused as-is for the empty-queue response, and as the
    // prefix before an appended 36-byte body when the queue has one.
    private static readonly byte[] ResponseHeaderTemplate =
    {
        0x05, 0x00, 0x02, 0x03, 0x10, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
    };

    public static byte[] Handle(Client client, uint sequence)
    {
        var writer = new PacketWriter();
        writer.WriteBytes(ResponseHeaderTemplate);

        if (client.ExtendedQueue.Count > 0)
            writer.WriteBytes(client.ExtendedQueue.Dequeue());

        return ResponseHeader.Finalize(writer.ToArray(), sequence);
    }
}
