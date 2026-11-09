using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CFCAgent.App.Core;

// Something that can ask the user "apply this change?" and report the answer. The real one opens a dialog;
// tests supply a scripted one. Its shape matches the callback the backend wants for the server's
// confirmation requests, so a presenter's ConfirmAsync can be handed straight to LocalBackendOptions.
public interface IApprovalPresenter
{
    // Returns true only when the user explicitly approved. Cancelling, closing the dialog, or any other way
    // of leaving it returns false. If the request itself is cancelled (the server gave up waiting), it
    // throws OperationCanceledException.
    Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken);
}

// One pending "apply this change?" question as the dialog shows it: the preview text the server sent, and
// two commands. The first answer wins: once the user has applied or cancelled, nothing can change it, so a
// stray second click or the window closing afterwards can never turn a "no" into a "yes" (or the other way).
// Never answering (closing the window) counts as cancel.
public sealed partial class ApprovalRequestViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    public ApprovalRequestViewModel(string message)
    {
        Message = message;
    }

    // The exact preview the server produced. Shown as it is; the app does not reword it.
    public string Message { get; }

    // Completes with the user's answer.
    public Task<bool> Decision => _decision.Task;

    public bool IsDecided => _decision.Task.IsCompleted;

    [RelayCommand]
    private void Apply()
    {
        Decide(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Decide(false);
    }

    // Called when the window is closed without a button being pressed. Counts as cancel.
    public void CloseWithoutAnswer()
    {
        Decide(false);
    }

    private void Decide(bool approved)
    {
        if (_decision.TrySetResult(approved))
        {
            OnPropertyChanged(nameof(IsDecided));
        }
    }
}
