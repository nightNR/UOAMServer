namespace UOAMServer.Domain;

public static class Limits
{
    public const int MaxDomains = 64;
    public const int MaxClientsPerDomain = 256;
    public const int MaxChats = 64;
    public const int PositionStalenessSeconds = 5;
}
