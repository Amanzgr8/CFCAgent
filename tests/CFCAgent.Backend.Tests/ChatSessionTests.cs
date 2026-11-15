using System.Runtime.CompilerServices;
using System.Text;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.Backend.Tests;

public class ChatSessionTests
{
    // Sends one message and returns the whole reply, the way a caller that does not show the pieces would.
    private static async Task<string> ReplyAsync(ChatSession session, string message)
    {
        StringBuilder reply = new StringBuilder();
        await foreach (ChatResponseUpdate update in session.SendStreamingAsync(message))
        {
            reply.Append(update.Text);
        }

        return reply.ToString();
    }

    // A model that gives a few words of a reply and then fails, like a dropped connection.
    private sealed class FailingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("the model is unavailable");
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Half a");
            await Task.Yield();
            throw new InvalidOperationException("the model is unavailable");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task SendStreamingAsync_KeepsSystemPromptAndHistoryAcrossTurns()
    {
        ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "system text");

        string first = await ReplyAsync(session, "hello");
        await ReplyAsync(session, "again");

        Assert.Contains("hello", first);
        Assert.Equal(5, session.History.Count);
        Assert.Equal(ChatRole.System, session.History[0].Role);
        Assert.Equal("system text", session.History[0].Text);
    }

    [Fact]
    public async Task SendStreamingAsync_YieldsTheReplyInPiecesThatJoinToTheWholeReply()
    {
        ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "system text");

        List<ChatResponseUpdate> updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in session.SendStreamingAsync("hello there"))
        {
            updates.Add(update);
        }

        string joined = string.Concat(updates.Select(u => u.Text));
        Assert.True(updates.Count > 1, "expected the reply to arrive in several pieces");
        Assert.Equal("[stub model, no ANTHROPIC_API_KEY] You said: hello there", joined);
    }

    [Fact]
    public async Task SendStreamingAsync_AfterACompleteReply_RecordsTheTurnInTheHistory()
    {
        ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "system text");

        await foreach (ChatResponseUpdate _ in session.SendStreamingAsync("first"))
        {
        }

        await ReplyAsync(session, "second");

        Assert.Equal(5, session.History.Count);
        Assert.Equal(ChatRole.User, session.History[1].Role);
        Assert.Equal("first", session.History[1].Text);
        Assert.Equal(ChatRole.Assistant, session.History[2].Role);
        Assert.Contains("first", session.History[2].Text);
    }

    [Fact]
    public async Task SendStreamingAsync_WhenTheCallerStopsEarly_LeavesTheHistoryUnchanged()
    {
        ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "system text");

        await foreach (ChatResponseUpdate _ in session.SendStreamingAsync("abandoned"))
        {
            break;
        }

        Assert.Single(session.History);
    }

    [Fact]
    public async Task SendStreamingAsync_WhenCancelledMidReply_LeavesTheHistoryUnchanged()
    {
        ChatSession session = new ChatSession(new StubChatClient(TimeSpan.FromMilliseconds(5)), Array.Empty<AITool>(), "system text");
        using CancellationTokenSource cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ChatResponseUpdate _ in session.SendStreamingAsync("cancelled", cancellation.Token))
            {
                cancellation.Cancel();
            }
        });

        Assert.Single(session.History);
    }

    [Fact]
    public async Task SendStreamingAsync_WhenTheModelFailsMidReply_LeavesTheHistoryUnchanged()
    {
        ChatSession session = new ChatSession(new FailingChatClient(), Array.Empty<AITool>(), "system text");

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ReplyAsync(session, "this one fails"));

        Assert.Equal("the model is unavailable", failure.Message);
        Assert.Single(session.History);
    }
}
