using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, "Something went wrong: the model is unreachable"));

        string path = Capture(window, "chat-notice");

        Assert.True(new FileInfo(path).Length > 1000, "the screenshot looks empty");
        await Task.CompletedTask;
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
