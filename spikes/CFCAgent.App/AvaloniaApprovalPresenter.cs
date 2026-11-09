using Avalonia.Controls;
using Avalonia.Threading;
using CFCAgent.App.Core;

namespace CFCAgent.App;

// Answers the server's "apply this change?" questions by opening ApprovalWindow over the main window. The
// backend calls this from a background thread, so everything that touches the screen is moved to the UI
// thread. One dialog is open at a time: a second question waits until the first is answered.
public sealed class AvaloniaApprovalPresenter : IApprovalPresenter
{
    private readonly Func<Window?> _ownerProvider;
    private readonly SemaphoreSlim _oneAtATime = new SemaphoreSlim(1, 1);

    public AvaloniaApprovalPresenter(Func<Window?> ownerProvider)
    {
        _ownerProvider = ownerProvider;
    }

    // The dialog that is open right now, if any. For tests and for bringing it to the front.
    public ApprovalWindow? CurrentDialog { get; private set; }

    public async Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            bool approved = await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(message, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return approved;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<bool> ShowAsync(string message, CancellationToken cancellationToken)
    {
        ApprovalRequestViewModel request = new ApprovalRequestViewModel(message);
        ApprovalWindow dialog = new ApprovalWindow { DataContext = request };

        // If the server stops waiting (its own time limit), the dialog is withdrawn rather than left on screen.
        using CancellationTokenRegistration withdraw = cancellationToken.Register(
            () => Dispatcher.UIThread.Post(request.CloseWithoutAnswer));

        CurrentDialog = dialog;
        try
        {
            Window? owner = _ownerProvider();
            if (owner is not null && owner.IsVisible)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
                await request.Decision;
            }

            return await request.Decision;
        }
        finally
        {
            CurrentDialog = null;
        }
    }
}
