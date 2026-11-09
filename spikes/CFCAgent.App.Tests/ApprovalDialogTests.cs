using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CFCAgent.App.Core;
using CFCAgent.Backend;

namespace CFCAgent.App.Tests;

// The approval dialog, driven the way a person would: real mouse clicks and key presses on the real window.
// Every way of leaving the dialog except pressing "Apply change" must answer no.
public class ApprovalDialogTests
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

    private static async Task<ApprovalWindow> WaitForDialogAsync(AvaloniaApprovalPresenter presenter)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 5)
        {
            ApprovalWindow? dialog = presenter.CurrentDialog;
            if (dialog is not null && dialog.IsVisible)
            {
                await PumpAsync(30);
                return dialog;
            }

            await PumpAsync(5);
        }

        throw new TimeoutException("the approval dialog never appeared");
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

    private static void Click(Window window, Button button)
    {
        Point? centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
    }

    private static (MainWindow Owner, AvaloniaApprovalPresenter Presenter) OpenOwnerAndPresenter()
    {
        MainWindow owner = new MainWindow { DataContext = ChatViewModel.CreateDemo(TimeSpan.Zero), Width = 900, Height = 600 };
        owner.Show();
        return (owner, new AvaloniaApprovalPresenter(() => owner));
    }

    [AvaloniaFact]
    public async Task ClickingApplyChange_AnswersYes()
    {
        (Window owner, AvaloniaApprovalPresenter presenter) = OpenOwnerAndPresenter();
        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);
        ApprovalWindow dialog = await WaitForDialogAsync(presenter);

        Click(dialog, dialog.FindControl<Button>("ApplyButton")!);

        Assert.True(await AnswerOrTimeOutAsync(answer));
        await PumpAsync(50);
        Assert.False(dialog.IsVisible, "the dialog closes once answered");
        Assert.Null(presenter.CurrentDialog);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task ClickingCancel_AnswersNo()
    {
        (Window owner, AvaloniaApprovalPresenter presenter) = OpenOwnerAndPresenter();
        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);
        ApprovalWindow dialog = await WaitForDialogAsync(presenter);

        Click(dialog, dialog.FindControl<Button>("CancelButton")!);

        Assert.False(await AnswerOrTimeOutAsync(answer));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task PressingEscape_AnswersNo()
    {
        (Window owner, AvaloniaApprovalPresenter presenter) = OpenOwnerAndPresenter();
        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);
        ApprovalWindow dialog = await WaitForDialogAsync(presenter);

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(await AnswerOrTimeOutAsync(answer));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task PressingEnter_AnswersNo_BecauseCancelIsTheDefault()
    {
        (Window owner, AvaloniaApprovalPresenter presenter) = OpenOwnerAndPresenter();
        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);
        ApprovalWindow dialog = await WaitForDialogAsync(presenter);

        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.False(await AnswerOrTimeOutAsync(answer));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_AnswersNo()
    {
        (Window owner, AvaloniaApprovalPresenter presenter) = OpenOwnerAndPresenter();
        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);
        ApprovalWindow dialog = await WaitForDialogAsync(presenter);

        dialog.Close();

        Assert.False(await AnswerOrTimeOutAsync(answer));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheServerStopsWaiting_TheDialogIsWithdrawnAndTheRequestIsCancelled()
    {
        (Window owner, AvaloniaApprovalPresenter presenter) = OpenOwnerAndPresenter();
        using CancellationTokenSource serverGaveUp = new CancellationTokenSource();
        Task<bool> answer = presenter.ConfirmAsync(Preview, serverGaveUp.Token);
        ApprovalWindow dialog = await WaitForDialogAsync(presenter);

        serverGaveUp.Cancel();

        Stopwatch clock = Stopwatch.StartNew();
        while (!answer.IsCompleted && clock.Elapsed.TotalSeconds < 5)
        {
            await PumpAsync(5);
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => answer);
        Assert.False(dialog.IsVisible, "the dialog is taken away rather than left on screen");
        owner.Close();
    }

    [AvaloniaFact]
    public async Task TheDialogShowsTheServersPreviewUnchanged_AndCancelIsTheDefaultButton()
    {
        (Window owner, AvaloniaApprovalPresenter presenter) = OpenOwnerAndPresenter();
        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);
        ApprovalWindow dialog = await WaitForDialogAsync(presenter);

        Assert.Equal(Preview, dialog.FindControl<SelectableTextBlock>("PreviewText")!.Text);
        Assert.True(dialog.FindControl<Button>("CancelButton")!.IsDefault);
        Assert.True(dialog.FindControl<Button>("CancelButton")!.IsCancel);
        Assert.False(dialog.FindControl<Button>("ApplyButton")!.IsDefault);

        dialog.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap? frame = dialog.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(ScreenshotDirectory, "approval-dialog.png"), new PngBitmapEncoderOptions());

        Click(dialog, dialog.FindControl<Button>("CancelButton")!);
        await AnswerOrTimeOutAsync(answer);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task TwoQuestionsAtOnce_AreAskedOneAfterTheOther()
    {
        (Window owner, AvaloniaApprovalPresenter presenter) = OpenOwnerAndPresenter();
        Task<bool> first = presenter.ConfirmAsync("first change", CancellationToken.None);
        ApprovalWindow firstDialog = await WaitForDialogAsync(presenter);
        Task<bool> second = presenter.ConfirmAsync("second change", CancellationToken.None);
        await PumpAsync(100);

        Assert.Same(firstDialog, presenter.CurrentDialog);
        Assert.Equal("first change", firstDialog.FindControl<SelectableTextBlock>("PreviewText")!.Text);
        Assert.False(second.IsCompleted);

        Click(firstDialog, firstDialog.FindControl<Button>("ApplyButton")!);
        Assert.True(await AnswerOrTimeOutAsync(first));

        ApprovalWindow secondDialog = await WaitForDialogAsync(presenter);
        Assert.NotSame(firstDialog, secondDialog);
        Assert.Equal("second change", secondDialog.FindControl<SelectableTextBlock>("PreviewText")!.Text);
        Click(secondDialog, secondDialog.FindControl<Button>("CancelButton")!);
        Assert.False(await AnswerOrTimeOutAsync(second));
        owner.Close();
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

                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(ScreenshotDirectory, "chat-changes-allowed.png"), new PngBitmapEncoderOptions());
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
