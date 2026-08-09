using UOAMServer.Domain;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class QueryListHandlerTests
{
    private static Client MakeClientWithPosition(UOAMServer.Domain.Domain domain, uint id, string name)
    {
        var client = new Client(id);
        domain.TryAddClient(client);
        client.Name = name;
        client.ReservedBlock = new byte[12];
        client.PositionBlock = new byte[16];
        client.TouchPosition();
        return client;
    }

    [Fact]
    public void Handle_IncludesOnlyClientsWithRecentPosition()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var withPosition = MakeClientWithPosition(domain, 1, "Alice");
        var withoutPosition = new Client(2); // LastPositionUpdateAt still DateTime.MinValue
        domain.TryAddClient(withoutPosition);

        var response = QueryListHandler.Handle(withPosition, sequence: 1);

        Assert.Equal(1u, ReadUInt32LE(response, 24)); // count
        Assert.Equal(1u, ReadUInt32LE(response, 28)); // constant
        Assert.Equal(1u, ReadUInt32LE(response, 32)); // count duplicated
    }

    [Fact]
    public void Handle_ExcludesClientsWithStalePosition()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var fresh = MakeClientWithPosition(domain, 1, "Alice");
        var stale = MakeClientWithPosition(domain, 2, "Bob");
        stale.LastPositionUpdateAt = DateTime.UtcNow.AddSeconds(-Limits.PositionStalenessSeconds - 1);

        var response = QueryListHandler.Handle(fresh, sequence: 1);

        Assert.Equal(1u, ReadUInt32LE(response, 24));
    }

    [Fact]
    public void Handle_ExcludesClientsPendingDestruction()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var alive = MakeClientWithPosition(domain, 1, "Alice");
        var dying = MakeClientWithPosition(domain, 2, "Bob");
        dying.ShouldDestroy = true;

        var response = QueryListHandler.Handle(alive, sequence: 1);

        Assert.Equal(1u, ReadUInt32LE(response, 24));
    }

    [Fact]
    public void Handle_DoesNotDeduplicateSameName_TwoIndependentClientsBothShow()
    {
        // Redundant position connections: two separate Client objects, same
        // name, both within the staleness window — both should appear.
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var leg1 = MakeClientWithPosition(domain, 1, "Alice");
        MakeClientWithPosition(domain, 2, "Alice");

        var response = QueryListHandler.Handle(leg1, sequence: 1);

        Assert.Equal(2u, ReadUInt32LE(response, 24));
    }

    [Fact]
    public void Handle_TotalLengthMatchesHeaderPlusPlayerBlocksPlusTrailer()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("pw")!;
        var requester = MakeClientWithPosition(domain, 1, "Alice");
        MakeClientWithPosition(domain, 2, "Bob");

        var response = QueryListHandler.Handle(requester, sequence: 1);

        // 36-byte header + 2 * 92-byte player blocks + 4-byte trailer
        Assert.Equal(36 + 2 * 92 + 4, response.Length);
        Assert.Equal(0x02, response[2]);
        Assert.Equal(0u, ReadUInt32LE(response, 20)); // marker20 = 0 -> PlayerList
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
