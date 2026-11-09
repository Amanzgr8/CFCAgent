using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CFCAgent.App.Core;

namespace CFCAgent.App;

// Window behaviour that only makes sense with a screen: Enter sends and Shift+Enter adds a line, and the
// message list follows new text as it arrives. Everything else lives in ChatViewModel.
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Tunnel, not the usual bubble: the text box handles Enter itself (it adds a new line, because it
        // accepts multi-line text), so a normal KeyDown handler would never see the key. Tunnelling reaches
        // this handler first, which can send the message and mark the key handled.
        InputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);

        // Esc cancels an open approval question wherever the keyboard focus is.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ChatViewModel viewModel)
        {
            return;
        }

        viewModel.Messages.CollectionChanged += OnMessagesChanged;
        foreach (ChatMessageViewModel message in viewModel.Messages)
        {
            message.PropertyChanged += OnMessagePropertyChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ChatMessageViewModel message in e.NewItems.OfType<ChatMessageViewModel>())
            {
                message.PropertyChanged += OnMessagePropertyChanged;
            }
        }

        ScrollToEnd();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatMessageViewModel.Text))
        {
            ScrollToEnd();
        }
    }

    private void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ChatViewModel { PendingApproval: ApprovalRequestViewModel pending })
        {
            e.Handled = true;
            pending.CancelCommand.Execute(null);
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        bool sendKey = e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!sendKey || DataContext is not ChatViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.SendCommand.CanExecute(null))
        {
            viewModel.SendCommand.Execute(null);
        }
    }
}
