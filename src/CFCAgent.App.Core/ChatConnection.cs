using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core;

// A live link to a backend: the chat session to talk through, a one-line description for the window
// ("tenant 'x', 16 tools, read-only, model ..."), and the means to shut it down. Disposing it ends the
// MCP server child process, so the window disposes it when the app closes.
public sealed class ChatConnection : IAsyncDisposable
{
    private readonly Func<ValueTask> _dispose;
    private int _disposed;

    public ChatConnection(ChatSession session, string description, IReadOnlyList<AITool> tools, ServerLogBuffer serverLog, Func<ValueTask> dispose)
    {
        Session = session;
        Description = description;
        Tools = tools;
        ServerLog = serverLog;
        _dispose = dispose;
    }

    public ChatSession Session { get; }

    public string Description { get; }

    // The tools the model is offered (after any read-only filtering).
    public IReadOnlyList<AITool> Tools { get; }

    // The server's important log lines (audit lines, warnings, errors).
    public ServerLogBuffer ServerLog { get; }

    public ValueTask DisposeAsync()
    {
        // Safe to call more than once: only the first call shuts anything down.
        return Interlocked.Exchange(ref _disposed, 1) == 0 ? _dispose() : ValueTask.CompletedTask;
    }
}

// Where a window gets its connection from. The real one starts the MCP server; tests supply fakes.
public interface IChatConnector
{
    Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken);
}

// The server's stderr lines, kept on a background thread's behalf: the MCP client delivers them from
// its own thread, so every access takes a lock. Keeps the most recent lines only.
public sealed class ServerLogBuffer
{
    private const int MaxLines = 200;
    private readonly object _gate = new object();
    private readonly List<string> _important = new List<string>();
    private readonly Queue<string> _recentRaw = new Queue<string>();

    // Every line goes in; the important ones (audit, warn, fail, crit) are kept for display, and the last
    // few of any kind are kept so a failed start can say what the server printed.
    public void Add(string line)
    {
        lock (_gate)
        {
            _recentRaw.Enqueue(line);
            while (_recentRaw.Count > 10)
            {
                _recentRaw.Dequeue();
            }

            if (ServerLogFilter.IsImportant(line))
            {
                _important.Add(line);
                if (_important.Count > MaxLines)
                {
                    _important.RemoveAt(0);
                }
            }
        }
    }

    public IReadOnlyList<string> ImportantLines()
    {
        lock (_gate)
        {
            return _important.ToList();
        }
    }

    public IReadOnlyList<string> RecentLines(int count)
    {
        lock (_gate)
        {
            return _recentRaw.TakeLast(count).ToList();
        }
    }
}
