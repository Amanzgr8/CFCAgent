using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CFCAgent.App.Core;
using CFCAgent.Backend;

namespace CFCAgent.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // --demo runs on the echo-only stub with no database or server (handy for a quick look at the window).
            // Otherwise the app connects to the real backend after the window opens, read-only by default.
            StartupTrace.Mark("framework initialized");
            bool demo = desktop.Args?.Contains("--demo") == true;
            ChatViewModel viewModel = demo ? ChatViewModel.CreateDemo(TimeSpan.FromMilliseconds(60)) : new ChatViewModel();
            MainWindow window = new MainWindow { DataContext = viewModel };

            if (!demo)
            {
                // Read-only unless the user turns changes on; then every change goes through an approval card in
                // the chat. The card is added on the UI thread, which is the thread this code runs on.
                ChatApprovalPresenter approvals = new ChatApprovalPresenter(viewModel, SynchronizationContext.Current);
                Func<bool, IChatConnector> connectorForMode = allowChanges => new LocalBackendConnector(new LocalBackendOptions
                {
                    ReadOnly = !allowChanges,
                    ConfirmAsync = allowChanges ? approvals.ConfirmAsync : null,
                });
                viewModel.EnableModeSwitch(connectorForMode);

                // The settings screen: the same tenant setup and credential store the CLI's --setup uses.
                string defaultProductConfig = LocalBackendLocations.DefaultProductConfigPath();
                viewModel.AttachSettings(new SettingsViewModel(
                    new TenantSettings(TenantSettings.DefaultConfigPath, CredentialStoreFactory.Create()),
                    new LocalBackendConnectionTester(),
                    defaultProductConfig,
                    CredentialStoreFactory.StoreName));
                window.Opened += async (_, _) =>
                {
                    StartupTrace.Mark("window opened");
                    await viewModel.ConnectAsync(connectorForMode(false));
                    StartupTrace.Mark(viewModel.IsConnected ? "connected" : "connection failed");
                };
            }

            // Closing the app ends the MCP server child process. Run on the thread pool so waiting here
            // cannot deadlock with the UI thread.
            desktop.Exit += (_, _) =>
            {
                StartupTrace.Mark("exit begins");
                Task.Run(() => viewModel.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
                StartupTrace.Mark("server shut down");
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
