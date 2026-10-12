using System.Text;
using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

/// <summary>
/// CUOX v3 <c>(2,4)</c> sync: one round trip that both uploads the caller's state and returns
/// the current state of every other fresh player.
///
/// Request:  48-byte prefix | 36-byte extended body | 16-byte position block (verbatim, as in
///           a standard position update).
/// Response: 24-byte header (marker20 = 4) | uint16 count | uint16 reserved | count x record,
///           record = byte nameLen | name (ASCII) | 16-byte position block | byte hasExtended |
///           [36-byte extended body if hasExtended].
/// The response is always the full current state (no deltas, no queue): a player missing from
/// it has gone stale, and nothing can back up behind a slow poller.
/// </summary>
public static class ExtendedSyncHandler
{
    public const int RequestLength = 48 + 36 + 16;
    private const int BodyOffset = 48;
    private const int PositionOffset = BodyOffset + 36;

    private static readonly byte[] ResponseHeaderTemplate =
    {
        0x05, 0x00, 0x02, 0x03, 0x10, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00,
    };

    public static byte[]? Handle(Client client, byte[] data, int packetOffset, int packetLength, uint sequence)
    {
        if (!client.SupportsExtended || client.ExtendedVersion < 3 || packetLength < RequestLength)
            return null;

        var body = new byte[36];
        Array.Copy(data, packetOffset + BodyOffset, body, 0, 36);
        var position = new byte[16];
        Array.Copy(data, packetOffset + PositionOffset, position, 0, 16);

        client.PositionBlock = position;
        client.HavePosition = true;
        ExtendedPositionHandler.Apply(client, body); // also touches presence

        var now = DateTime.UtcNow;
        var staleness = TimeSpan.FromSeconds(Limits.PositionStalenessSeconds);
        var peers = client.Domain!.Clients
            .Where(c => c != client && !c.ShouldDestroy && c.Name.Length > 0 && now - c.LastPositionUpdateAt < staleness)
            .ToList();

        var writer = new PacketWriter();
        writer.WriteBytes(ResponseHeaderTemplate);
        writer.WriteUInt16LE((ushort)peers.Count);
        writer.WriteUInt16LE(0);

        foreach (var peer in peers)
        {
            var name = Encoding.ASCII.GetBytes(peer.Name);
            var nameLength = Math.Min(name.Length, 63);
            writer.WriteByte((byte)nameLength);
            writer.WriteBytes(name.AsSpan(0, nameLength).ToArray());
            writer.WriteBytes(peer.PositionBlock);
            if (peer.SupportsExtended && peer.ExtendedBody is { } extended)
            {
                writer.WriteByte(1);
                writer.WriteBytes(extended);
            }
            else
            {
                writer.WriteByte(0);
            }
        }

        return ResponseHeader.Finalize(writer.ToArray(), sequence);
    }
}
