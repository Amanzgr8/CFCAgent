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

    // ---- Taking over an earlier conversation ----

    [Fact]
    public async Task ContinueFrom_KeepsTheEarlierTurns_ButThisSessionsOwnSystemPrompt()
    {
        ChatSession earlier = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "read-only prompt");
        await ReplyAsync(earlier, "what does D066 cost?");
        ChatSession next = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "changes-allowed prompt");

        next.ContinueFrom(earlier.History, "changes are now allowed");

        Assert.Equal(4, next.History.Count);
        Assert.Equal(ChatRole.System, next.History[0].Role);
        Assert.Equal("changes-allowed prompt", next.History[0].Text);
        Assert.Equal("what does D066 cost?", next.History[1].Text);
        Assert.Equal(ChatRole.Assistant, next.History[2].Role);
        Assert.Equal(ChatRole.User, next.History[3].Role);
        Assert.Equal(ChatSession.AppNotePrefix + "changes are now allowed", next.History[3].Text);
        Assert.DoesNotContain(next.History, message => message.Text == "read-only prompt");
    }

    [Fact]
    public async Task ContinueFrom_ThenSending_CarriesOnTheSameConversation()
    {
        ChatSession earlier = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "first prompt");
        await ReplyAsync(earlier, "first question");
        ChatSession next = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "second prompt");
        next.ContinueFrom(earlier.History, "the mode changed");

        string reply = await ReplyAsync(next, "second question");

        Assert.Contains("second question", reply);
        Assert.Equal(6, next.History.Count);
        Assert.Equal("first question", next.History[1].Text);
        Assert.Equal("second question", next.History[4].Text);
    }

    [Fact]
    public void ContinueFrom_WithNoEarlierTurns_AddsNothing()
    {
        ChatSession earlier = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "first prompt");
        ChatSession next = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "second prompt");

        next.ContinueFrom(earlier.History, "the mode changed");

        // Nothing was said yet, so there is nothing to explain: the system prompt already describes the session.
        ChatMessage only = Assert.Single(next.History);
        Assert.Equal("second prompt", only.Text);
    }

    [Fact]
    public async Task ContinueFrom_DropsANoteThatNoTurnFollowed_SoOnlyTheNewestNoteIsKept()
    {
        ChatSession first = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);
        await ReplyAsync(first, "hello");
        ChatSession second = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);
        second.ContinueFrom(first.History, "changes are now allowed");
        ChatSession third = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);

        third.ContinueFrom(second.History, "back to read-only");

        Assert.Equal(3, third.History.Count);
        Assert.Equal("hello", third.History[0].Text);
        Assert.Equal(ChatSession.AppNotePrefix + "back to read-only", third.History[2].Text);
    }

    [Fact]
    public async Task ContinueFrom_AfterAMessageWasSent_IsRefused()
    {
        ChatSession earlier = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);
        await ReplyAsync(earlier, "an earlier question");
        ChatSession next = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "system text");
        await ReplyAsync(next, "already talking");

        Assert.Throws<InvalidOperationException>(() => next.ContinueFrom(earlier.History));
        Assert.Equal(3, next.History.Count);
    }

    [Fact]
    public async Task Clear_ForgetsTheTurns_AndKeepsTheSystemPrompt()
    {
        ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "the prompt");
        await ReplyAsync(session, "what does D066 cost?");

        session.Clear();

        ChatMessage only = Assert.Single(session.History);
        Assert.Equal(ChatRole.System, only.Role);
        Assert.Equal("the prompt", only.Text);

        // The next question starts from nothing: the model is not sent the earlier one.
        await ReplyAsync(session, "list the binders");
        Assert.Equal(3, session.History.Count);
        Assert.DoesNotContain(session.History, message => message.Text.Contains("D066"));
    }

    [Fact]
    public async Task Clear_MakesRoomToTakeOverAnotherConversation()
    {
        ChatSession saved = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);
        await ReplyAsync(saved, "an earlier chat");
        ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), "the prompt");
        await ReplyAsync(session, "the chat that was open");

        session.Clear();
        session.ContinueFrom(saved.History, "this chat was reopened");

        Assert.Equal(4, session.History.Count);
        Assert.Equal("the prompt", session.History[0].Text);
        Assert.Equal("an earlier chat", session.History[1].Text);
        Assert.Equal(ChatSession.AppNotePrefix + "this chat was reopened", session.History[3].Text);
    }
}
