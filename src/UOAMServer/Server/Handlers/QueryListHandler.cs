using System.Text;
using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class QueryListHandler
{
    private static readonly byte[] HeaderTemplate =
    {
        0x05, 0x00, 0x02, 0x03, 0x10, 0x00, 0x00, 0x00,
        0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
        0xff, 0xff, 0xff, 0xff, 0x01, 0x00, 0x00, 0x00,
        0xff, 0xff, 0xff, 0xff,
    };

    public static byte[] Handle(Client requester, uint sequence)
    {
        var now = DateTime.UtcNow;
        var staleness = TimeSpan.FromSeconds(Limits.PositionStalenessSeconds);

        var included = requester.Domain!.Clients
            .Where(c => !c.ShouldDestroy && now - c.LastPositionUpdateAt < staleness)
            .ToList();

        var writer = new PacketWriter();
        writer.WriteBytes(HeaderTemplate);

        foreach (var client in included)
        {
            var nameBytes = new byte[64];
            var ascii = Encoding.ASCII.GetBytes(client.Name);
            Array.Copy(ascii, 0, nameBytes, 0, Math.Min(ascii.Length, 64));
            writer.WriteBytes(nameBytes);
            writer.WriteBytes(client.ReservedBlock);
            writer.WriteBytes(client.PositionBlock);
        }

        writer.WriteZeros(4); // trailer

        var response = writer.ToArray();
        ResponseHeader.WriteUInt32At(response, 24, (uint)included.Count);
        ResponseHeader.WriteUInt32At(response, 32, (uint)included.Count);

        return ResponseHeader.Finalize(response, sequence);
    }
}
