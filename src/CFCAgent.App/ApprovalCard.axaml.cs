using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CFCAgent.App.Core;

namespace CFCAgent.App;

// The "apply this change?" card in the chat. When it appears with a question waiting, Cancel takes the
// keyboard focus, so a stray Enter cancels rather than approves, and the card scrolls into view so it
// cannot be missed. All the logic is in ApprovalRequestViewModel.
public partial class ApprovalCard : UserControl
{
    public ApprovalCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => FocusCancelWhenPending();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FocusCancelWhenPending();
    }

    private void FocusCancelWhenPending()
    {
        if (DataContext is ApprovalRequestViewModel { IsPending: true } && TopLevel.GetTopLevel(this) is not null)
        {
            // After layout, so the button exists and is visible when focus is requested.
            Dispatcher.UIThread.Post(
                () =>
                {
                    CancelButton.Focus();
                    this.BringIntoView();
                },
                DispatcherPriority.Background);
        }
    }
}
