using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Tests;

// The window while connecting to a backend: connected (with the real MCP server started, but a database that
// goes nowhere and the echo-only stub model), and not connected (with a notice and a Retry button).
public class ConnectionWindowTests
{
    private sealed class InMemoryCredentials : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new Dictionary<string, string>();

        public string? Get(string target) => _secrets.TryGetValue(target, out string? value) ? value : null;

        public void Set(string target, string userName, string secret) => _secrets[target] = secret;

        public bool Delete(string target) => _secrets.Remove(target);
    }

    private sealed class RefusingConnector : IChatConnector
    {
        public Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Tenant 'default' has no stored database connection. Run the CLI with --setup to enter it.");
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

    private static void Capture(MainWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(ScreenshotDirectory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static string StatusLineText(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        return window.FindControl<TextBlock>("StatusLine")!.Text ?? "";
    }

    [AvaloniaFact]
    public async Task ConnectedToTheRealServer_ShowsStatusAndAnswersThroughIt()
    {
        string folder = Path.Combine(Path.GetTempPath(), "cfcagent-window-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            InMemoryCredentials credentials = new InMemoryCredentials();
            string tenantConfig = Path.Combine(folder, "tenant.json");
            new TenantSettings(tenantConfig, credentials).Save(
                new TenantConfig { TenantName = "window-test" },
                "Host=127.0.0.1;Port=1;Database=nothing;Username=nobody;Password=nothing;Timeout=1");

            ChatViewModel viewModel = new ChatViewModel();
            MainWindow window = new MainWindow { DataContext = viewModel, Width = 900, Height = 420 };
            window.Show();
            Assert.False(window.FindControl<TextBox>("InputBox")!.IsEnabled, "typing is off until connected");

            LocalBackendConnector connector = new LocalBackendConnector(new LocalBackendOptions
            {
                TenantConfigPath = tenantConfig,
                Credentials = credentials,
                ForceStubModel = true,
            });
            await using (viewModel)
            {
                await viewModel.ConnectAsync(connector);

                Assert.True(viewModel.IsConnected, string.Join(" / ", viewModel.Messages.Select(m => m.Text)));
                Assert.Contains("tenant 'window-test'", StatusLineText(window));
                Assert.Contains("read-only", StatusLineText(window));
                Assert.True(window.FindControl<TextBox>("InputBox")!.IsEnabled);

                viewModel.Input = "How many ingredients are there?";
                await viewModel.SendCommand.ExecuteAsync(null);
                Capture(window, "chat-connected");

                Assert.Equal(2, viewModel.Messages.Count);
                Assert.Contains("You said: How many ingredients are there?", viewModel.Messages[1].Text);
            }
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public async Task NotConnected_ShowsTheReasonAndARetryButton()
    {
        ChatViewModel viewModel = new ChatViewModel();
        MainWindow window = new MainWindow { DataContext = viewModel, Width = 900, Height = 420 };
        window.Show();

        await viewModel.ConnectAsync(new RefusingConnector());

        Assert.Equal("Not connected", StatusLineText(window));
        Assert.False(window.FindControl<TextBox>("InputBox")!.IsEnabled);
        Assert.Contains("Run the CLI with --setup", viewModel.Messages.Single().Text);
        Capture(window, "chat-not-connected");
    }
}
