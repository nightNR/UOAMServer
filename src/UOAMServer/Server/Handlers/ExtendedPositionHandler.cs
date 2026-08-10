using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class ExtendedPositionHandler
{
    private const int BodyOffset = 48;
    private const int BodyLength = 36;

    public static byte[] Handle(Client sender, byte[] data, int bodyOffset, uint sequence)
    {
        var reader = new PacketReader(data, bodyOffset + BodyOffset);
        sender.SerialId = reader.ReadUInt32LE();
        sender.Hp = reader.ReadUInt16LE();
        sender.MaxHp = reader.ReadUInt16LE();
        sender.Mana = reader.ReadUInt16LE();
        sender.MaxMana = reader.ReadUInt16LE();
        sender.Stamina = reader.ReadUInt16LE();
        sender.MaxStamina = reader.ReadUInt16LE();
        sender.Facet = reader.ReadByte();
        sender.Notoriety = reader.ReadByte();
        sender.GuildId = reader.ReadUInt32LE();
        sender.ExtendedFlags = reader.ReadUInt16LE();
        sender.VelocityX = reader.ReadInt16LE();
        sender.VelocityY = reader.ReadInt16LE();
        sender.ExtendedTimestamp = reader.ReadUInt64LE();
        sender.TouchPosition();

        var body = new byte[BodyLength];
        Array.Copy(data, bodyOffset + BodyOffset, body, 0, BodyLength);

        if (sender.Domain is not null)
        {
            foreach (var recipient in sender.Domain.Clients)
            {
                if (recipient == sender)
                    continue;

                if (!recipient.SupportsExtended)
                    continue;

                if (recipient.ExtendedQueue.Count >= Limits.MaxExtended)
                    continue;

                recipient.ExtendedQueue.Enqueue(body);
            }
        }

        return AckTemplate.Build(sequence);
    }
}
