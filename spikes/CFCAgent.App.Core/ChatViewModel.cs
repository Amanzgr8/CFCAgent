using System.Collections.ObjectModel;
using CFCAgent.Backend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core;

// Everything the chat window does, with no UI in it: the list of messages, the text being typed, and
// the Send and Stop commands. Sending shows the user's message at once, then fills an assistant
// message piece by piece as the reply streams in. Stop cancels the reply being received. A failure
// is shown as a notice in the conversation instead of crashing the app.
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly ChatSession _session;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isBusy;

    public ChatViewModel(ChatSession session)
    {
        _session = session;
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new ObservableCollection<ChatMessageViewModel>();

    // Shown in the window footer, to make it obvious what the app is actually running on.
    public string RuntimeInfo => RuntimeSummary.Describe();

    // A conversation with the stub model, which only echoes. It needs no database, server or API key,
    // so the window can be developed and tested on its own.
    public static ChatViewModel CreateDemo(TimeSpan streamDelay)
    {
        ChatSession session = new ChatSession(new StubChatClient(streamDelay), Array.Empty<AITool>(), null);
        return new ChatViewModel(session);
    }

    private bool CanSend()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(Input);
    }

    [RelayCommand(CanExecute = nameof(CanSend), IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        string text = Input.Trim();
        Input = "";
        IsBusy = true;

        Messages.Add(new ChatMessageViewModel(ChatAuthor.User, text));
        ChatMessageViewModel reply = new ChatMessageViewModel(ChatAuthor.Assistant, "");
        reply.IsStreaming = true;
        Messages.Add(reply);

        try
        {
            await foreach (ChatResponseUpdate update in _session.SendStreamingAsync(text, cancellationToken))
            {
                reply.Text += update.Text;
            }
        }
        catch (OperationCanceledException)
        {
            reply.Text += reply.Text.Length > 0 ? "\n\n(stopped)" : "(stopped)";
        }
        catch (Exception ex)
        {
            if (reply.Text.Length == 0)
            {
                Messages.Remove(reply);
            }

            Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, $"Something went wrong: {ex.Message}"));
        }
        finally
        {
            reply.IsStreaming = false;
            IsBusy = false;
        }
    }
}
