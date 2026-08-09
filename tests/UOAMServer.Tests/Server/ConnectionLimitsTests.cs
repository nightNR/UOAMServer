using System.Net;
using UOAMServer.Server;
using Xunit;

namespace UOAMServer.Tests.Server;

public class ConnectionLimitsTests
{
    [Fact]
    public void TryAcquire_UnderLimit_ReturnsTrue()
    {
        var limits = new ConnectionLimits();

        Assert.True(limits.TryAcquire(IPAddress.Loopback));
    }

    [Fact]
    public void TryAcquire_AtLimit_ReturnsFalse()
    {
        var limits = new ConnectionLimits();
        for (var i = 0; i < ConnectionLimits.MaxPerAddress; i++)
            Assert.True(limits.TryAcquire(IPAddress.Loopback));

        Assert.False(limits.TryAcquire(IPAddress.Loopback));
    }

    [Fact]
    public void Release_FreesUpASlot()
    {
        var limits = new ConnectionLimits();
        for (var i = 0; i < ConnectionLimits.MaxPerAddress; i++)
            limits.TryAcquire(IPAddress.Loopback);

        limits.Release(IPAddress.Loopback);

        Assert.True(limits.TryAcquire(IPAddress.Loopback));
    }

    [Fact]
    public void DifferentAddresses_HaveIndependentLimits()
    {
        var limits = new ConnectionLimits();
        for (var i = 0; i < ConnectionLimits.MaxPerAddress; i++)
            Assert.True(limits.TryAcquire(IPAddress.Loopback));

        Assert.True(limits.TryAcquire(IPAddress.Parse("10.0.0.1")));
    }
}
