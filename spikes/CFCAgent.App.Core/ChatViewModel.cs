using System.Collections.ObjectModel;
using CFCAgent.Backend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core;

// Everything the chat window does, with no UI in it: the list of messages, the text being typed, and
// the Send and Stop commands. Sending shows the user's message at once, then fills an assistant
// message piece by piece as the reply streams in. Stop cancels the reply being received. A failure
// is shown as a notice in the conversation instead of crashing the app.
public sealed partial class ChatViewModel : ObservableObject, IAsyncDisposable
{
    private ChatSession? _session;
    private ChatConnection? _connection;
    private IChatConnector? _connector;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isBusy;

    // Sending is only possible once a backend is connected.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryConnectionCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryConnectionCommand))]
    private bool _isConnecting;

    // One line about the connection, shown in the window: what it is connected to, or why it is not.
    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    // A window that connects after it opens: use ConnectAsync to attach a backend.
    public ChatViewModel()
    {
    }

    // A window that is already connected to a session (used by the demo and the tests).
    public ChatViewModel(ChatSession session, string connectionStatus)
    {
        _session = session;
        _isConnected = true;
        _connectionStatus = connectionStatus;
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new ObservableCollection<ChatMessageViewModel>();

    // Shown in the window footer, to make it obvious what the app is actually running on.
    public string RuntimeInfo => RuntimeSummary.Describe();

    // A conversation with the stub model, which only echoes. It needs no database, server or API key,
    // so the window can be developed and tested on its own.
    public static ChatViewModel CreateDemo(TimeSpan streamDelay)
    {
        ChatSession session = new ChatSession(new StubChatClient(streamDelay), Array.Empty<AITool>(), null);
        return new ChatViewModel(session, "Demo: stub model, no database");
    }

    // Connects to a backend. A failure does not crash the app: it is shown as a notice in the conversation,
    // the status line says "Not connected", and Retry can try again (for example after fixing the setup).
    public async Task ConnectAsync(IChatConnector connector, CancellationToken cancellationToken = default)
    {
        _connector = connector;
        IsConnecting = true;
        ConnectionStatus = "Connecting...";
        try
        {
            ChatConnection connection = await connector.ConnectAsync(cancellationToken);
            _connection = connection;
            _session = connection.Session;
            ConnectionStatus = connection.Description;
            IsConnected = true;
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = "Not connected (cancelled)";
        }
        catch (Exception ex)
        {
            ConnectionStatus = "Not connected";
            Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, $"Could not connect: {ex.Message}"));
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanRetryConnection()
    {
        return !IsConnected && !IsConnecting && _connector is not null;
    }

    [RelayCommand(CanExecute = nameof(CanRetryConnection))]
    private Task RetryConnectionAsync()
    {
        return ConnectAsync(_connector!);
    }

    // Shuts the backend down (ends the MCP server process). Safe to call more than once.
    public async ValueTask DisposeAsync()
    {
        ChatConnection? connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    private bool CanSend()
    {
        return IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(Input);
    }

    [RelayCommand(CanExecute = nameof(CanSend), IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        string text = Input.Trim();
        Input = "";
        IsBusy = true;

        Messages.Add(new ChatMessageViewModel(ChatAuthor.User, text));
        ChatMessageViewModel reply = new ChatMessageViewModel(ChatAuthor.Assistant, "");
        reply.IsStreaming = true;
        Messages.Add(reply);

        try
        {
            await foreach (ChatResponseUpdate update in _session!.SendStreamingAsync(text, cancellationToken))
            {
                reply.Text += update.Text;
            }
        }
        catch (OperationCanceledException)
        {
            reply.Text += reply.Text.Length > 0 ? "\n\n(stopped)" : "(stopped)";
        }
        catch (Exception ex)
        {
            if (reply.Text.Length == 0)
            {
                Messages.Remove(reply);
            }

            Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, $"Something went wrong: {ex.Message}"));
        }
        finally
        {
            reply.IsStreaming = false;
            IsBusy = false;
        }
    }
}
