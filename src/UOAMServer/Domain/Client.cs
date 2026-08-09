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

    public Client(uint id)
    {
        Id = id;
    }

    public void TouchPosition() => LastPositionUpdateAt = DateTime.UtcNow;
}
