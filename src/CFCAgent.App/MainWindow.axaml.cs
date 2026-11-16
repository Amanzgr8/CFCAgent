using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using CFCAgent.App.Core;
using FluentIcons.Common;

namespace CFCAgent.App;

// Window behaviour that only makes sense with a screen: Enter sends and Shift+Enter adds a line, the
// message list follows new text as it arrives, the mode switch in the message box is kept in step with the
// session, and the moon / sun button switches between light and dark. Everything else lives in ChatViewModel.
public partial class MainWindow : Window
{
    // True while this code moves the mode switch itself, so that is not mistaken for the user flipping it.
    private bool _settingChangesToggle;

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

        // Chat is the page behind the settings screen, so choosing it in the sidebar closes that screen.
        ChatNavButton.Click += (_, _) =>
        {
            if (DataContext is ChatViewModel { Settings: { IsOpen: true } settings })
            {
                settings.Close();
            }
        };

        ChangesToggle.IsCheckedChanged += OnChangesToggleFlipped;

        // The app starts in the system's light or dark mode; the button switches this window by hand.
        ThemeButton.Click += (_, _) =>
            RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        ActualThemeVariantChanged += (_, _) => UpdateThemeIcon();
        UpdateThemeIcon();
    }

    // Shows what the button switches to: a moon in light mode, a sun in dark mode.
    private void UpdateThemeIcon()
    {
        ThemeIcon.Symbol = ActualThemeVariant == ThemeVariant.Dark ? Symbol.WeatherSunny : Symbol.WeatherMoon;
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

        // The mode switch shows the session's mode, and can be flipped only when the mode can be switched
        // (not while a reply is arriving or the app is connecting).
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.AllowChanges))
            {
                ShowModeOnChangesToggle(viewModel);
            }
        };
        viewModel.ToggleChangesCommand.CanExecuteChanged += (_, _) =>
            ChangesToggle.IsEnabled = viewModel.ToggleChangesCommand.CanExecute(null);
        ChangesToggle.IsEnabled = viewModel.ToggleChangesCommand.CanExecute(null);
        ShowModeOnChangesToggle(viewModel);
    }

    private void ShowModeOnChangesToggle(ChatViewModel viewModel)
    {
        _settingChangesToggle = true;
        ChangesToggle.IsChecked = viewModel.AllowChanges;
        _settingChangesToggle = false;
    }

    // The user flipped the mode switch: restart the session in the other mode. Whatever happens, the switch
    // ends up showing the mode the session is really in.
    private async void OnChangesToggleFlipped(object? sender, RoutedEventArgs e)
    {
        if (_settingChangesToggle || DataContext is not ChatViewModel viewModel)
        {
            return;
        }

        bool wanted = ChangesToggle.IsChecked == true;
        if (wanted == viewModel.AllowChanges)
        {
            return;
        }

        try
        {
            if (viewModel.ToggleChangesCommand.CanExecute(null))
            {
                await viewModel.ToggleChangesCommand.ExecuteAsync(null);
            }
        }
        finally
        {
            ShowModeOnChangesToggle(viewModel);
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
        if (e.Key != Key.Escape || DataContext is not ChatViewModel viewModel)
        {
            return;
        }

        if (viewModel.PendingApproval is ApprovalRequestViewModel pending)
        {
            e.Handled = true;
            pending.CancelCommand.Execute(null);
        }
        else if (viewModel.Settings is { IsOpen: true } settings)
        {
            // Esc closes the settings screen (and drops anything typed into the secret box).
            e.Handled = true;
            settings.Close();
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
