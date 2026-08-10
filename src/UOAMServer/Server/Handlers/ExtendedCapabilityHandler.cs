using UOAMServer.Domain;

namespace UOAMServer.Server.Handlers;

public static class ExtendedCapabilityHandler
{
    private static readonly byte[] Magic = { 0x43, 0x55, 0x4F, 0x58 }; // "CUOX"
    private const int MagicOffset = 48;

    public static byte[]? Handle(Client client, byte[] data, int bodyOffset, uint sequence)
    {
        var magic = new ReadOnlySpan<byte>(data, bodyOffset + MagicOffset, Magic.Length);
        if (!magic.SequenceEqual(Magic))
            return null;

        client.SupportsExtended = true;
        return AckTemplate.Build(sequence);
    }
}
