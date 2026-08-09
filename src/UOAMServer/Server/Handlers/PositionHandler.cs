using System.Text;
using UOAMServer.Domain;

namespace UOAMServer.Server.Handlers;

public static class PositionHandler
{
    private const int NameOffset = 48;
    private const int NameLength = 64;
    private const int ReservedOffset = NameOffset + NameLength;
    private const int ReservedLength = 12;
    private const int PositionOffset = ReservedOffset + ReservedLength;
    private const int PositionLength = 16;

    public static byte[] Handle(Client client, byte[] data, int bodyOffset, uint sequence)
    {
        var nameBytes = new byte[NameLength];
        Array.Copy(data, bodyOffset + NameOffset, nameBytes, 0, NameLength);
        var nulIndex = Array.IndexOf(nameBytes, (byte)0);
        var nameLength = nulIndex >= 0 ? nulIndex : NameLength;
        client.Name = Encoding.ASCII.GetString(nameBytes, 0, nameLength);

        var reserved = new byte[ReservedLength];
        Array.Copy(data, bodyOffset + ReservedOffset, reserved, 0, ReservedLength);
        client.ReservedBlock = reserved;

        var position = new byte[PositionLength];
        Array.Copy(data, bodyOffset + PositionOffset, position, 0, PositionLength);
        client.PositionBlock = position;

        client.HavePosition = true;
        client.TouchPosition();

        return AckTemplate.Build(sequence);
    }
}
