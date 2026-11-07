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
}
