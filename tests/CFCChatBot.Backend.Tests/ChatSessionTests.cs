using CFCChatBot.Backend;
using Microsoft.Extensions.AI;

namespace CFCChatBot.Backend.Tests;

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
}
