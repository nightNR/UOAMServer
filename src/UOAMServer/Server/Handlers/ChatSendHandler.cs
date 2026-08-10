using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class ChatSendHandler
{
    public const int PayloadOffset = 52; // start of subtype: 48-byte common prefix + 4 zero bytes
    public const int RegistrationSubtype = 2;

    public static byte[] Handle(Client sender, byte[] data, int bodyOffset, int packetLength, uint sequence)
    {
        // Everything from PayloadOffset to the end of this packet (subtype +
        // 0x00020000 constant + NDR sender name + NDR text) is exactly what a
        // recipient's ChatData response needs after its own 24-byte header —
        // relay it opaquely rather than re-encoding it (see spec: "Not a bug").
        //
        // payloadLength MUST come from the packet's own declared length
        // (packetLength), not from data.Length: data is the connection's
        // shared, oversized receive buffer, reused across reads and never
        // cleared between packets. Some payloads here — e.g. the font/color
        // subtype (3): COLORREF + a 60-byte LOGFONTA — have no internal
        // length prefix of their own; a receiving client locates LOGFONTA at
        // (this packet's declared length - 60). Relaying trailing garbage
        // from the shared buffer would inflate the declared length and make
        // the recipient read the wrong bytes as the font — not just wasted
        // bandwidth, but a real correctness bug (and a data-leak one, since
        // the leaked bytes could be a previous packet's password field).
        var payloadStart = bodyOffset + PayloadOffset;
        var payloadLength = packetLength - PayloadOffset;
        var body = new byte[payloadLength];
        Array.Copy(data, payloadStart, body, 0, payloadLength);

        var subtype = new PacketReader(data, payloadStart).ReadUInt32LE();

        // Registration (subtype 2) isn't a chat message — never relay it, even
        // though this connection is only ever reached for it defensively (real
        // clients send it on the primary connection, where the connection-role
        // fault gate in UoamServer, Task 13, rejects it before this handler
        // ever runs).
        if (subtype != RegistrationSubtype && sender.Domain is not null)
        {
            // Includes the sender: real uoam_Orion clients don't echo their
            // own sent message locally — they only display it once it comes
            // back through their own next chat poll, so the sender's own
            // Client needs it queued too, not just everyone else's.
            foreach (var recipient in sender.Domain.Clients)
            {
                if (recipient.ChatQueue.Count >= Limits.MaxChats)
                    continue;

                recipient.ChatQueue.Enqueue(body);
            }
        }

        return AckTemplate.BuildChatFamily(sequence);
    }
}
