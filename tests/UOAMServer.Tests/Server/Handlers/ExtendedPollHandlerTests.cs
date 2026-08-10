using UOAMServer.Domain;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class ExtendedPollHandlerTests
{
    [Fact]
    public void Handle_EmptyQueue_Returns24ByteExtendedFamilyResponse()
    {
        var client = new Client(1);

        var response = ExtendedPollHandler.Handle(client, sequence: 1);

        Assert.Equal(24, response.Length);
        Assert.Equal(0x02, response[2]);
        Assert.Equal(2u, ReadUInt32LE(response, 20)); // marker20 = 2 -> extended family
    }

    [Fact]
    public void Handle_QueuedEntry_DequeuesAndAppendsIt()
    {
        var client = new Client(1);
        var queuedBody = new byte[36];
        queuedBody[0] = 0xAB;
        client.ExtendedQueue.Enqueue(queuedBody);

        var response = ExtendedPollHandler.Handle(client, sequence: 9);

        Assert.Empty(client.ExtendedQueue);
        Assert.Equal(0x02, response[2]);
        Assert.Equal(24 + 36, response.Length);
        Assert.Equal(0xAB, response[24]);
        Assert.Equal(9u, ReadUInt32LE(response, 12));
    }

    [Fact]
    public void Handle_MultipleQueuedEntries_DequeuesOnlyOnePerCall()
    {
        var client = new Client(1);
        client.ExtendedQueue.Enqueue(new byte[36]);
        client.ExtendedQueue.Enqueue(new byte[36]);

        ExtendedPollHandler.Handle(client, sequence: 1);

        Assert.Single(client.ExtendedQueue);
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
