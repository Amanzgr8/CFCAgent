using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CFCAgent.App.Core;

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
            bool demo = desktop.Args?.Contains("--demo") == true;
            ChatViewModel viewModel = demo ? ChatViewModel.CreateDemo(TimeSpan.FromMilliseconds(60)) : new ChatViewModel();
            MainWindow window = new MainWindow { DataContext = viewModel };

            if (!demo)
            {
                // Read-only unless the user turns changes on; then every change goes through the approval dialog.
                AvaloniaApprovalPresenter approvals = new AvaloniaApprovalPresenter(() => window);
                Func<bool, IChatConnector> connectorForMode = allowChanges => new LocalBackendConnector(new LocalBackendOptions
                {
                    ReadOnly = !allowChanges,
                    ConfirmAsync = allowChanges ? approvals.ConfirmAsync : null,
                });
                viewModel.EnableModeSwitch(connectorForMode);
                window.Opened += async (_, _) => await viewModel.ConnectAsync(connectorForMode(false));
            }

            // Closing the app ends the MCP server child process. Run on the thread pool so waiting here
            // cannot deadlock with the UI thread.
            desktop.Exit += (_, _) => Task.Run(() => viewModel.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
