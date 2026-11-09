using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.Backend.Tests;

public class ChatSessionTests
{
    [Fact]
    public async Task SendAsync_KeepsSystemPromptAndHistoryAcrossTurns()
    {
        ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "system text");

        string first = await session.SendAsync("hello");
        await session.SendAsync("again");

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

        await session.SendAsync("second");

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
}
