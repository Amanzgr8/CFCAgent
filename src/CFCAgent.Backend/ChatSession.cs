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

    // Marks a message the app wrote into the conversation for the model to read (see ContinueFrom). It goes
    // in as a user message, so the marker tells the model the user did not type it.
    public const string AppNotePrefix = "[Notice from the app, not written by the user] ";

    // Takes over the conversation of an earlier session, so a new connection can carry on where the old one
    // stopped (the app does this when it restarts the server to switch between read-only and changes-allowed).
    // Every earlier turn is kept. The earlier system prompt is not: this session keeps its own, which matches
    // the tools it was given. noteForModel, if any, is added after the earlier turns to tell the model what
    // changed. Only possible before the first message is sent.
    public void ContinueFrom(IEnumerable<ChatMessage> earlierHistory, string? noteForModel = null)
    {
        if (_history.Any(message => message.Role != ChatRole.System))
        {
            throw new InvalidOperationException("A conversation can only be taken over before the first message is sent.");
        }

        List<ChatMessage> turns = earlierHistory.Where(message => message.Role != ChatRole.System).ToList();

        // A note left by an earlier switch that no turn followed says nothing any more: only the newest counts.
        while (turns.Count > 0 && IsAppNote(turns[^1]))
        {
            turns.RemoveAt(turns.Count - 1);
        }

        _history.AddRange(turns);

        // With no earlier turns there is nothing to explain: the system prompt already describes this session.
        if (turns.Count > 0 && !string.IsNullOrWhiteSpace(noteForModel))
        {
            _history.Add(new ChatMessage(ChatRole.User, AppNotePrefix + noteForModel));
        }
    }

    // Forgets every turn and keeps the system prompt, so the same connection can start a new conversation, or
    // take over a saved one with ContinueFrom, without restarting the server. Not to be called while a reply
    // is arriving.
    public void Clear()
    {
        _history.RemoveAll(message => message.Role != ChatRole.System);
    }

    private static bool IsAppNote(ChatMessage message)
    {
        return message.Role == ChatRole.User && message.Text.StartsWith(AppNotePrefix, StringComparison.Ordinal);
    }

    // Sends one message and yields the model's reply piece by piece as it arrives, including any tool
    // calls and their results, so a caller can show text growing and tool activity live. This is the
    // only way to send a message: a caller that just wants the whole reply joins the pieces' Text.
    // The whole history goes to the model on every request. A turn is added to the history only once
    // the reply has run to the end. A reply that is cancelled, fails, or is abandoned part-way leaves
    // the history as it was, because a half-finished tool call left in it would make the next request
    // fail.
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
