using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

// The switch between a read-only session and one that allows changes.
public class ApprovalAndModeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cfcagent-mode-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // ---- Switching between read-only and changes-allowed ----

    private sealed class ModeConnector : IChatConnector
    {
        private readonly bool _allowChanges;
        private readonly Action<bool> _onConnect;
        private readonly Func<ValueTask> _onDispose;
        private readonly Func<bool> _fail;
        private readonly Action<ChatSession> _onSession;

        public ModeConnector(bool allowChanges, Action<bool> onConnect, Func<ValueTask> onDispose, Func<bool> fail, Action<ChatSession> onSession)
        {
            _allowChanges = allowChanges;
            _onConnect = onConnect;
            _onDispose = onDispose;
            _fail = fail;
            _onSession = onSession;
        }

        public Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            _onConnect(_allowChanges);
            if (_fail())
            {
                throw new InvalidOperationException("the server would not start");
            }

            // Each mode has its own system prompt, as the real connector's sessions do.
            ChatSession session = new ChatSession(
                new StubChatClient(),
                Array.Empty<AITool>(),
                _allowChanges ? "system prompt: changes allowed" : "system prompt: read-only");
            _onSession(session);
            string description = _allowChanges ? "changes allowed" : "read-only";
            return Task.FromResult(new ChatConnection(session, description, Array.Empty<AITool>(), new ServerLogBuffer(), _onDispose));
        }
    }

    private sealed class ModeLog
    {
        public List<bool> Connected { get; } = new List<bool>();

        public int Disposed { get; set; }

        public bool FailNextWriteConnect { get; set; }

        // Every session a connection produced, oldest first.
        public List<ChatSession> Sessions { get; } = new List<ChatSession>();

        public Func<bool, IChatConnector> Factory =>
            allowChanges => new ModeConnector(
                allowChanges,
                Connected.Add,
                () =>
                {
                    Disposed++;
                    return ValueTask.CompletedTask;
                },
                fail: () => allowChanges && FailNextWriteConnect,
                onSession: Sessions.Add);
    }

    // What a session would send to the model, as "role: text" lines.
    private static List<string> Turns(ChatSession session)
    {
        return session.History.Select(message => message.Role.Value + ": " + message.Text).ToList();
    }

    private static async Task<(ChatViewModel ViewModel, ModeLog Log)> ConnectedReadOnlyAsync()
    {
        ModeLog log = new ModeLog();
        ChatViewModel viewModel = new ChatViewModel();
        viewModel.EnableModeSwitch(log.Factory);
        await viewModel.ConnectAsync(log.Factory(false));
        return (viewModel, log);
    }

    [Fact]
    public void EveryNewViewModelStartsReadOnly()
    {
        ChatViewModel viewModel = new ChatViewModel();

        Assert.False(viewModel.AllowChanges);
        Assert.Equal("READ-ONLY", viewModel.ModeBadge);
        Assert.Equal("Allow changes", viewModel.ToggleChangesLabel);
    }

    [Fact]
    public void WithoutAModeSwitch_ThereIsNothingToToggle()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);

        Assert.False(viewModel.CanSwitchMode);
        Assert.False(viewModel.ToggleChangesCommand.CanExecute(null));
    }

    [Fact]
    public async Task TurningChangesOn_ReconnectsInTheOtherModeAndKeepsTheConversation()
    {
        (ChatViewModel viewModel, ModeLog log) = await ConnectedReadOnlyAsync();
        viewModel.Input = "hello";
        await viewModel.SendCommand.ExecuteAsync(null);
        Assert.Equal(2, viewModel.Messages.Count);

        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        Assert.True(viewModel.AllowChanges);
        Assert.Equal("CHANGES ALLOWED", viewModel.ModeBadge);
        Assert.Equal("Back to read-only", viewModel.ToggleChangesLabel);
        Assert.Equal(new[] { false, true }, log.Connected);
        Assert.Equal(1, log.Disposed);
        Assert.Equal("changes allowed", viewModel.ConnectionStatus);
        Assert.True(viewModel.IsConnected);

        // On screen: the earlier question and reply are still there, with the notice after them.
        Assert.Equal(3, viewModel.Messages.Count);
        Assert.Equal("hello", viewModel.Messages[0].Text);
        Assert.Equal(ChatAuthor.Assistant, viewModel.Messages[1].Author);
        ChatMessageViewModel notice = viewModel.Messages[2];
        Assert.Equal(ChatAuthor.Notice, notice.Author);
        Assert.DoesNotContain("new conversation", notice.Text);
        Assert.Contains("approve", notice.Text);
        Assert.False(notice.IsError, "a change of mode is information, not a failure");

        // For the model: the new session has the new mode's system prompt, the earlier turns, and a note
        // saying what changed.
        List<string> turns = Turns(log.Sessions[1]);
        Assert.Equal(4, turns.Count);
        Assert.Equal("system: system prompt: changes allowed", turns[0]);
        Assert.Equal("user: hello", turns[1]);
        Assert.StartsWith("assistant: ", turns[2]);
        Assert.StartsWith("user: " + ChatSession.AppNotePrefix, turns[3]);
        Assert.Contains("changes allowed", turns[3]);
    }

    [Fact]
    public async Task AfterASwitch_TheNextQuestionIsAskedInTheSameConversation()
    {
        (ChatViewModel viewModel, ModeLog log) = await ConnectedReadOnlyAsync();
        viewModel.Input = "what does D066 cost?";
        await viewModel.SendCommand.ExecuteAsync(null);
        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        viewModel.Input = "now set it to 0.43";
        await viewModel.SendCommand.ExecuteAsync(null);

        List<string> turns = Turns(log.Sessions[1]);
        Assert.Equal("user: what does D066 cost?", turns[1]);
        Assert.Equal("user: now set it to 0.43", turns[4]);
        Assert.StartsWith("assistant: ", turns[5]);
        Assert.Equal(5, viewModel.Messages.Count);
    }

    [Fact]
    public async Task TurningChangesBackOff_ReturnsToReadOnlyAndStillKeepsTheConversation()
    {
        (ChatViewModel viewModel, ModeLog log) = await ConnectedReadOnlyAsync();
        viewModel.Input = "hello";
        await viewModel.SendCommand.ExecuteAsync(null);
        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        Assert.False(viewModel.AllowChanges);
        Assert.Equal("READ-ONLY", viewModel.ModeBadge);
        Assert.Equal(new[] { false, true, false }, log.Connected);
        Assert.Equal(2, log.Disposed);
        Assert.Equal(4, viewModel.Messages.Count);
        Assert.Contains("read-only", viewModel.Messages[3].Text);

        // Nothing was said between the two switches, so only the newest note is kept for the model.
        List<string> turns = Turns(log.Sessions[2]);
        Assert.Equal(4, turns.Count);
        Assert.Equal("system: system prompt: read-only", turns[0]);
        Assert.Equal("user: hello", turns[1]);
        Assert.Contains("read-only mode", turns[3]);
        Assert.DoesNotContain(turns, turn => turn.Contains("with changes allowed"));
    }

    [Fact]
    public async Task ASwitchBeforeAnythingWasSaid_LeavesTheNewSessionWithOnlyItsSystemPrompt()
    {
        (ChatViewModel viewModel, ModeLog log) = await ConnectedReadOnlyAsync();

        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "system: system prompt: changes allowed" }, Turns(log.Sessions[1]));
        Assert.Contains("approve", Assert.Single(viewModel.Messages).Text);
    }

    [Fact]
    public async Task IfTheWritableConnectionFails_TheAppIsNotConnected_AndRetryCarriesOnTheConversation()
    {
        (ChatViewModel viewModel, ModeLog log) = await ConnectedReadOnlyAsync();
        viewModel.Input = "hello";
        await viewModel.SendCommand.ExecuteAsync(null);
        log.FailNextWriteConnect = true;

        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConnected);
        Assert.Contains(viewModel.Messages, m => m.Text.Contains("the server would not start"));
        Assert.False(viewModel.SendCommand.CanExecute(null));
        Assert.Equal("hello", viewModel.Messages[0].Text);

        log.FailNextWriteConnect = false;
        await viewModel.RetryConnectionCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("changes allowed", viewModel.ConnectionStatus);
        List<string> turns = Turns(log.Sessions[^1]);
        Assert.Equal("user: hello", turns[1]);
        Assert.Contains("changes allowed", turns[^1]);
    }

    [Fact]
    public async Task TheSwitchIsOffWhileAReplyIsBeingReceived()
    {
        (ChatViewModel viewModel, ModeLog _) = await ConnectedReadOnlyAsync();

        viewModel.IsBusy = true;

        Assert.False(viewModel.ToggleChangesCommand.CanExecute(null));
    }

    // ---- The real connector in changes-allowed mode ----

    private sealed class InMemoryCredentials : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new Dictionary<string, string>();

        public string? Get(string target) => _secrets.TryGetValue(target, out string? value) ? value : null;

        public void Set(string target, string userName, string secret) => _secrets[target] = secret;

        public bool Delete(string target) => _secrets.Remove(target);
    }

    private LocalBackendOptions WritableOptions(Func<string, CancellationToken, Task<bool>>? confirm)
    {
        InMemoryCredentials credentials = new InMemoryCredentials();
        string tenantConfig = Path.Combine(_folder, "tenant.json");
        new TenantSettings(tenantConfig, credentials).Save(
            new TenantConfig { TenantName = "mode-test" },
            "Host=127.0.0.1;Port=1;Database=nothing;Username=nobody;Password=nothing;Timeout=1");
        return new LocalBackendOptions
        {
            ReadOnly = false,
            TenantConfigPath = tenantConfig,
            Credentials = credentials,
            ForceStubModel = true,
            ConfirmAsync = confirm,
        };
    }

    [Fact]
    public async Task ChangesAllowedWithAnApprovalPrompt_OffersWriteToolsAndSaysEachNeedsApproval()
    {
        LocalBackendOptions options = WritableOptions((_, _) => Task.FromResult(false));

        await using ChatConnection connection = await new LocalBackendConnector(options).ConnectAsync(CancellationToken.None);

        Assert.Contains(connection.Tools, tool => tool.Name == "update_ingredient");
        Assert.Contains("changes allowed (each one needs your approval)", connection.Description);
    }

    [Fact]
    public async Task ChangesAllowedButNoApprovalPrompt_SaysEveryChangeWillBeRefused()
    {
        LocalBackendOptions options = WritableOptions(null);

        await using ChatConnection connection = await new LocalBackendConnector(options).ConnectAsync(CancellationToken.None);

        Assert.Contains("every change will be refused", connection.Description);
    }
}
