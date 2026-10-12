using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class ExtendedCapabilityHandler
{
    private static readonly byte[] Magic = { 0x43, 0x55, 0x4F, 0x58 }; // "CUOX"
    private const int MagicOffset = 48;
    private const int VersionOffset = 52;

    /// <summary>Highest CUOX version this server speaks. 1 = single-entry poll; 2 = batched latest-state poll.</summary>
    public const ushort MaxVersion = 2;

    // 16-byte framing header + offset16-19 (stub length, patched by Finalize) + marker20 = 3
    // (capability reply), then "CUOX" + uint16 negotiated version.
    private static readonly byte[] ReplyTemplate =
    {
        0x05, 0x00, 0x02, 0x03, 0x10, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
        0x43, 0x55, 0x4F, 0x58, 0x00, 0x00,
    };

    public static byte[]? Handle(Client client, byte[] data, int bodyOffset, uint sequence)
    {
        var magic = new ReadOnlySpan<byte>(data, bodyOffset + MagicOffset, Magic.Length);
        if (!magic.SequenceEqual(Magic))
            return null;

        ushort announced = data.Length >= bodyOffset + VersionOffset + 2
            ? (ushort)(data[bodyOffset + VersionOffset] | data[bodyOffset + VersionOffset + 1] << 8)
            : (ushort)1;
        ushort negotiated = Math.Min(Math.Max(announced, (ushort)1), MaxVersion);

        client.SupportsExtended = true;
        client.ExtendedVersion = negotiated;

        // A v1 client only understands a plain Ack. Only clients that announced >= 2 get the
        // capability reply that tells them which version the server will actually speak.
        if (announced < 2)
            return AckTemplate.Build(sequence);

        var reply = (byte[])ReplyTemplate.Clone();
        reply[28] = (byte)negotiated;
        reply[29] = (byte)(negotiated >> 8);
        return ResponseHeader.Finalize(reply, sequence);
    }
}
