using System.ComponentModel;
using Avalonia.Controls;
using CFCAgent.App.Core;

namespace CFCAgent.App;

// The dialog that asks "apply this change?". It closes itself as soon as the question is answered, and
// closing it any other way (the X, Alt+F4) counts as cancel. The answer itself lives in the view-model.
public partial class ApprovalWindow : Window
{
    public ApprovalWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) => CancelButton.Focus();
        Closing += OnClosing;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ApprovalRequestViewModel request)
        {
            request.PropertyChanged += OnRequestChanged;
            if (request.IsDecided)
            {
                Close();
            }
        }
    }

    private void OnRequestChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApprovalRequestViewModel.IsDecided) && DataContext is ApprovalRequestViewModel { IsDecided: true })
        {
            Close();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is ApprovalRequestViewModel request)
        {
            request.CloseWithoutAnswer();
        }
    }
}
