using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

// The approval question's answer rules, and the switch between a read-only session and one that allows changes.
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

    // ---- The question and its answer ----

    [Fact]
    public async Task Apply_AnswersYes()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel("Change D066 owncost 0.41 -> 0.43");

        request.ApplyCommand.Execute(null);

        Assert.True(await request.Decision);
        Assert.True(request.IsDecided);
    }

    [Fact]
    public async Task Cancel_AnswersNo()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel("anything");

        request.CancelCommand.Execute(null);

        Assert.False(await request.Decision);
    }

    [Fact]
    public async Task ClosingTheWindowWithoutAnswering_IsNo()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel("anything");

        request.CloseWithoutAnswer();

        Assert.False(await request.Decision);
    }

    [Fact]
    public async Task TheFirstAnswerWins_ACancelCanNeverBecomeAYes()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel("anything");

        request.CancelCommand.Execute(null);
        request.ApplyCommand.Execute(null);
        request.CloseWithoutAnswer();

        Assert.False(await request.Decision);
    }

    [Fact]
    public async Task TheFirstAnswerWins_AnApplyIsNotUndoneByTheWindowClosingAfterwards()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel("anything");

        request.ApplyCommand.Execute(null);
        request.CloseWithoutAnswer();
        request.CancelCommand.Execute(null);

        Assert.True(await request.Decision);
    }

    [Fact]
    public void NoAnswerYet_MeansNotDecidedAndNotApproved()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel("anything");

        Assert.False(request.IsDecided);
        Assert.False(request.Decision.IsCompleted);
    }

    [Fact]
    public void TheMessageIsKeptExactlyAsTheServerSentIt()
    {
        string preview = "Ingredient D066 (Urea)\n  owncost (cost per weight unit): 0.41 -> 0.43\n\nApply this change?";

        Assert.Equal(preview, new ApprovalRequestViewModel(preview).Message);
    }

    // ---- Switching between read-only and changes-allowed ----

    private sealed class ModeConnector : IChatConnector
    {
        private readonly bool _allowChanges;
        private readonly Action<bool> _onConnect;
        private readonly Func<ValueTask> _onDispose;
        private readonly Func<bool> _fail;

        public ModeConnector(bool allowChanges, Action<bool> onConnect, Func<ValueTask> onDispose, Func<bool> fail)
        {
            _allowChanges = allowChanges;
            _onConnect = onConnect;
            _onDispose = onDispose;
            _fail = fail;
        }

        public Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            _onConnect(_allowChanges);
            if (_fail())
            {
                throw new InvalidOperationException("the server would not start");
            }

            ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);
            string description = _allowChanges ? "changes allowed" : "read-only";
            return Task.FromResult(new ChatConnection(session, description, Array.Empty<AITool>(), new ServerLogBuffer(), _onDispose));
        }
    }

    private sealed class ModeLog
    {
        public List<bool> Connected { get; } = new List<bool>();

        public int Disposed { get; set; }

        public bool FailNextWriteConnect { get; set; }

        public Func<bool, IChatConnector> Factory =>
            allowChanges => new ModeConnector(
                allowChanges,
                Connected.Add,
                () =>
                {
                    Disposed++;
                    return ValueTask.CompletedTask;
                },
                fail: () => allowChanges && FailNextWriteConnect);
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
    public async Task TurningChangesOn_ReconnectsInTheOtherModeAndStartsFresh()
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

        ChatMessageViewModel notice = Assert.Single(viewModel.Messages);
        Assert.Equal(ChatAuthor.Notice, notice.Author);
        Assert.Contains("new conversation", notice.Text);
        Assert.Contains("approve", notice.Text);
    }

    [Fact]
    public async Task TurningChangesBackOff_ReturnsToReadOnly()
    {
        (ChatViewModel viewModel, ModeLog log) = await ConnectedReadOnlyAsync();
        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        Assert.False(viewModel.AllowChanges);
        Assert.Equal("READ-ONLY", viewModel.ModeBadge);
        Assert.Equal(new[] { false, true, false }, log.Connected);
        Assert.Equal(2, log.Disposed);
        Assert.Contains("read-only", Assert.Single(viewModel.Messages).Text);
    }

    [Fact]
    public async Task IfTheWritableConnectionFails_TheAppIsNotConnectedAndRetryWorks()
    {
        (ChatViewModel viewModel, ModeLog log) = await ConnectedReadOnlyAsync();
        log.FailNextWriteConnect = true;

        await viewModel.ToggleChangesCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConnected);
        Assert.Contains(viewModel.Messages, m => m.Text.Contains("the server would not start"));
        Assert.False(viewModel.SendCommand.CanExecute(null));

        log.FailNextWriteConnect = false;
        await viewModel.RetryConnectionCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("changes allowed", viewModel.ConnectionStatus);
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
