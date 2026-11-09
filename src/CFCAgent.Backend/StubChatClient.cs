using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace CFCAgent.Backend;

// Fake model used when there is no ANTHROPIC_API_KEY. It echoes the last user message back, either
// all at once or word by word (with an optional delay between words to imitate a model typing), so the
// tool host, chat session, CLI and any UI can be exercised without calling Claude.
public sealed class StubChatClient : IChatClient
{
    private readonly TimeSpan _streamDelay;

    public StubChatClient()
        : this(TimeSpan.Zero)
    {
    }

    public StubChatClient(TimeSpan streamDelay)
    {
        _streamDelay = streamDelay;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, BuildReply(messages))));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string[] words = BuildReply(messages).Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_streamDelay > TimeSpan.Zero)
            {
                await Task.Delay(_streamDelay, cancellationToken);
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, (i == 0 ? "" : " ") + words[i]);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static string BuildReply(IEnumerable<ChatMessage> messages)
    {
        string lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        return $"[stub model, no ANTHROPIC_API_KEY] You said: {lastUser}";
    }
}
