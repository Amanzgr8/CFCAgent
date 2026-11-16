using Avalonia;
using Avalonia.Headless;
using CFCAgent.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace CFCAgent.App.Tests;

// Starts the real app in Avalonia's headless platform. Skia drawing is switched on (not the
// do-nothing headless drawing) so a rendered frame can be captured and saved as a picture.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .WithInterFont()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
