using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CFCAgent.App.Core;

namespace CFCAgent.App.Tests;

public class ChatWindowTests
{
    // Where screenshots go. Set CFCAGENT_SCREENSHOT_DIR to collect them somewhere specific.
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

    private static string Capture(MainWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        string path = Path.Combine(ScreenshotDirectory, name + ".png");
        frame.Save(path, new PngBitmapEncoderOptions());
        return path;
    }

    [AvaloniaFact]
    public void EmptyWindow_RendersAndIsSaved()
    {
        MainWindow window = new MainWindow { DataContext = ChatViewModel.CreateDemo(TimeSpan.Zero) };
        window.Show();

        string path = Capture(window, "chat-empty");

        Assert.True(new FileInfo(path).Length > 1000, "the screenshot looks empty");
    }

    [AvaloniaFact]
    public async Task WindowAfterAConversation_ShowsBothSidesAndIsSaved()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel };
        window.Show();

        viewModel.Input = "Which formulas use D066?";
        await viewModel.SendCommand.ExecuteAsync(null);
        viewModel.Input = "And what does it cost per ton?";
        await viewModel.SendCommand.ExecuteAsync(null);

        string path = Capture(window, "chat-conversation");

        Assert.Equal(4, viewModel.Messages.Count);
        Assert.True(new FileInfo(path).Length > 1000, "the screenshot looks empty");
    }

    [AvaloniaFact]
    public async Task WindowShowingAFailure_ShowsTheNoticeAndIsSaved()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel };
        window.Show();

        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.User, "Change D066's cost"));
        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, "Something went wrong: the model is unreachable", isError: true));

        string path = Capture(window, "chat-notice");

        Assert.True(new FileInfo(path).Length > 1000, "the screenshot looks empty");
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Notices_LookLikeInformation_UnlessTheyReportAFailure()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel, Width = 900, Height = 420 };
        window.Show();

        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, "Changes are now allowed. Nothing is applied unless you approve it."));
        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, "Could not connect: the database password was rejected", isError: true));
        Capture(window, "chat-notices");

        // Every message has a notice frame in its template; only a notice shows it.
        List<Border> notices = window.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("notice") && border.IsEffectivelyVisible)
            .ToList();
        Assert.Equal(2, notices.Count);
        Assert.DoesNotContain("error", notices[0].Classes);
        Assert.Contains("error", notices[1].Classes);
    }

    [AvaloniaFact]
    public async Task TheThemeButton_SwitchesTheWindowBetweenLightAndDark()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow
        {
            DataContext = viewModel,
            Width = 900,
            Height = 640,
            RequestedThemeVariant = ThemeVariant.Light,
        };
        window.Show();
        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.User, "Which formulas use D066?"));
        viewModel.Messages.Add(new ChatMessageViewModel(
            ChatAuthor.Assistant,
            "**3 formulas** use `D066`:\n\n| Formula | Grade | Cost per ton |\n|---|---|---:|\n| F1001 | 10-10-10 | 549.42 |\n| F1002 | 28-0-0 | 478.53 |\n"));
        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, "Back to read-only. The assistant can no longer change anything."));
        await SettleAsync(300);

        Button themeButton = window.FindControl<Button>("ThemeButton")!;
        Click(window, themeButton);
        await SettleAsync(300);

        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        Capture(window, "chat-dark");

        Click(window, themeButton);
        await SettleAsync(100);

        Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
    }

    private static void Click(Window window, Control control)
    {
        Point? centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
    }

    // Lets layout and timers run for a while (the Markdown view fills in on a timer).
    private static async Task SettleAsync(double milliseconds)
    {
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(10);
        }
        while (clock.Elapsed.TotalMilliseconds < milliseconds);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }

        Assert.True(condition(), "the condition was never met");
    }

    // Types into the input box and presses keys the way a person would: focus the box, type, press a key.
    private static (MainWindow Window, ChatViewModel ViewModel, TextBox InputBox) OpenWithTypedText(string text)
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel };
        window.Show();
        TextBox inputBox = window.FindControl<TextBox>("InputBox")!;
        inputBox.Focus();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel, inputBox);
    }

    [AvaloniaFact]
    public async Task PressingEnter_SendsTheMessage()
    {
        (MainWindow window, ChatViewModel viewModel, TextBox _) = OpenWithTypedText("hello there");
        Assert.Equal("hello there", viewModel.Input);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        await WaitUntilAsync(() => viewModel.Messages.Count == 2 && !viewModel.IsBusy);
        Assert.Equal("hello there", viewModel.Messages[0].Text);
        Assert.Equal("", viewModel.Input);
    }

    [AvaloniaFact]
    public async Task PressingShiftEnter_AddsALineAndDoesNotSend()
    {
        (MainWindow window, ChatViewModel viewModel, TextBox _) = OpenWithTypedText("first line");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(viewModel.Messages);
        Assert.Contains("\n", viewModel.Input);
        Assert.StartsWith("first line", viewModel.Input);
    }

    [AvaloniaFact]
    public async Task PressingEnterWithNothingTyped_DoesNothing()
    {
        (MainWindow window, ChatViewModel viewModel, TextBox _) = OpenWithTypedText("   ");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(viewModel.Messages);
    }

    [AvaloniaFact]
    public void TypingIntoTheInputBox_UpdatesTheViewModelAndEnablesSend()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel };
        window.Show();

        Assert.False(viewModel.SendCommand.CanExecute(null));

        TextBox inputBox = window.FindControl<TextBox>("InputBox")!;
        inputBox.Text = "hello";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("hello", viewModel.Input);
        Assert.True(viewModel.SendCommand.CanExecute(null));
    }
}
