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
            // Stub model for now: it streams an echo word by word, with a short delay so the streaming is visible.
            desktop.MainWindow = new MainWindow
            {
                DataContext = ChatViewModel.CreateDemo(TimeSpan.FromMilliseconds(60)),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
