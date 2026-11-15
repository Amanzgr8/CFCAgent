using System.Runtime.CompilerServices;
using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

// The approval question: its answer rules, how it appears in the conversation, and where the reply text
// goes around it. Everything here runs without a window, a server or a database.
public class ApprovalCardTests
{
    private const string Preview = "Ingredient D066 (Urea)\n  owncost (cost per weight unit): 0.41 -> 0.43\n\nNothing has been changed yet.";

    // ---- The question and its answer ----

    [Fact]
    public async Task Apply_AnswersYes()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(Preview);

        request.ApplyCommand.Execute(null);

        Assert.True(await request.Decision);
        Assert.Equal(ApprovalState.Approved, request.State);
        Assert.False(request.IsPending);
        Assert.Equal("Approved", request.StatusText);
    }

    [Fact]
    public async Task Cancel_AnswersNo()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(Preview);

        request.CancelCommand.Execute(null);

        Assert.False(await request.Decision);
        Assert.Equal(ApprovalState.Cancelled, request.State);
        Assert.Contains("nothing was changed", request.StatusText);
    }

    [Fact]
    public async Task Withdrawing_AnswersNo()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(Preview);

        request.Withdraw();

        Assert.False(await request.Decision);
        Assert.Equal(ApprovalState.Withdrawn, request.State);
    }

    [Fact]
    public async Task TheFirstAnswerWins_ACancelCanNeverBecomeAYes()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(Preview);

        request.CancelCommand.Execute(null);
        request.ApplyCommand.Execute(null);
        request.Withdraw();

        Assert.False(await request.Decision);
        Assert.Equal(ApprovalState.Cancelled, request.State);
    }

    [Fact]
    public async Task TheFirstAnswerWins_AnApplyIsNotUndoneLater()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(Preview);

        request.ApplyCommand.Execute(null);
        request.Withdraw();
        request.CancelCommand.Execute(null);

        Assert.True(await request.Decision);
        Assert.Equal(ApprovalState.Approved, request.State);
    }

    [Fact]
    public void OnceAnswered_TheButtonsCannotBePressedAgain()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(Preview);
        Assert.True(request.ApplyCommand.CanExecute(null));
        Assert.True(request.CancelCommand.CanExecute(null));

        request.CancelCommand.Execute(null);

        Assert.False(request.ApplyCommand.CanExecute(null));
        Assert.False(request.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void NoAnswerYet_MeansPendingAndNotApproved()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(Preview);

        Assert.True(request.IsPending);
        Assert.False(request.Decision.IsCompleted);
    }

    [Fact]
    public void TheMessageIsKeptExactlyAsTheServerSentIt_AndTheSummaryIsItsFirstLine()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel("\n  \nIngredient D066 (Urea)\n  owncost: 0.41 -> 0.43");

        Assert.Equal("\n  \nIngredient D066 (Urea)\n  owncost: 0.41 -> 0.43", request.Message);
        Assert.Equal("Ingredient D066 (Urea)", request.Summary);
    }

    [Fact]
    public void ALongFirstLine_IsShortenedInTheSummary()
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(new string('x', 300));

        Assert.True(request.Summary.Length <= 103);
        Assert.EndsWith("...", request.Summary);
    }

    // ---- The card in the conversation ----

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition(), "the condition was never met");
    }

    [Fact]
    public async Task ConfirmAsync_PutsACardInTheChat_AndApplyAnswersYes()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        ChatApprovalPresenter presenter = new ChatApprovalPresenter(chat, null);

        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);

        ChatMessageViewModel card = Assert.Single(chat.Messages);
        Assert.Equal(ChatAuthor.Approval, card.Author);
        Assert.Equal(Preview, card.Approval!.Message);
        Assert.Same(card.Approval, chat.PendingApproval);
        Assert.False(answer.IsCompleted);

        card.Approval.ApplyCommand.Execute(null);

        Assert.True(await answer);
        Assert.Null(chat.PendingApproval);
        Assert.Single(chat.Messages);
        Assert.Equal(ApprovalState.Approved, card.Approval.State);
    }

    [Fact]
    public async Task ConfirmAsync_Cancel_AnswersNo()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        ChatApprovalPresenter presenter = new ChatApprovalPresenter(chat, null);
        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);

        chat.PendingApproval!.CancelCommand.Execute(null);

        Assert.False(await answer);
        Assert.Null(chat.PendingApproval);
    }

    [Fact]
    public async Task ConfirmAsync_WhenTheServerStopsWaiting_WithdrawsTheCardAndThrows()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        ChatApprovalPresenter presenter = new ChatApprovalPresenter(chat, null);
        using CancellationTokenSource serverGaveUp = new CancellationTokenSource();
        Task<bool> answer = presenter.ConfirmAsync(Preview, serverGaveUp.Token);
        ApprovalRequestViewModel card = chat.PendingApproval!;

        serverGaveUp.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => answer);
        Assert.Equal(ApprovalState.Withdrawn, card.State);
        Assert.Null(chat.PendingApproval);
    }

    [Fact]
    public async Task TwoQuestionsAtOnce_AreAskedOneAfterTheOther()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        ChatApprovalPresenter presenter = new ChatApprovalPresenter(chat, null);
        Task<bool> first = presenter.ConfirmAsync("first change", CancellationToken.None);
        Task<bool> second = presenter.ConfirmAsync("second change", CancellationToken.None);
        await Task.Delay(50);

        Assert.Single(chat.Messages);
        Assert.False(second.IsCompleted);

        chat.PendingApproval!.ApplyCommand.Execute(null);
        Assert.True(await first);

        await WaitUntilAsync(() => chat.Messages.Count == 2);
        Assert.Equal("second change", chat.PendingApproval!.Message);
        chat.PendingApproval.CancelCommand.Execute(null);
        Assert.False(await second);
    }

    [Fact]
    public async Task DisposingTheViewModel_WithdrawsAnOpenQuestion_SoNothingIsLeftWaiting()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        ChatApprovalPresenter presenter = new ChatApprovalPresenter(chat, null);
        Task<bool> answer = presenter.ConfirmAsync(Preview, CancellationToken.None);

        await chat.DisposeAsync();

        Assert.False(await answer);
    }

    // ---- Where the reply text goes around the card ----

    // A model that says something, asks for approval in the middle of its reply (the way a write tool does),
    // then says what happened.
    private sealed class AskingChatClient : IChatClient
    {
        private readonly Func<string, CancellationToken, Task<bool>> _confirm;
        private readonly string? _before;

        public AskingChatClient(Func<string, CancellationToken, Task<bool>> confirm, string? before)
        {
            _confirm = confirm;
            _before = before;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_before is not null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, _before);
            }

            bool approved = await _confirm(Preview, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, approved ? "Done." : "Not applied.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static (ChatViewModel Chat, Task Send) StartAskingReply(string? before)
    {
        ChatViewModel? chat = null;
        ChatApprovalPresenter? presenter = null;
        AskingChatClient client = new AskingChatClient((message, ct) => presenter!.ConfirmAsync(message, ct), before);
        ChatSession session = new ChatSession(client, Array.Empty<AITool>(), null);
        chat = new ChatViewModel(session, "test");
        presenter = new ChatApprovalPresenter(chat, null);
        chat.Input = "Set D066 to 0.43";
        return (chat, chat.SendCommand.ExecuteAsync(null));
    }

    [Fact]
    public async Task TextAfterTheCard_StartsANewBubbleBelowIt()
    {
        (ChatViewModel chat, Task send) = StartAskingReply("Applying now. ");
        await WaitUntilAsync(() => chat.PendingApproval is not null);

        chat.PendingApproval!.ApplyCommand.Execute(null);
        await send;

        Assert.Collection(
            chat.Messages,
            m => Assert.Equal(ChatAuthor.User, m.Author),
            m =>
            {
                Assert.Equal(ChatAuthor.Assistant, m.Author);
                Assert.Equal("Applying now. ", m.Text);
                Assert.False(m.IsStreaming);
            },
            m => Assert.Equal(ChatAuthor.Approval, m.Author),
            m =>
            {
                Assert.Equal(ChatAuthor.Assistant, m.Author);
                Assert.Equal("Done.", m.Text);
                Assert.False(m.IsStreaming);
            });
        Assert.False(chat.IsBusy);
    }

    [Fact]
    public async Task AnEmptyBubbleBeforeTheCard_IsRemoved()
    {
        (ChatViewModel chat, Task send) = StartAskingReply(before: null);
        await WaitUntilAsync(() => chat.PendingApproval is not null);

        chat.PendingApproval!.CancelCommand.Execute(null);
        await send;

        Assert.Collection(
            chat.Messages,
            m => Assert.Equal(ChatAuthor.User, m.Author),
            m => Assert.Equal(ChatAuthor.Approval, m.Author),
            m => Assert.Equal("Not applied.", m.Text));
    }

    [Fact]
    public async Task StoppingTheReplyWhileAQuestionIsOpen_WithdrawsTheCardAndChangesNothing()
    {
        (ChatViewModel chat, Task send) = StartAskingReply("Applying now. ");
        await WaitUntilAsync(() => chat.PendingApproval is not null);
        ApprovalRequestViewModel card = chat.PendingApproval!;

        chat.SendCancelCommand.Execute(null);
        await send;

        Assert.Equal(ApprovalState.Withdrawn, card.State);
        Assert.Null(chat.PendingApproval);
        Assert.False(chat.IsBusy);
        Assert.Contains(chat.Messages, m => m.Text.Contains("(stopped)"));
    }
}
