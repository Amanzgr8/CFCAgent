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
    [NotifyCanExecuteChangedFor(nameof(ToggleChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    private bool _isBusy;

    // Sending is only possible once a backend is connected.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryConnectionCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    private bool _isConnecting;

    // Whether this session may change data. Always false at startup: every launch is read-only until the
    // user turns changes on, and even then each change needs its own approval.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeBadge))]
    [NotifyPropertyChangedFor(nameof(ToggleChangesLabel))]
    private bool _allowChanges;

    private Func<bool, IChatConnector>? _connectorForMode;

    // The approval card waiting for an answer, if any. The window uses it to let Esc cancel the question.
    [ObservableProperty]
    private ApprovalRequestViewModel? _pendingApproval;

    // Set when a card has been added: text that arrives after it starts a new reply bubble below the card,
    // so the conversation reads in the order things happened (text, question, answer, more text).
    private bool _startNewReplyBubble;

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
            Messages.Add(new ChatMessageViewModel(
                ChatAuthor.Notice,
                Settings is null ? $"Could not connect: {ex.Message}" : $"Could not connect: {ex.Message} Open Settings to check the connection."));

            // First run: there is nothing saved yet, so take the user straight to the place to enter it.
            if (Settings is { IsOpen: false } settings && settings.NeedsSetup())
            {
                settings.Open("First-time setup: enter your database connection to get started.");
            }
        }
        finally
        {
            IsConnecting = false;
        }
    }

    public string ModeBadge => AllowChanges ? "CHANGES ALLOWED" : "READ-ONLY";

    public string ToggleChangesLabel => AllowChanges ? "Back to read-only" : "Allow changes";

    // Whether the window should offer the read-only / changes switch at all (a demo has nothing to switch).
    public bool CanSwitchMode => _connectorForMode is not null;

    // Gives the view-model what it needs to switch between a read-only session and one that allows changes:
    // a way to make a connector for either mode. Without this the switch is not offered.
    public void EnableModeSwitch(Func<bool, IChatConnector> connectorForMode)
    {
        _connectorForMode = connectorForMode;
        OnPropertyChanged(nameof(CanSwitchMode));
        ToggleChangesCommand.NotifyCanExecuteChanged();
    }

    private bool CanToggleChanges()
    {
        return _connectorForMode is not null && !IsBusy && !IsConnecting;
    }

    // Switches the session between read-only and changes-allowed. The tool list and the confirmation
    // handshake are fixed when a session connects, so this ends the current server process and starts a
    // fresh connection and a fresh conversation (the model would otherwise remember the old mode's tools).
    [RelayCommand(CanExecute = nameof(CanToggleChanges))]
    private Task ToggleChangesAsync()
    {
        bool target = !AllowChanges;
        return RestartAsync(
            target,
            target
                ? "Changes are now allowed. The assistant can propose a change, and nothing is applied unless you approve it on the approval card. This started a new conversation."
                : "Back to read-only. The assistant can no longer change anything. This started a new conversation.");
    }

    // Ends the current connection and starts a fresh one (and a fresh conversation) in the given mode.
    private async Task RestartAsync(bool allowChanges, string notice)
    {
        IChatConnector connector = _connectorForMode!(allowChanges);

        await DisposeAsync();
        _session = null;
        IsConnected = false;
        Messages.Clear();
        AllowChanges = allowChanges;
        Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, notice));

        await ConnectAsync(connector);
    }

    // ---- Settings ----

    // The settings screen, once the app has given one to the window. Without it there is no Settings button.
    public SettingsViewModel? Settings { get; private set; }

    public bool HasSettings => Settings is not null;

    public void AttachSettings(SettingsViewModel settings)
    {
        Settings = settings;
        settings.Saved += (_, _) => _ = ReconnectAfterSettingsAsync();
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(HasSettings));
        OpenSettingsCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenSettings()
    {
        return Settings is not null && !IsBusy && !IsConnecting;
    }

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private void OpenSettings()
    {
        Settings!.Open();
    }

    // New settings may point at a different database, so the connection is rebuilt in read-only mode, whatever
    // the mode was before. Changes have to be allowed again, on purpose, for the new database.
    private async Task ReconnectAfterSettingsAsync()
    {
        if (_connectorForMode is null)
        {
            return;
        }

        await RestartAsync(false, "Settings saved. Reconnecting in read-only mode. This started a new conversation.");
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

    // Puts an "apply this change?" card in the conversation. Called on the UI thread by ChatApprovalPresenter.
    public void ShowApproval(ApprovalRequestViewModel request)
    {
        Messages.Add(new ChatMessageViewModel(request));
        PendingApproval = request;
        _startNewReplyBubble = true;

        // Once the question is answered or withdrawn, nothing is pending any more. The card stays as a record.
        request.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ApprovalRequestViewModel.State) && !request.IsPending && ReferenceEquals(PendingApproval, request))
            {
                PendingApproval = null;
            }
        };
    }

    // Shuts the backend down (ends the MCP server process). Safe to call more than once.
    public async ValueTask DisposeAsync()
    {
        // A question that is still open can never be answered once the connection is gone.
        PendingApproval?.Withdraw();

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

        _startNewReplyBubble = false;

        try
        {
            await foreach (ChatResponseUpdate update in _session!.SendStreamingAsync(text, cancellationToken))
            {
                if (_startNewReplyBubble && update.Text.Length > 0)
                {
                    // An approval card was added while this reply was in progress: carry on below it.
                    reply.IsStreaming = false;
                    if (reply.Text.Length == 0)
                    {
                        Messages.Remove(reply);
                    }

                    reply = new ChatMessageViewModel(ChatAuthor.Assistant, "") { IsStreaming = true };
                    Messages.Add(reply);
                    _startNewReplyBubble = false;
                }

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
            // The reply is over, so a question still open can no longer matter: withdraw it (nothing is changed).
            PendingApproval?.Withdraw();
            reply.IsStreaming = false;
            if (reply.Text.Length == 0)
            {
                Messages.Remove(reply);
            }

            IsBusy = false;
        }
    }
}
