using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using CFCAgent.Markdown.Bakeoff;

[assembly: AvaloniaTestApplication(typeof(BakeoffAppBuilder))]

namespace CFCAgent.Markdown.Bakeoff;

public class BakeoffApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

// Skia drawing is switched on (not the do-nothing headless drawing) so rendered frames can be saved as pictures.
public static class BakeoffAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<BakeoffApp>()
            .WithInterFont()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
