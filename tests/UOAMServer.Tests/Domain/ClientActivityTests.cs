using UOAMServer.Domain;
using Xunit;

namespace UOAMServer.Tests.Domain;

public class ClientActivityTests
{
    [Fact]
    public void NotifyActivity_InvokesSubscribedCallback()
    {
        // A chat connection and its position connection share the same
        // Client object (see design spec's "Connection topology"). Real
        // traffic proved that letting either connection's idle timer expire
        // independently causes a cascading full disconnect — packets on
        // *either* connection should keep *both* alive. This is the
        // Domain-layer hook the Server layer wires each connection's
        // idle-reset into.
        var client = new Client(1);
        var invocations = 0;
        client.ActivityObserved += () => invocations++;

        client.NotifyActivity();

        Assert.Equal(1, invocations);
    }

    [Fact]
    public void NotifyActivity_InvokesAllSubscribers_NotJustTheLastOne()
    {
        var client = new Client(1);
        var firstInvoked = false;
        var secondInvoked = false;
        client.ActivityObserved += () => firstInvoked = true;
        client.ActivityObserved += () => secondInvoked = true;

        client.NotifyActivity();

        Assert.True(firstInvoked);
        Assert.True(secondInvoked);
    }

    [Fact]
    public void NotifyActivity_AfterUnsubscribe_DoesNotInvokeThatCallback()
    {
        var client = new Client(1);
        var invocations = 0;
        void OnActivity() => invocations++;
        client.ActivityObserved += OnActivity;
        client.ActivityObserved -= OnActivity;

        client.NotifyActivity();

        Assert.Equal(0, invocations);
    }

    [Fact]
    public void NotifyActivity_NoSubscribers_DoesNotThrow()
    {
        var client = new Client(1);

        client.NotifyActivity();
    }
}
