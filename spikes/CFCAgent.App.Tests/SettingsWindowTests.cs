using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Tests;

// The settings screen in the real window, driven with real clicks and typing. The connection string must be masked
// while it is typed and must never appear anywhere on screen once it is saved.
public class SettingsWindowTests : IDisposable
{
    private const string Secret = "Host=db.example;Database=cfc;Username=app;Password=hunter2-SECRET";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cfcagent-settings-window-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCredentials _credentials = new FakeCredentials();
    private readonly FakeTester _tester = new FakeTester();

    private sealed class FakeCredentials : ICredentialStore
    {
        public Dictionary<string, string> Secrets { get; } = new Dictionary<string, string>();

        public string? Get(string target) => Secrets.TryGetValue(target, out string? value) ? value : null;

        public void Set(string target, string userName, string secret) => Secrets[target] = secret;

        public bool Delete(string target) => Secrets.Remove(target);
    }

    private sealed class FakeTester : IConnectionTester
    {
        public ConnectionTestResult Result { get; set; } = new ConnectionTestResult(true, "Database connection OK.");

        public Task<ConnectionTestResult> TestAsync(string connectionString, string? productConfigPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeConnector : IChatConnector
    {
        private readonly bool _fail;

        public FakeConnector(bool fail)
        {
            _fail = fail;
        }

        public Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            if (_fail)
            {
                throw new InvalidOperationException("Tenant 'default' has no stored database connection.");
            }

            ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);
            return Task.FromResult(new ChatConnection(session, "connected to the test database", Array.Empty<AITool>(), new ServerLogBuffer(), () => ValueTask.CompletedTask));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static string ScreenshotDirectory
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("CFCAGENT_SCREENSHOT_DIR");
            string directory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "screenshots")
                : configured;
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    private static async Task PumpAsync(double milliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(1);
        }
        while (clock.Elapsed.TotalMilliseconds < milliseconds);
    }

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(ScreenshotDirectory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Click(Window window, Control control)
    {
        Point? centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
    }

    private static T Find<T>(MainWindow window, string name)
        where T : Control
    {
        return window.GetVisualDescendants().OfType<T>().First(control => control.Name == name);
    }

    // Everything readable on screen: text blocks and the text of every box (the masked box included, since its Text
    // holds what was typed).
    private static string EverythingOnScreen(MainWindow window)
    {
        System.Text.StringBuilder all = new System.Text.StringBuilder();
        foreach (Visual visual in window.GetVisualDescendants())
        {
            if (visual is Control { IsEffectivelyVisible: true } control)
            {
                if (control is TextBlock block)
                {
                    all.Append(block.Text).Append('\n');
                    foreach (Avalonia.Controls.Documents.Inline inline in block.Inlines ?? new Avalonia.Controls.Documents.InlineCollection())
                    {
                        if (inline is Avalonia.Controls.Documents.Run run)
                        {
                            all.Append(run.Text);
                        }
                    }
                }
                else if (control is TextBox box)
                {
                    all.Append(box.Text).Append('\n');
                }
            }
        }

        return all.ToString();
    }

    private (MainWindow Window, ChatViewModel Chat, SettingsViewModel Settings) OpenWindow(bool alreadySaved)
    {
        TenantSettings tenantSettings = new TenantSettings(Path.Combine(_folder, "tenant.json"), _credentials);
        if (alreadySaved)
        {
            tenantSettings.Save(new TenantConfig { TenantName = "acme" }, Secret);
        }

        SettingsViewModel settings = new SettingsViewModel(tenantSettings, _tester, @"C:\products\default-schema-map.yaml", "Windows Credential Manager");
        ChatViewModel chat = new ChatViewModel();
        chat.EnableModeSwitch(allow => new FakeConnector(fail: false));
        chat.AttachSettings(settings);
        MainWindow window = new MainWindow { DataContext = chat, Width = 900, Height = 640 };
        window.Show();
        return (window, chat, settings);
    }

    [AvaloniaFact]
    public async Task ClickingSettings_ShowsTheScreenOverTheChat_AndCloseBringsTheChatBack()
    {
        (MainWindow window, ChatViewModel chat, SettingsViewModel settings) = OpenWindow(alreadySaved: false);
        await chat.ConnectAsync(new FakeConnector(fail: false));
        await PumpAsync(50);
        Assert.False(Find<Border>(window, "SettingsOverlay").IsVisible);

        Click(window, Find<Button>(window, "SettingsButton"));
        await PumpAsync(100);

        Assert.True(settings.IsOpen);
        Assert.True(Find<Border>(window, "SettingsOverlay").IsEffectivelyVisible);

        Click(window, Find<Button>(window, "CloseButton"));
        await PumpAsync(50);

        Assert.False(settings.IsOpen);
        Assert.False(Find<Border>(window, "SettingsOverlay").IsVisible);
    }

    [AvaloniaFact]
    public async Task TheConnectionStringBox_IsMasked_AndTypingReachesTheViewModel()
    {
        (MainWindow window, ChatViewModel chat, SettingsViewModel settings) = OpenWindow(alreadySaved: false);
        await chat.ConnectAsync(new FakeConnector(fail: false));
        settings.Open();
        await PumpAsync(100);

        TextBox secretBox = Find<TextBox>(window, "SecretBox");
        Assert.Equal('•', secretBox.PasswordChar);
        Assert.False(secretBox.RevealPassword);

        secretBox.Focus();
        window.KeyTextInput(Secret);
        await PumpAsync(50);

        Assert.Equal(Secret, settings.ConnectionString);
        Capture(window, "settings-typing-masked");
    }

    [AvaloniaFact]
    public async Task ASavedConnectionString_IsNeverShownAnywhereOnScreen()
    {
        (MainWindow window, ChatViewModel chat, SettingsViewModel settings) = OpenWindow(alreadySaved: true);
        await chat.ConnectAsync(new FakeConnector(fail: false));

        Click(window, Find<Button>(window, "SettingsButton"));
        await PumpAsync(100);

        Assert.Equal("", Find<TextBox>(window, "SecretBox").Text ?? "");
        Assert.Equal("acme", Find<TextBox>(window, "TenantBox").Text);
        string screen = EverythingOnScreen(window);
        Assert.DoesNotContain("hunter2", screen);
        Assert.DoesNotContain("db.example", screen);
        Assert.Contains("A connection is saved", Find<TextBox>(window, "SecretBox").PlaceholderText);
        Capture(window, "settings-saved-connection");
    }

    [AvaloniaFact]
    public async Task ClickingSave_StoresTheConnection_ClosesTheScreen_AndReconnectsReadOnly()
    {
        (MainWindow window, ChatViewModel chat, SettingsViewModel settings) = OpenWindow(alreadySaved: false);
        await chat.ConnectAsync(new FakeConnector(fail: false));
        settings.Open();
        await PumpAsync(100);
        Find<TextBox>(window, "TenantBox").Text = "acme";
        Find<TextBox>(window, "SecretBox").Text = Secret;
        await PumpAsync(30);

        Click(window, Find<Button>(window, "SaveButton"));
        await PumpAsync(300);

        Assert.Equal(Secret, _credentials.Get(TenantSettings.CredentialTarget("acme")));
        Assert.False(settings.IsOpen);
        Assert.True(chat.IsConnected);
        Assert.Contains("Settings saved", chat.Messages.Single().Text);
        Assert.DoesNotContain("hunter2", EverythingOnScreen(window));
    }

    [AvaloniaFact]
    public async Task ClickingTestConnection_ShowsTheResult_WithoutSaving()
    {
        (MainWindow window, ChatViewModel chat, SettingsViewModel settings) = OpenWindow(alreadySaved: false);
        await chat.ConnectAsync(new FakeConnector(fail: false));
        settings.Open();
        await PumpAsync(100);
        Find<TextBox>(window, "SecretBox").Text = Secret;
        await PumpAsync(30);

        Click(window, Find<Button>(window, "TestButton"));
        await PumpAsync(200);

        Assert.Contains("Connection OK", Find<TextBlock>(window, "StatusText").Text);
        Assert.Empty(_credentials.Secrets);
        Capture(window, "settings-test-ok");

        _tester.Result = new ConnectionTestResult(false, "could not connect to the server");
        Click(window, Find<Button>(window, "TestButton"));
        await PumpAsync(200);

        Assert.Contains("Connection failed: could not connect to the server", Find<TextBlock>(window, "StatusText").Text);
        Assert.Contains("error", Find<TextBlock>(window, "StatusText").Classes);
    }

    [AvaloniaFact]
    public async Task PressingEscape_ClosesTheScreen_AndDropsWhatWasTyped()
    {
        (MainWindow window, ChatViewModel chat, SettingsViewModel settings) = OpenWindow(alreadySaved: false);
        await chat.ConnectAsync(new FakeConnector(fail: false));
        settings.Open();
        await PumpAsync(100);
        Find<TextBox>(window, "SecretBox").Text = Secret;
        await PumpAsync(30);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await PumpAsync(50);

        Assert.False(settings.IsOpen);
        Assert.Equal("", settings.ConnectionString);
    }

    [AvaloniaFact]
    public async Task FirstRun_WithNothingSaved_OpensTheSettingsByItself()
    {
        (MainWindow window, ChatViewModel chat, SettingsViewModel settings) = OpenWindow(alreadySaved: false);

        await chat.ConnectAsync(new FakeConnector(fail: true));
        await PumpAsync(150);

        Assert.True(settings.IsOpen);
        Assert.True(Find<Border>(window, "SettingsOverlay").IsEffectivelyVisible);
        Assert.Contains("First-time setup", Find<TextBlock>(window, "StatusText").Text);
        Capture(window, "settings-first-run");
    }
}
