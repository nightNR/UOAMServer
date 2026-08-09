namespace UOAMServer.Protocol;

public static class PacketLimits
{
    // Robustness (design spec) — generously above every legitimate payload
    // this protocol defines (the largest, a PlayerList response at
    // Domain.Limits.MaxClientsPerDomain clients, is ~23.6KB); bounds
    // PacketHeader.TryParse's declared-length check so a malformed or
    // hostile length can never wedge a connection's receive buffer.
    public const int MaxPacketLength = 65536;
}
