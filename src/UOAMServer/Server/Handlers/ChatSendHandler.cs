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
        // 0x00020000 constant + NDR sender name + NDR text) is what a
        // recipient's ChatData response needs after its own 24-byte header —
        // relayed opaquely rather than re-encoded, except for the subtype-1
        // text-field trailer added below (a real hub pads it; the sender
        // never does).
        //
        // payloadLength MUST come from the packet's own declared length
        // (packetLength), not from data.Length: data is the connection's
        // shared, oversized receive buffer, reused across reads and never
        // cleared between packets. Some payloads here — e.g. the font/color
        // subtype (3): COLORREF + a 60-byte LOGFONTA — have no internal
        // length prefix of their own; a receiving client locates LOGFONTA
        // from the end of the packet (declared length minus the trailing
        // zero dword added below, minus 60). Relaying trailing garbage from
        // the shared buffer would inflate the declared length and make the
        // recipient read the wrong bytes as the font — not just wasted
        // bandwidth, but a real correctness bug (and a data-leak one, since
        // the leaked bytes could be a previous packet's password field).
        var payloadStart = bodyOffset + PayloadOffset;
        var payloadLength = packetLength - PayloadOffset;

        var subtype = new PacketReader(data, payloadStart).ReadUInt32LE();

        // Verified against a live client-vs-client host-mode capture
        // (2026-08-10): a real hub always pads the last field — chat text
        // for subtype 1, the COLORREF+LOGFONTA blob for subtype 3, both
        // wrapped in the same [maxCount][marker][actualCount] triple — to a
        // 4-byte boundary and appends one extra all-zero trailing dword,
        // regardless of that field's length. A sender's own send packet
        // never includes this; the hub adds it when relaying. Without it, a
        // real UOAM/uoam_Orion client silently fails to render the relayed
        // message (subtype 1) or font (subtype 3).
        var trailer = subtype is 1 or 3 ? PaddedTrailerLength(data, payloadStart, payloadLength) : 0;
        var body = new byte[payloadLength + trailer];
        Array.Copy(data, payloadStart, body, 0, payloadLength);

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

    // subtype(4) + 0x00020000 marker(4) + NDR sender name (padded) + a
    // trailing field (unpadded as sent) sharing the same
    // [maxCount][marker][actualCount] triple shape whether it's subtype 1's
    // NDR text or subtype 3's COLORREF+LOGFONTA blob — walk past the name to
    // find that field's actual_count, then compute how many zero bytes a
    // real hub would add: pad-to-4-byte-boundary + one constant trailing
    // zero dword.
    private static int PaddedTrailerLength(byte[] data, int payloadStart, int payloadLength)
    {
        var reader = new PacketReader(data, payloadStart + 4 + 4);
        var nameActualCount = (int)reader.ReadUInt32LE(); // max_count
        reader.ReadUInt32LE(); // offset
        reader.ReadUInt32LE(); // actual_count (same as max_count)
        var nameFieldEnd = reader.Position + NdrString.PaddedLength(nameActualCount);

        var textActualCount = (int)new PacketReader(data, nameFieldEnd).ReadUInt32LE(); // max_count
        var pad = NdrString.PaddedLength(textActualCount) - textActualCount;
        return pad + 4;
    }
}
