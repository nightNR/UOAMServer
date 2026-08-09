using UOAMServer.Domain;
using UOAMServer.Server.Handlers;
using Xunit;

namespace UOAMServer.Tests.Server.Handlers;

public class ChatPollHandlerTests
{
    [Fact]
    public void Handle_EmptyQueue_ReturnsNoChat44Bytes()
    {
        var client = new Client(1);

        var response = ChatPollHandler.Handle(client, sequence: 1);

        Assert.Equal(44, response.Length);
        Assert.Equal(0x02, response[2]);
        Assert.Equal(1u, ReadUInt32LE(response, 20)); // marker20 = 1 -> NoChat
    }

    [Fact]
    public void Handle_QueuedChat_DequeuesAndReturnsIt()
    {
        var client = new Client(1);
        // Queued payload is the response body from offset 24 onward (subtype,
        // 0x00020000 constant, NDR sender, NDR text) — see ChatSendHandler (Task 11).
        var queuedBody = new byte[] { 1, 0, 0, 0, 0, 0, 2, 0 };
        client.ChatQueue.Enqueue(queuedBody);

        var response = ChatPollHandler.Handle(client, sequence: 9);

        Assert.Empty(client.ChatQueue);
        Assert.Equal(0x02, response[2]);
        Assert.Equal(24 + queuedBody.Length, response.Length);
        Assert.Equal(9u, ReadUInt32LE(response, 12));
    }

    [Fact]
    public void Handle_MultipleQueuedEntries_DequeuesOnlyOnePerCall()
    {
        var client = new Client(1);
        client.ChatQueue.Enqueue(new byte[] { 1 });
        client.ChatQueue.Enqueue(new byte[] { 2 });

        ChatPollHandler.Handle(client, sequence: 1);

        Assert.Single(client.ChatQueue);
    }

    private static uint ReadUInt32LE(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
}
