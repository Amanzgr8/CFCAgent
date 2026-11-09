using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace CFCAgent.Backend;

// Keeps the conversation history and runs one turn at a time against any model client.
// The client is wrapped in FunctionInvokingChatClient, which runs the tool loop: the model
// asks for a tool, the tool runs, and the result goes back to the model. The server's
// instructions are used as the system prompt.
public sealed class ChatSession
{
    private readonly IChatClient _client;
    private readonly ChatOptions _options;
    private readonly List<ChatMessage> _history = new List<ChatMessage>();

    public ChatSession(IChatClient chatClient, IReadOnlyList<AITool> tools, string? systemPrompt)
    {
        _client = new FunctionInvokingChatClient(chatClient);
        _options = new ChatOptions { Tools = tools.ToList() };
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            _history.Add(new ChatMessage(ChatRole.System, systemPrompt));
        }
    }

    public IReadOnlyList<ChatMessage> History => _history;

    public async Task<string> SendAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        _history.Add(new ChatMessage(ChatRole.User, userMessage));
        ChatResponse response = await _client.GetResponseAsync(_history, _options, cancellationToken);
        _history.AddMessages(response);
        return response.Text;
    }

    // Streaming version of SendAsync: yields the model's reply piece by piece as it arrives, including
    // any tool calls and their results, so a UI can show text growing and tool activity live. A turn
    // is added to the history only once the reply has run to the end. A reply that is cancelled,
    // fails, or is abandoned part-way leaves the history as it was, because a half-finished tool call
    // left in it would make the next request fail.
    public async IAsyncEnumerable<ChatResponseUpdate> SendStreamingAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessage userChatMessage = new ChatMessage(ChatRole.User, userMessage);
        List<ChatMessage> request = new List<ChatMessage>(_history) { userChatMessage };
        List<ChatResponseUpdate> updates = new List<ChatResponseUpdate>();

        await foreach (ChatResponseUpdate update in _client.GetStreamingResponseAsync(request, _options, cancellationToken))
        {
            updates.Add(update);
            yield return update;
        }

        ChatResponse response = updates.ToChatResponse();
        _history.Add(userChatMessage);
        _history.AddMessages(response);
    }
}
