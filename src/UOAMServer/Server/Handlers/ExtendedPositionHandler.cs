using UOAMServer.Domain;
using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class ExtendedPositionHandler
{
    private const int BodyOffset = 48;
    private const int BodyLength = 36;

    public static byte[] Handle(Client sender, byte[] data, int bodyOffset, uint sequence)
    {
        var body = new byte[BodyLength];
        Array.Copy(data, bodyOffset + BodyOffset, body, 0, BodyLength);
        Apply(sender, body);
        return AckTemplate.Build(sequence);
    }

    /// <summary>
    /// Stores a sender's 36-byte extended body and relays it to recipients that read via the
    /// (2,3) poll. CUOX v3 recipients are skipped: they read everything from their own sync
    /// response, so queuing for them would only grow memory.
    /// </summary>
    public static void Apply(Client sender, byte[] body)
    {
        var reader = new PacketReader(body, 0);
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
        sender.ExtendedBody = body;
        sender.TouchPosition();

        if (sender.Domain is null)
            return;

        foreach (var recipient in sender.Domain.Clients)
        {
            if (recipient == sender)
                continue;

            if (!recipient.SupportsExtended || recipient.ExtendedVersion >= 3)
                continue;

            if (recipient.ExtendedVersion >= 2)
            {
                recipient.ExtendedPending[sender.Id] = body;
                continue;
            }

            if (recipient.ExtendedQueue.Count >= Limits.MaxExtended)
                continue;

            recipient.ExtendedQueue.Enqueue(body);
        }
    }
}
