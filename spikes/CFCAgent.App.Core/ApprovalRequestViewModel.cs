using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CFCAgent.App.Core;

// Something that can ask the user "apply this change?" and report the answer. The real one puts a card in
// the chat; tests supply a scripted one. Its shape matches the callback the backend wants for the server's
// confirmation requests, so a presenter's ConfirmAsync can be handed straight to LocalBackendOptions.
public interface IApprovalPresenter
{
    // Returns true only when the user explicitly approved. Cancelling, or any other way of not approving,
    // returns false. If the request itself is withdrawn (the server gave up waiting, or the reply was
    // stopped), it throws OperationCanceledException.
    Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken);
}

public enum ApprovalState
{
    Pending,
    Approved,
    Cancelled,

    // Taken away without an answer: the server stopped waiting, the reply was stopped, or the app closed.
    // Counts as "no": nothing was changed.
    Withdrawn,
}

// One "apply this change?" question as the chat card shows it: the preview text the server sent, two commands,
// and a state. The first answer wins: once the user has applied or cancelled (or the question was withdrawn),
// nothing can change it, so a stray second click can never turn a "no" into a "yes" or the other way round.
// Only an explicit Apply is a yes.
public sealed partial class ApprovalRequestViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private ApprovalState _state = ApprovalState.Pending;

    public ApprovalRequestViewModel(string message)
    {
        Message = message;
    }

    // The exact preview the server produced. Shown as it is; the app does not reword it.
    public string Message { get; }

    // The first non-empty line of the preview: what the card shrinks to once the question is answered.
    public string Summary
    {
        get
        {
            string? first = Message.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
            return first is null ? "change" : (first.Length > 100 ? first.Substring(0, 100) + "..." : first);
        }
    }

    public bool IsPending => State == ApprovalState.Pending;

    // "Approved", "Cancelled" or "Withdrawn", for the one-line record the card leaves behind.
    public string StatusText => State switch
    {
        ApprovalState.Approved => "Approved",
        ApprovalState.Cancelled => "Cancelled, nothing was changed",
        ApprovalState.Withdrawn => "Withdrawn, nothing was changed",
        _ => "Waiting for your answer",
    };

    // Completes with the user's answer: true only for Apply.
    public Task<bool> Decision => _decision.Task;

    [RelayCommand(CanExecute = nameof(IsPending))]
    private void Apply()
    {
        Decide(ApprovalState.Approved);
    }

    [RelayCommand(CanExecute = nameof(IsPending))]
    private void Cancel()
    {
        Decide(ApprovalState.Cancelled);
    }

    // The question is no longer wanted (the server stopped waiting, the reply was stopped, the app is closing).
    // Counts as "no". Does nothing if the user already answered.
    public void Withdraw()
    {
        Decide(ApprovalState.Withdrawn);
    }

    private void Decide(ApprovalState answer)
    {
        if (State != ApprovalState.Pending)
        {
            return;
        }

        State = answer;
        _decision.TrySetResult(answer == ApprovalState.Approved);
    }
}
