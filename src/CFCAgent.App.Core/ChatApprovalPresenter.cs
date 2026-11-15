namespace CFCAgent.App.Core;

// Answers the server's "apply this change?" questions by putting an approval card in the chat. The backend
// calls this from a background thread, so everything that touches the conversation is posted to the UI
// thread. One question is open at a time: a second waits until the first has been answered.
public sealed class ChatApprovalPresenter : IApprovalPresenter
{
    private readonly ChatViewModel _chat;
    private readonly SynchronizationContext? _uiThread;
    private readonly SemaphoreSlim _oneAtATime = new SemaphoreSlim(1, 1);

    // uiThread is the UI's synchronization context (null in tests, where everything runs on the calling thread).
    public ChatApprovalPresenter(ChatViewModel chat, SynchronizationContext? uiThread)
    {
        _chat = chat;
        _uiThread = uiThread;
    }

    public async Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            ApprovalRequestViewModel request = new ApprovalRequestViewModel(message);
            await OnUiThreadAsync(() => _chat.ShowApproval(request));

            // If the server stops waiting, or the reply is stopped, the card is withdrawn rather than left clickable.
            using CancellationTokenRegistration withdraw = cancellationToken.Register(
                () => _ = OnUiThreadAsync(request.Withdraw));

            bool approved = await request.Decision;
            cancellationToken.ThrowIfCancellationRequested();
            return approved;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private Task OnUiThreadAsync(Action action)
    {
        if (_uiThread is null)
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiThread.Post(
            _ =>
            {
                try
                {
                    action();
                    done.SetResult();
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            },
            null);
        return done.Task;
    }
}
