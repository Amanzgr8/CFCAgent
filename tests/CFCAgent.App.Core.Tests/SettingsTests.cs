using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

// The settings screen's logic: what it loads, what it saves and where, that the connection string is never shown
// back, and what happens to the chat when settings change. A fake credential store and a fake tester keep these
// away from Credential Manager, the server and any database.
public class SettingsTests : IDisposable
{
    private const string Secret = "Host=db.example;Database=cfc;Username=app;Password=hunter2-SECRET";
    private const string OtherSecret = "Host=other.example;Database=cfc;Username=app;Password=correct-horse-SECRET";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cfcagent-settings-tests-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeCredentials : ICredentialStore
    {
        public Dictionary<string, string> Secrets { get; } = new Dictionary<string, string>();

        public bool FailOnSet { get; set; }

        public string? Get(string target) => Secrets.TryGetValue(target, out string? value) ? value : null;

        public void Set(string target, string userName, string secret)
        {
            if (FailOnSet)
            {
                throw new InvalidOperationException("the credential store is locked, tried to save " + secret);
            }

            Secrets[target] = secret;
        }

        public bool Delete(string target) => Secrets.Remove(target);
    }

    private sealed class FakeTester : IConnectionTester
    {
        public List<(string Connection, string? ProductConfig)> Calls { get; } = new List<(string, string?)>();

        public ConnectionTestResult Result { get; set; } = new ConnectionTestResult(true, "Database connection OK.");

        public TaskCompletionSource<ConnectionTestResult>? Hold { get; set; }

        public async Task<ConnectionTestResult> TestAsync(string connectionString, string? productConfigPath, CancellationToken cancellationToken)
        {
            Calls.Add((connectionString, productConfigPath));
            if (Hold is not null)
            {
                using (cancellationToken.Register(() => Hold.TrySetCanceled(cancellationToken)))
                {
                    return await Hold.Task;
                }
            }

            return Result;
        }
    }

    private readonly FakeCredentials _credentials = new FakeCredentials();
    private readonly FakeTester _tester = new FakeTester();

    private string ConfigPath => Path.Combine(_folder, "tenant.json");

    private TenantSettings NewTenantSettings() => new TenantSettings(ConfigPath, _credentials);

    private SettingsViewModel NewSettings()
    {
        return new SettingsViewModel(NewTenantSettings(), _tester, @"C:\products\default-schema-map.yaml", "Test Credential Store");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private void SavedSetup(string tenant = "acme", string secret = Secret, string? productConfig = null)
    {
        NewTenantSettings().Save(new TenantConfig { TenantName = tenant, ProductConfigPath = productConfig }, secret);
    }

    // ---- Opening ----

    [Fact]
    public void Open_WithNothingSaved_ShowsDefaultsAndNeedsSetup()
    {
        SettingsViewModel settings = NewSettings();

        Assert.True(settings.NeedsSetup());
        settings.Open();

        Assert.True(settings.IsOpen);
        Assert.Equal("default", settings.TenantName);
        Assert.Equal("", settings.ProductConfigPath);
        Assert.False(settings.HasStoredConnection);
        Assert.Contains("Paste", settings.SecretHint);
    }

    [Fact]
    public void Open_WithASavedSetup_LoadsTheNameAndConfig_ButNeverTheConnectionString()
    {
        SavedSetup("acme", productConfig: @"C:\products\acme.yaml");
        SettingsViewModel settings = NewSettings();

        Assert.False(settings.NeedsSetup());
        settings.Open();

        Assert.Equal("acme", settings.TenantName);
        Assert.Equal(@"C:\products\acme.yaml", settings.ProductConfigPath);
        Assert.True(settings.HasStoredConnection);
        Assert.Equal("", settings.ConnectionString);
        Assert.Contains("Leave this blank to keep it", settings.SecretHint);
        Assert.DoesNotContain("hunter2", settings.SecretHint + settings.StatusMessage + settings.TenantName + settings.ProductConfigPath);
    }

    [Fact]
    public void Open_WithAConfigFileThatCannotBeRead_SaysSoAndStillOpens()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(ConfigPath, "{ \"tenantName\": \"\" }");
        SettingsViewModel settings = NewSettings();

        settings.Open();

        Assert.True(settings.IsOpen);
        Assert.True(settings.StatusIsError);
        Assert.Contains("could not be read", settings.StatusMessage);
    }

    // ---- Saving ----

    [Fact]
    public void Save_StoresTheSecretInTheCredentialStore_AndNotInTheConfigFile()
    {
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.TenantName = "acme";
        settings.ConnectionString = Secret;
        int saved = 0;
        settings.Saved += (_, _) => saved++;

        settings.SaveCommand.Execute(null);

        Assert.Equal(Secret, _credentials.Get(TenantSettings.CredentialTarget("acme")));
        Assert.DoesNotContain("hunter2", File.ReadAllText(ConfigPath));
        Assert.Equal("acme", NewTenantSettings().Load()!.TenantName);
        Assert.Equal(1, saved);
        Assert.False(settings.IsOpen);
        Assert.Equal("", settings.ConnectionString);
        Assert.True(settings.HasStoredConnection);
        Assert.Contains("Test Credential Store", settings.StatusMessage);
        Assert.DoesNotContain("hunter2", settings.StatusMessage);
    }

    [Fact]
    public void Save_SavesABlankProductConfigAsNone()
    {
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.ConnectionString = Secret;
        settings.ProductConfigPath = "   ";

        settings.SaveCommand.Execute(null);

        Assert.Null(NewTenantSettings().Load()!.ProductConfigPath);
    }

    [Fact]
    public void Save_WithNoConnectionStringTypedAndNoneSaved_SavesNothing()
    {
        SettingsViewModel settings = NewSettings();
        settings.Open();
        int saved = 0;
        settings.Saved += (_, _) => saved++;

        settings.SaveCommand.Execute(null);

        Assert.True(settings.StatusIsError);
        Assert.Contains("Nothing is saved yet", settings.StatusMessage);
        Assert.False(File.Exists(ConfigPath));
        Assert.Empty(_credentials.Secrets);
        Assert.Equal(0, saved);
        Assert.True(settings.IsOpen);
    }

    [Fact]
    public void Save_WithABlankTenantName_SavesNothing()
    {
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.TenantName = "  ";
        settings.ConnectionString = Secret;

        settings.SaveCommand.Execute(null);

        Assert.True(settings.StatusIsError);
        Assert.Contains("tenant name", settings.StatusMessage);
        Assert.Empty(_credentials.Secrets);
    }

    [Fact]
    public void Save_WithTheConnectionStringLeftBlank_KeepsTheSavedOne()
    {
        SavedSetup("acme");
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.ProductConfigPath = @"C:\products\new.yaml";

        settings.SaveCommand.Execute(null);

        Assert.Equal(Secret, _credentials.Get(TenantSettings.CredentialTarget("acme")));
        Assert.Equal(@"C:\products\new.yaml", NewTenantSettings().Load()!.ProductConfigPath);
    }

    [Fact]
    public void Save_WithANewConnectionString_ReplacesTheSavedOne()
    {
        SavedSetup("acme");
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.ConnectionString = OtherSecret;

        settings.SaveCommand.Execute(null);

        Assert.Equal(OtherSecret, _credentials.Get(TenantSettings.CredentialTarget("acme")));
    }

    [Fact]
    public void Save_UnderANewName_MovesTheSecretAndLeavesNoCopyUnderTheOldName()
    {
        SavedSetup("old-name");
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.TenantName = "new-name";

        settings.SaveCommand.Execute(null);

        Assert.Equal(Secret, _credentials.Get(TenantSettings.CredentialTarget("new-name")));
        Assert.Null(_credentials.Get(TenantSettings.CredentialTarget("old-name")));
        Assert.Equal("new-name", NewTenantSettings().Load()!.TenantName);
    }

    [Fact]
    public void Save_WhenTheCredentialStoreFails_ShowsAnErrorWithoutTheSecret_AndStaysOpen()
    {
        _credentials.FailOnSet = true;
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.ConnectionString = Secret;
        int saved = 0;
        settings.Saved += (_, _) => saved++;

        settings.SaveCommand.Execute(null);

        Assert.True(settings.StatusIsError);
        Assert.Contains("Could not save", settings.StatusMessage);
        Assert.DoesNotContain("hunter2", settings.StatusMessage);
        Assert.Equal(0, saved);
        Assert.True(settings.IsOpen);
    }

    [Fact]
    public void Close_DropsAnythingTypedIntoTheSecretBox()
    {
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.ConnectionString = Secret;

        settings.CloseCommand.Execute(null);

        Assert.False(settings.IsOpen);
        Assert.Equal("", settings.ConnectionString);
    }

    // ---- Testing the connection ----

    [Fact]
    public async Task TestConnection_UsesWhatWasTyped_ReportsOk_AndSavesNothing()
    {
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.ConnectionString = Secret;
        settings.ProductConfigPath = @"C:\products\x.yaml";

        await settings.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal((Secret, @"C:\products\x.yaml"), Assert.Single(_tester.Calls));
        Assert.False(settings.StatusIsError);
        Assert.Contains("Connection OK", settings.StatusMessage);
        Assert.Contains("nothing is saved until you press Save", settings.StatusMessage);
        Assert.False(File.Exists(ConfigPath));
        Assert.Empty(_credentials.Secrets);
        Assert.False(settings.IsBusy);
    }

    [Fact]
    public async Task TestConnection_WithNothingTyped_TestsTheSavedConnection()
    {
        SavedSetup("acme");
        SettingsViewModel settings = NewSettings();
        settings.Open();

        await settings.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(Secret, Assert.Single(_tester.Calls).Connection);
        Assert.False(settings.StatusIsError);
    }

    [Fact]
    public async Task TestConnection_WithNothingTypedOrSaved_AsksForOne_WithoutCallingTheTester()
    {
        SettingsViewModel settings = NewSettings();
        settings.Open();

        await settings.TestConnectionCommand.ExecuteAsync(null);

        Assert.Empty(_tester.Calls);
        Assert.True(settings.StatusIsError);
    }

    [Fact]
    public async Task TestConnection_Failure_ShowsTheReason()
    {
        _tester.Result = new ConnectionTestResult(false, "password authentication failed for user \"app\"");
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.ConnectionString = Secret;

        await settings.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(settings.StatusIsError);
        Assert.Contains("Connection failed: password authentication failed", settings.StatusMessage);
    }

    [Fact]
    public async Task TestConnection_CanBeCancelled_AndSaveIsOffWhileItRuns()
    {
        _tester.Hold = new TaskCompletionSource<ConnectionTestResult>();
        SettingsViewModel settings = NewSettings();
        settings.Open();
        settings.ConnectionString = Secret;

        Task test = settings.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(settings.IsBusy);
        Assert.False(settings.SaveCommand.CanExecute(null));

        settings.TestConnectionCancelCommand.Execute(null);
        await test;

        Assert.False(settings.IsBusy);
        Assert.Contains("cancelled", settings.StatusMessage);
        Assert.True(settings.SaveCommand.CanExecute(null));
    }

    // ---- What the tester shows ----

    [Fact]
    public void Redact_RemovesTheWholeConnectionStringAndThePasswordInsideIt()
    {
        string text = "failed for " + Secret + " and also hunter2-SECRET appeared alone";

        string cleaned = LocalBackendConnectionTester.Redact(text, Secret);

        Assert.DoesNotContain("hunter2", cleaned);
        Assert.DoesNotContain("db.example", cleaned);
    }

    [Fact]
    public void Redact_HandlesQuotedPasswordsAndLeavesOrdinaryTextAlone()
    {
        string connection = "Host=h;Password='quoted secret';Database=d";

        Assert.Equal("login failed for ***", LocalBackendConnectionTester.Redact("login failed for quoted secret", connection));
        Assert.Equal("timeout expired", LocalBackendConnectionTester.Redact("timeout expired", connection));
    }

    [Fact]
    public async Task TheRealTester_AgainstAnUnreachableDatabase_FailsWithoutLeakingTheSecret()
    {
        const string unreachable = "Host=127.0.0.1;Port=1;Database=nothing;Username=nobody;Password=SECRET-MARKER-5521;Timeout=1";

        ConnectionTestResult result = await new LocalBackendConnectionTester().TestAsync(unreachable, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
        Assert.DoesNotContain("SECRET-MARKER-5521", result.Message);
    }

    // Opt-in: uses this machine's real saved setup (Windows Credential Manager) and the real database. It only
    // reads: the settings are opened and the saved connection is tested (a plain SELECT 1). Nothing is saved.
    [FactIfLive]
    public async Task RealSetup_OpensWithoutShowingTheConnection_AndTestConnectionWorks()
    {
        SettingsViewModel settings = new SettingsViewModel(
            new TenantSettings(TenantSettings.DefaultConfigPath, CredentialStoreFactory.Create()),
            new LocalBackendConnectionTester(),
            LocalBackendLocations.DefaultProductConfigPath(),
            CredentialStoreFactory.StoreName);

        settings.Open();

        Assert.True(settings.HasStoredConnection, "this machine has no saved connection to test");
        Assert.Equal("", settings.ConnectionString);
        Assert.False(settings.NeedsSetup());

        await settings.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(settings.StatusIsError, settings.StatusMessage);
        Assert.Contains("Connection OK", settings.StatusMessage);
        Assert.DoesNotContain("Password", settings.StatusMessage);
    }

    // ---- The chat around the settings ----

    private sealed class RecordingConnector : IChatConnector
    {
        private readonly Action _onConnect;
        private readonly bool _fail;

        public RecordingConnector(Action onConnect, bool fail = false)
        {
            _onConnect = onConnect;
            _fail = fail;
        }

        public Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            _onConnect();
            if (_fail)
            {
                throw new InvalidOperationException("Tenant 'default' has no stored database connection.");
            }

            ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);
            return Task.FromResult(new ChatConnection(session, "connected", Array.Empty<AITool>(), new ServerLogBuffer(), () => ValueTask.CompletedTask));
        }
    }

    [Fact]
    public async Task SavingSettings_ReconnectsInReadOnlyMode_EvenIfChangesWereAllowed()
    {
        List<bool> connectedWithChangesAllowed = new List<bool>();
        Func<bool, IChatConnector> factory = allow => new RecordingConnector(() => connectedWithChangesAllowed.Add(allow));
        ChatViewModel chat = new ChatViewModel();
        chat.EnableModeSwitch(factory);
        SettingsViewModel settings = NewSettings();
        chat.AttachSettings(settings);
        await chat.ConnectAsync(factory(false));
        await chat.ToggleChangesCommand.ExecuteAsync(null);
        Assert.True(chat.AllowChanges);
        connectedWithChangesAllowed.Clear();

        settings.Open();
        settings.ConnectionString = Secret;
        settings.SaveCommand.Execute(null);

        await Task.Delay(100);
        Assert.False(chat.AllowChanges);
        Assert.Equal(new[] { false }, connectedWithChangesAllowed);
        Assert.Contains("read-only", Assert.Single(chat.Messages).Text);
        Assert.True(chat.IsConnected);
    }

    [Fact]
    public async Task AFailedFirstConnection_WithNothingSaved_OpensTheSettingsForFirstTimeSetup()
    {
        ChatViewModel chat = new ChatViewModel();
        SettingsViewModel settings = NewSettings();
        chat.AttachSettings(settings);

        await chat.ConnectAsync(new RecordingConnector(() => { }, fail: true));

        Assert.True(settings.IsOpen);
        Assert.Contains("First-time setup", settings.StatusMessage);
        Assert.Contains("Open Settings", chat.Messages.Single().Text);
    }

    [Fact]
    public async Task AFailedConnection_WithASetupAlreadySaved_DoesNotJumpToTheSettings()
    {
        SavedSetup("acme");
        ChatViewModel chat = new ChatViewModel();
        SettingsViewModel settings = NewSettings();
        chat.AttachSettings(settings);

        await chat.ConnectAsync(new RecordingConnector(() => { }, fail: true));

        Assert.False(settings.IsOpen);
        Assert.Contains("Open Settings", chat.Messages.Single().Text);
    }

    [Fact]
    public void WithoutSettings_ThereIsNoSettingsButton()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);

        Assert.False(chat.HasSettings);
        Assert.False(chat.OpenSettingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheSettingsButton_IsOffWhileAReplyIsBeingReceived()
    {
        ChatViewModel chat = new ChatViewModel();
        chat.AttachSettings(NewSettings());
        await chat.ConnectAsync(new RecordingConnector(() => { }));
        Assert.True(chat.OpenSettingsCommand.CanExecute(null));

        chat.IsBusy = true;

        Assert.False(chat.OpenSettingsCommand.CanExecute(null));
    }
}
