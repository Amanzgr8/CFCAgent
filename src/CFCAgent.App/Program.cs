using Avalonia;
using CFCAgent.App.Core;

namespace CFCAgent.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupTrace.Mark("Main entered");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the design-time tooling and the headless tests.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
