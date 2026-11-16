using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CFCAgent.App.Core;

namespace CFCAgent.App.Tests;

// New chat and the recent chats in the real window, driven with real key presses and mouse clicks. The model
// is the echo-only stub and the chats go to a SQLite file in a temporary folder, never to the user's own.
public class RecentChatsWindowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cfcagent-recent-window-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
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
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(ScreenshotDirectory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Click(Window window, Control control)
    {
        Point? centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(5);
        }

        Assert.True(condition(), "the condition was never met");
    }

    // Types a question into the message box and presses Enter, then waits for the reply to end.
    private static async Task AskAsync(MainWindow window, ChatViewModel viewModel, string question)
    {
        int before = viewModel.Messages.Count;
        TextBox inputBox = window.FindControl<TextBox>("InputBox")!;
        inputBox.Focus();
        window.KeyTextInput(question);
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await WaitUntilAsync(() => viewModel.Messages.Count == before + 2 && !viewModel.IsBusy);
    }

    // The buttons of one kind in the list of recent chats that can be seen right now, top to bottom.
    private static List<Button> RecentButtons(MainWindow window, string kind)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        ItemsControl list = window.FindControl<ItemsControl>("RecentList")!;
        return list.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains(kind) && button.IsEffectivelyVisible)
            .ToList();
    }

    private (MainWindow Window, ChatViewModel ViewModel) OpenWindowThatKeepsChats()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        viewModel.AttachHistory(new ConversationStore(Path.Combine(_folder, "conversations.db")));
        MainWindow window = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    [AvaloniaFact]
    public async Task NewChat_AndARecentChat_WorkWithRealClicks()
    {
        (MainWindow window, ChatViewModel viewModel) = OpenWindowThatKeepsChats();
        Button newChat = window.FindControl<Button>("NewChatButton")!;
        TextBlock emptyHint = window.FindControl<TextBlock>("NoRecentChats")!;
        Assert.True(newChat.IsEffectivelyVisible);
        Assert.True(emptyHint.IsEffectivelyVisible);
        Assert.Empty(RecentButtons(window, "recentChat"));

        await AskAsync(window, viewModel, "Which formulas use D066?");

        Assert.False(emptyHint.IsEffectivelyVisible);
        Assert.Single(RecentButtons(window, "recentChat"));

        Click(window, newChat);

        Assert.Empty(viewModel.Messages);
        Assert.Single(RecentButtons(window, "recentChat"));

        await AskAsync(window, viewModel, "List the binding agents");

        List<Button> rows = RecentButtons(window, "recentChat");
        Assert.Equal(2, rows.Count);
        Assert.Contains("selected", rows[0].Classes);
        Assert.DoesNotContain("selected", rows[1].Classes);
        Capture(window, "chat-recent");

        // The older chat is the second row. Clicking it brings its question and reply back.
        Click(window, rows[1]);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal("Which formulas use D066?", viewModel.Messages[0].Text);
        rows = RecentButtons(window, "recentChat");
        Assert.DoesNotContain("selected", rows[0].Classes);
        Assert.Contains("selected", rows[1].Classes);

        // And the next question is asked in that chat: it moves to the top, and there are still two chats.
        await AskAsync(window, viewModel, "And the cost per ton?");

        Assert.Equal(4, viewModel.Messages.Count);
        Assert.Equal(new[] { "Which formulas use D066?", "List the binding agents" }, viewModel.RecentChats.Select(chat => chat.Title));
    }

    [AvaloniaFact]
    public async Task DeletingAChat_TakesASecondClick_AndKeepCallsItOff()
    {
        (MainWindow window, ChatViewModel viewModel) = OpenWindowThatKeepsChats();
        await AskAsync(window, viewModel, "Which formulas use D066?");
        Click(window, window.FindControl<Button>("NewChatButton")!);
        await AskAsync(window, viewModel, "List the binding agents");

        // The bin on the older chat's row asks first; nothing is deleted yet.
        Click(window, RecentButtons(window, "deleteChat")[1]);

        Assert.Equal(2, viewModel.RecentChats.Count);
        Assert.Single(RecentButtons(window, "confirmDeleteChat"));
        Assert.Single(RecentButtons(window, "recentChat"));
        Capture(window, "chat-recent-delete");

        Click(window, RecentButtons(window, "keepChat")[0]);

        Assert.Equal(2, viewModel.RecentChats.Count);
        Assert.Empty(RecentButtons(window, "confirmDeleteChat"));
        Assert.Equal(2, RecentButtons(window, "recentChat").Count);

        Click(window, RecentButtons(window, "deleteChat")[1]);
        Click(window, RecentButtons(window, "confirmDeleteChat")[0]);

        // The older chat is gone, from the list and from the file. The open chat is untouched.
        Assert.Equal("List the binding agents", Assert.Single(viewModel.RecentChats).Title);
        Assert.Single(new ConversationStore(Path.Combine(_folder, "conversations.db")).List());
        Assert.Equal(2, viewModel.Messages.Count);
    }

    [AvaloniaFact]
    public async Task WhenChatsAreNotKept_ThereIsNoRecentList_ButNewChatStillWorks()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        await AskAsync(window, viewModel, "hello");

        Assert.False(window.FindControl<Grid>("RecentPanel")!.IsEffectivelyVisible);

        Click(window, window.FindControl<Button>("NewChatButton")!);

        Assert.Empty(viewModel.Messages);
    }

    [AvaloniaFact]
    public void ChatsFromAnEarlierRun_AreListedWhenTheWindowOpens()
    {
        ConversationStore store = new ConversationStore(Path.Combine(_folder, "conversations.db"));
        store.Save(new SavedConversation
        {
            Id = "c1",
            Title = "Why is formula 757 infeasible?",
            CreatedUtc = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedUtc = DateTimeOffset.UtcNow.AddDays(-3),
            Turns = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "Why is formula 757 infeasible?"),
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "Its nitrogen minimum cannot be met."),
            },
            Transcript = new List<TranscriptEntry>
            {
                new TranscriptEntry(ChatAuthor.User, "Why is formula 757 infeasible?"),
                new TranscriptEntry(ChatAuthor.Assistant, "Its **nitrogen minimum** cannot be met."),
                new TranscriptEntry(ChatAuthor.Approval, "Formula 757\n  nitrogen minimum: 28 -> 26", Approval: ApprovalState.Approved),
            },
        });

        (MainWindow window, ChatViewModel viewModel) = OpenWindowThatKeepsChats();
        Button row = Assert.Single(RecentButtons(window, "recentChat"));

        Click(window, row);

        Assert.Equal(3, viewModel.Messages.Count);
        Assert.True(viewModel.Messages[2].IsApproval);
        Assert.Null(viewModel.PendingApproval);
        Capture(window, "chat-recent-reopened");
    }
}
