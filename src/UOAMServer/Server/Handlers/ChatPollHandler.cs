using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class ChatPollHandler
{
    // Corrected size (44 bytes, not the C server's mistaken 28) — see design
    // spec "Known protocol bugs". Bytes beyond the shared response header and
    // marker20=1 are zero-filled (unconfirmed content, see spec note).
    private static readonly byte[] NoChatTemplate = new byte[44]
    {
        0x05, 0x00, 0x02, 0x03, 0x10, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x14, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    };

    private static readonly byte[] ChatDataHeader =
    {
        0x05, 0x00, 0x02, 0x03, 0x10, 0x00, 0x00, 0x00,
        0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff, 0x01, 0x00, 0x00, 0x00,
    };

    public static byte[] Handle(Client client, uint sequence)
    {
        if (client.ChatQueue.Count == 0)
        {
            var noChat = (byte[])NoChatTemplate.Clone();
            return ResponseHeader.Finalize(noChat, sequence);
        }

        var body = client.ChatQueue.Dequeue();
        var writer = new PacketWriter();
        writer.WriteBytes(ChatDataHeader);
        writer.WriteBytes(body);

        return ResponseHeader.Finalize(writer.ToArray(), sequence);
    }
}
