using UOAMServer.Domain;
using Xunit;

namespace UOAMServer.Tests.Domain;

public class HostTests
{
    [Fact]
    public void CreateDomainZero_HasEmptyPasswordAndIsRegistered()
    {
        var host = new Host(lockedPassword: null);

        var domainZero = host.CreateDomainZero();

        Assert.Equal(string.Empty, domainZero.Password);
        Assert.Contains(domainZero, host.Domains);
        Assert.Same(domainZero, host.DomainZero);
    }

    [Fact]
    public void CreateDomain_UnderLimit_CreatesAndRegisters()
    {
        var host = new Host(lockedPassword: null);

        var domain = host.CreateDomain("hello");

        Assert.NotNull(domain);
        Assert.Equal("hello", domain!.Password);
        Assert.Contains(domain, host.Domains);
    }

    [Fact]
    public void CreateDomain_AtLimit_ReturnsNull()
    {
        var host = new Host(lockedPassword: null);
        for (var i = 0; i < Limits.MaxDomains; i++)
            Assert.NotNull(host.CreateDomain($"pw{i}"));

        var result = host.CreateDomain("one-too-many");

        Assert.Null(result);
    }

    [Fact]
    public void GetDomainByPassword_FindsExistingDomain()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("findme");

        var found = host.GetDomainByPassword("findme");

        Assert.Same(domain, found);
    }

    [Fact]
    public void GetDomainByPassword_NoMatch_ReturnsNull()
    {
        var host = new Host(lockedPassword: null);

        Assert.Null(host.GetDomainByPassword("nope"));
    }

    [Fact]
    public void RemoveDomain_UnregistersIt()
    {
        var host = new Host(lockedPassword: null);
        var domain = host.CreateDomain("bye")!;

        host.RemoveDomain(domain);

        Assert.DoesNotContain(domain, host.Domains);
    }

    [Fact]
    public void FindClientById_SearchesAcrossAllDomains()
    {
        var host = new Host(lockedPassword: null);
        var domainA = host.CreateDomain("a")!;
        var domainB = host.CreateDomain("b")!;
        var client = new Client(42);
        domainB.TryAddClient(client);

        var found = host.FindClientById(42);

        Assert.Same(client, found);
    }

    [Fact]
    public void FindClientById_NoMatch_ReturnsNull()
    {
        var host = new Host(lockedPassword: null);

        Assert.Null(host.FindClientById(999));
    }
}
