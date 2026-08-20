namespace UOAMServer.Domain;

public sealed class Client
{
    public uint Id { get; }
    public bool Handshaken { get; set; }
    public bool Authorized { get; set; }
    public bool HavePosition { get; set; }
    public bool ShouldDestroy { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte[] ReservedBlock { get; set; } = new byte[12];
    public byte[] PositionBlock { get; set; } = new byte[16];
    public byte[] LocalIp { get; set; } = new byte[4];
    public Domain? Domain { get; set; }
    public Queue<byte[]> ChatQueue { get; } = new();
    public DateTime LastPositionUpdateAt { get; set; } = DateTime.MinValue;

    // ClassicUO extended-protocol overlay (see
    // docs/superpowers/specs/2026-08-10-classicuo-extension-design.md).
    // SupportsExtended is set only by ExtendedCapabilityHandler — a stock
    // UOAM/uoam_Orion client never sends that packet, so it stays false and
    // none of the fields below are ever populated or read for it.
    public bool SupportsExtended { get; set; }
    public uint SerialId { get; set; }
    public ushort Hp { get; set; }
    public ushort MaxHp { get; set; }
    public ushort Mana { get; set; }
    public ushort MaxMana { get; set; }
    public ushort Stamina { get; set; }
    public ushort MaxStamina { get; set; }
    public byte Facet { get; set; }
    public byte Notoriety { get; set; }
    public uint GuildId { get; set; }
    public ushort ExtendedFlags { get; set; }
    public short VelocityX { get; set; }
    public short VelocityY { get; set; }
    public ulong ExtendedTimestamp { get; set; }
    public Queue<byte[]> ExtendedQueue { get; } = new();

    public Client(uint id)
    {
        Id = id;
    }

    public void TouchPosition() => LastPositionUpdateAt = DateTime.UtcNow;

    // A chat connection and its position connection(s) share this same
    // Client object (see design spec's "Connection topology"). Real traffic
    // proved that an idle timer per socket, reset only by that socket's own
    // packets, lets a real client's naturally-quiet chat leg get closed by
    // the server, which then cascades into the client losing position too —
    // a full disconnect the user has to manually recover from. The Server
    // layer subscribes each connection's idle-reset here so activity on
    // *either* connection keeps *both* alive.
    public event Action? ActivityObserved;

    public void NotifyActivity() => ActivityObserved?.Invoke();
}
