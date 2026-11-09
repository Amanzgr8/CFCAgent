using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CFCAgent.App.Core;
using CFCAgent.Backend;

namespace CFCAgent.App.Tests;

// The approval card in the real chat window, driven the way a person would: real mouse clicks and key presses.
// Every way of answering except pressing "Apply change" must answer no.
public class ApprovalCardWindowTests
{
    private const string Preview = "Ingredient D066 (Urea)\n  owncost (cost per weight unit): 0.41 -> 0.43\n\nNothing has been changed yet.";

    private sealed class InMemoryCredentials : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new Dictionary<string, string>();

        public string? Get(string target) => _secrets.TryGetValue(target, out string? value) ? value : null;

        public void Set(string target, string userName, string secret) => _secrets[target] = secret;

        public bool Delete(string target) => _secrets.Remove(target);
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

    private static async Task PumpAsync(double milliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(1);
        }
        while (clock.Elapsed.TotalMilliseconds < milliseconds);
    }

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(ScreenshotDirectory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static async Task<bool> AnswerOrTimeOutAsync(Task<bool> answer)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!answer.IsCompleted && clock.Elapsed.TotalSeconds < 5)
        {
            await PumpAsync(5);
        }

        Assert.True(answer.IsCompleted, "the question was never answered");
        return await answer;
    }

    private static void Click(Window window, Control control)
    {
        Point? centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
    }

    private static T Find<T>(MainWindow window, string name)
        where T : Control
    {
        return window.GetVisualDescendants().OfType<T>().First(control => control.Name == name);
    }

    // A window with a conversation already in it, and a question waiting.
    private static async Task<(MainWindow Window, ChatViewModel Chat, Task<bool> Answer)> AskAsync()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        chat.Messages.Add(new ChatMessageViewModel(ChatAuthor.User, "Set the cost of D066 to 0.43"));
        chat.Messages.Add(new ChatMessageViewModel(ChatAuthor.Assistant, "I'll set **D066** to `0.43`. Here is what will change:") { IsStreaming = true });

        MainWindow window = new MainWindow { DataContext = chat, Width = 900, Height = 640 };
        window.Show();
        ChatApprovalPresenter presenter = new ChatApprovalPresenter(chat, SynchronizationContext.Current);

        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);
        await PumpAsync(150);
        return (window, chat, answer);
    }

    [AvaloniaFact]
    public async Task TheCardAppearsInTheChat_ShowingThePreview_WithCancelFocused()
    {
        (MainWindow window, ChatViewModel chat, Task<bool> answer) = await AskAsync();

        Assert.NotNull(chat.PendingApproval);
        Assert.Equal(Preview, Find<SelectableTextBlock>(window, "PreviewText").Text);
        Assert.True(Find<StackPanel>(window, "PendingPanel").IsEffectivelyVisible);
        Assert.False(Find<StackPanel>(window, "RecordPanel").IsEffectivelyVisible);
        Assert.Same(Find<Button>(window, "CancelButton"), window.FocusManager!.GetFocusedElement());
        Capture(window, "chat-approval-card-pending");

        chat.PendingApproval!.CancelCommand.Execute(null);
        await AnswerOrTimeOutAsync(answer);
    }

    [AvaloniaFact]
    public async Task ClickingApplyChange_AnswersYes_AndTheCardShrinksToARecord()
    {
        (MainWindow window, ChatViewModel chat, Task<bool> answer) = await AskAsync();

        Click(window, Find<Button>(window, "ApplyButton"));

        Assert.True(await AnswerOrTimeOutAsync(answer));
        await PumpAsync(100);
        Assert.False(Find<StackPanel>(window, "PendingPanel").IsEffectivelyVisible);
        Assert.True(Find<StackPanel>(window, "RecordPanel").IsEffectivelyVisible);
        Assert.Equal("Approved", Find<TextBlock>(window, "RecordStatus").Text);
        Assert.Equal("Ingredient D066 (Urea)", Find<TextBlock>(window, "RecordSummary").Text);
        Assert.Null(chat.PendingApproval);
        Capture(window, "chat-approval-card-approved");
    }

    [AvaloniaFact]
    public async Task ClickingCancel_AnswersNo()
    {
        (MainWindow window, ChatViewModel _, Task<bool> answer) = await AskAsync();

        Click(window, Find<Button>(window, "CancelButton"));

        Assert.False(await AnswerOrTimeOutAsync(answer));
        await PumpAsync(100);
        Assert.Equal("Cancelled, nothing was changed", Find<TextBlock>(window, "RecordStatus").Text);
    }

    [AvaloniaFact]
    public async Task PressingEscape_AnswersNo_WhereverTheFocusIs()
    {
        (MainWindow window, ChatViewModel _, Task<bool> answer) = await AskAsync();
        window.FindControl<TextBox>("InputBox")!.Focus();
        await PumpAsync(30);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(await AnswerOrTimeOutAsync(answer));
    }

    [AvaloniaFact]
    public async Task PressingEnter_AnswersNo_BecauseCancelHasTheFocus()
    {
        (MainWindow window, ChatViewModel _, Task<bool> answer) = await AskAsync();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.False(await AnswerOrTimeOutAsync(answer));
    }

    [AvaloniaFact]
    public async Task ACardFarDownALongConversation_IsScrolledIntoView()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        for (int i = 0; i < 25; i++)
        {
            chat.Messages.Add(new ChatMessageViewModel(i % 2 == 0 ? ChatAuthor.User : ChatAuthor.Assistant, $"Message number {i}"));
        }

        MainWindow window = new MainWindow { DataContext = chat, Width = 900, Height = 500 };
        window.Show();
        await PumpAsync(100);
        ChatApprovalPresenter presenter = new ChatApprovalPresenter(chat, SynchronizationContext.Current);

        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);
        await PumpAsync(300);

        ScrollViewer scroller = window.FindControl<ScrollViewer>("Scroller")!;
        Button apply = Find<Button>(window, "ApplyButton");
        Point? applyInScroller = apply.TranslatePoint(new Point(0, apply.Bounds.Height), scroller);
        Assert.NotNull(applyInScroller);
        Assert.True(applyInScroller.Value.Y <= scroller.Bounds.Height, "the Apply button is below the visible part of the chat");
        Assert.True(applyInScroller.Value.Y >= 0, "the Apply button is above the visible part of the chat");

        chat.PendingApproval!.CancelCommand.Execute(null);
        await AnswerOrTimeOutAsync(answer);
    }

    [AvaloniaFact]
    public async Task TheConversationReadsInOrder_TextQuestionAnswerMoreText()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        chat.Messages.Add(new ChatMessageViewModel(ChatAuthor.User, "Set the cost of D066 to 0.43"));
        chat.Messages.Add(new ChatMessageViewModel(ChatAuthor.Assistant, "I'll set **D066** to `0.43`. Here is what will change:"));
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(Preview);
        chat.ShowApproval(request);
        MainWindow window = new MainWindow { DataContext = chat, Width = 900, Height = 640 };
        window.Show();
        request.ApplyCommand.Execute(null);
        chat.Messages.Add(new ChatMessageViewModel(ChatAuthor.Assistant, "Done: **D066** now costs `0.43`."));
        await PumpAsync(200);

        Capture(window, "chat-approval-in-conversation");
        Assert.Equal(
            new[] { ChatAuthor.User, ChatAuthor.Assistant, ChatAuthor.Approval, ChatAuthor.Assistant },
            chat.Messages.Select(m => m.Author).ToArray());
    }

    [AvaloniaFact]
    public async Task TurningChangesOn_ShowsTheAmberBadgeAndTheBackToReadOnlyButton()
    {
        string folder = Path.Combine(Path.GetTempPath(), "cfcagent-badge-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            InMemoryCredentials credentials = new InMemoryCredentials();
            string tenantConfig = Path.Combine(folder, "tenant.json");
            new TenantSettings(tenantConfig, credentials).Save(
                new TenantConfig { TenantName = "badge-test" },
                "Host=127.0.0.1;Port=1;Database=nothing;Username=nobody;Password=nothing;Timeout=1");

            ChatViewModel viewModel = new ChatViewModel();
            MainWindow window = new MainWindow { DataContext = viewModel, Width = 900, Height = 420 };
            window.Show();
            Func<bool, IChatConnector> connectorForMode = allowChanges => new LocalBackendConnector(new LocalBackendOptions
            {
                ReadOnly = !allowChanges,
                TenantConfigPath = tenantConfig,
                Credentials = credentials,
                ForceStubModel = true,
                ConfirmAsync = allowChanges ? (_, _) => Task.FromResult(false) : null,
            });
            viewModel.EnableModeSwitch(connectorForMode);

            await using (viewModel)
            {
                await viewModel.ConnectAsync(connectorForMode(false));
                await PumpAsync(50);
                Assert.Equal("READ-ONLY", window.FindControl<TextBlock>("ModeBadgeText")!.Text);
                Assert.Equal("Allow changes", window.FindControl<Button>("ToggleChangesButton")!.Content);
                Assert.DoesNotContain("writes", window.FindControl<Border>("ModeBadgeBorder")!.Classes);

                await viewModel.ToggleChangesCommand.ExecuteAsync(null);
                await PumpAsync(50);

                Assert.Equal("CHANGES ALLOWED", window.FindControl<TextBlock>("ModeBadgeText")!.Text);
                Assert.Equal("Back to read-only", window.FindControl<Button>("ToggleChangesButton")!.Content);
                Assert.Contains("writes", window.FindControl<Border>("ModeBadgeBorder")!.Classes);
                Assert.Contains("changes allowed (each one needs your approval)", window.FindControl<TextBlock>("StatusLine")!.Text);
                Capture(window, "chat-changes-allowed");
            }
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
