using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
