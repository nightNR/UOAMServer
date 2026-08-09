using UOAMServer.Domain;
using Xunit;

namespace UOAMServer.Tests.Domain;

public class DomainTests
{
    [Fact]
    public void TryAddClient_UnderLimit_AddsAndSetsClientDomain()
    {
        var host = new Host(lockedPassword: null);
        var domain = new UOAMServer.Domain.Domain(host, "secret");
        var client = new Client(1);

        var added = domain.TryAddClient(client);

        Assert.True(added);
        Assert.Contains(client, domain.Clients);
        Assert.Same(domain, client.Domain);
    }

    [Fact]
    public void TryAddClient_AtLimit_ReturnsFalse()
    {
        var host = new Host(lockedPassword: null);
        var domain = new UOAMServer.Domain.Domain(host, "secret");
        for (var i = 0; i < Limits.MaxClientsPerDomain; i++)
            Assert.True(domain.TryAddClient(new Client((uint)i)));

        var result = domain.TryAddClient(new Client(9999));

        Assert.False(result);
        Assert.Equal(Limits.MaxClientsPerDomain, domain.Clients.Count);
    }

    [Fact]
    public void RemoveClient_ClearsClientDomainReference()
    {
        var host = new Host(lockedPassword: null);
        var domain = new UOAMServer.Domain.Domain(host, "secret");
        var client = new Client(1);
        domain.TryAddClient(client);

        domain.RemoveClient(client);

        Assert.DoesNotContain(client, domain.Clients);
        Assert.Null(client.Domain);
    }

    [Fact]
    public void IsEmpty_ReflectsClientCount()
    {
        var host = new Host(lockedPassword: null);
        var domain = new UOAMServer.Domain.Domain(host, "secret");
        Assert.True(domain.IsEmpty);

        var client = new Client(1);
        domain.TryAddClient(client);
        Assert.False(domain.IsEmpty);

        domain.RemoveClient(client);
        Assert.True(domain.IsEmpty);
    }

    [Fact]
    public void Lock_IsDistinctPerDomainInstance()
    {
        // Robustness: per-domain locking only achieves isolation between
        // rooms if each Domain truly owns its own lock object, not a shared
        // one — this is what makes "different rooms don't block each other"
        // true rather than aspirational.
        var host = new Host(lockedPassword: null);
        var domainA = new UOAMServer.Domain.Domain(host, "a");
        var domainB = new UOAMServer.Domain.Domain(host, "b");

        Assert.NotSame(domainA.Lock, domainB.Lock);
    }
}
