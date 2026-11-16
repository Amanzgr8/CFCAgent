using System.Collections.ObjectModel;
using System.Globalization;
using CFCAgent.Backend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core;

// One saved conversation in the sidebar's list of recent chats.
public sealed partial class RecentChatViewModel : ObservableObject
{
    // When it was last used, in words ("Today, 14:05", "Yesterday", "12 Nov").
    [ObservableProperty]
    private string _whenText;

    // True for the conversation that is open in the window right now.
    [ObservableProperty]
    private bool _isCurrent;

    // True while the row asks "Delete?" instead of showing the chat. Deleting takes two clicks on purpose.
    [ObservableProperty]
    private bool _isConfirmingDelete;

    public RecentChatViewModel(string id, string title, string whenText)
    {
        Id = id;
        Title = title;
        _whenText = whenText;
    }

    public string Id { get; }

    public string Title { get; }

    [RelayCommand]
    private void AskDelete()
    {
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void KeepChat()
    {
        IsConfirmingDelete = false;
    }

    // "Today, 14:05" for today, "Yesterday", then the day and month, with the year once it is another year.
    public static string DescribeWhen(DateTimeOffset when, DateTimeOffset now, CultureInfo culture)
    {
        DateTime day = when.Date;
        DateTime today = now.Date;
        if (day == today)
        {
            return "Today, " + when.ToString("t", culture);
        }

        if (day == today.AddDays(-1))
        {
            return "Yesterday";
        }

        return when.ToString(day.Year == today.Year ? "d MMM" : "d MMM yyyy", culture);
    }
}

// The part of the chat that deals with more than one conversation: starting a new one, and keeping finished
// ones so they can be reopened later, also after the app was closed. Conversations are saved through a
// ConversationStore when the app has one; without one (the demo, most tests) New chat still works and
// nothing is kept.
public sealed partial class ChatViewModel
{
    private const int RecentChatLimit = 50;
    private const int TitleLength = 60;

    private ConversationStore? _store;
    private TimeProvider _clock = TimeProvider.System;

    // The saved copy of the conversation in the window, once it has one. A conversation is first saved when
    // its first reply ends, so a new chat nobody typed into leaves nothing behind.
    private string? _currentId;
    private string? _currentTitle;
    private DateTimeOffset _currentCreatedUtc;

    // What the live connection is connected to ("tenant 'x'"), when known.
    private string? _connectedSource;

    // A failure to save is shown once, not after every reply.
    private bool _saveFailureShown;

    // Saved conversations, most recently used first.
    public ObservableCollection<RecentChatViewModel> RecentChats { get; } = new ObservableCollection<RecentChatViewModel>();

    // Whether conversations are kept at all, so the window knows whether to show the list.
    public bool HasHistory => _store is not null;

    // Gives the view-model somewhere to keep conversations, and fills the list from what is already there.
    // A store that cannot be read does not stop the app: the chat works, and a notice says nothing is listed.
    public void AttachHistory(ConversationStore store, TimeProvider? clock = null)
    {
        _store = store;
        _clock = clock ?? TimeProvider.System;
        OnPropertyChanged(nameof(HasHistory));

        try
        {
            foreach (ConversationSummary summary in store.List(RecentChatLimit))
            {
                RecentChats.Add(new RecentChatViewModel(summary.Id, summary.Title, DescribeWhen(summary.UpdatedUtc)));
            }
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageViewModel(
                ChatAuthor.Notice,
                $"Your earlier chats could not be loaded: {ex.Message}",
                isError: true));
        }
    }

    // A conversation can be swapped only between replies, and not while the connection is being set up.
    private bool CanChangeConversation()
    {
        return !IsBusy && !IsConnecting;
    }

    // Starts an empty conversation on the same connection. The one that was open stays in the list.
    [RelayCommand(CanExecute = nameof(CanChangeConversation))]
    private void NewChat()
    {
        CloseSettingsIfOpen();
        ShowConversation(null);
    }

    // Reopens a saved conversation: the window shows it as it was, and the model is given its turns again,
    // so the next question carries on from it.
    [RelayCommand(CanExecute = nameof(CanChangeConversation))]
    private void OpenChat(RecentChatViewModel? chat)
    {
        if (chat is null || _store is null)
        {
            return;
        }

        CloseSettingsIfOpen();
        if (chat.Id == _currentId)
        {
            return;
        }

        SavedConversation? saved;
        try
        {
            saved = _store.Load(chat.Id);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, $"That chat could not be opened: {ex.Message}", isError: true));
            return;
        }

        if (saved is null)
        {
            // It is gone from the file (deleted from another window, for example): drop it from the list too.
            RecentChats.Remove(chat);
            return;
        }

        ShowConversation(saved);
    }

    // Deletes a saved conversation for good. If it is the one in the window, the window starts a new chat.
    [RelayCommand(CanExecute = nameof(CanChangeConversation))]
    private void DeleteChat(RecentChatViewModel? chat)
    {
        if (chat is null || _store is null)
        {
            return;
        }

        try
        {
            _store.Delete(chat.Id);
        }
        catch (Exception ex)
        {
            chat.IsConfirmingDelete = false;
            Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, $"That chat could not be deleted: {ex.Message}", isError: true));
            return;
        }

        RecentChats.Remove(chat);
        if (chat.Id == _currentId)
        {
            ShowConversation(null);
        }
    }

    private void CloseSettingsIfOpen()
    {
        if (Settings is { IsOpen: true } settings)
        {
            settings.Close();
        }
    }

    // Puts a saved conversation in the window, or an empty one when saved is null. The connection is kept:
    // the session forgets its turns and, for a saved conversation, takes that one's turns over. With no
    // connection the turns wait until one is made (see ConnectAsync).
    private void ShowConversation(SavedConversation? saved)
    {
        PendingApproval?.Withdraw();
        Messages.Clear();

        _currentId = saved?.Id;
        _currentTitle = saved?.Title;
        _currentCreatedUtc = saved?.CreatedUtc ?? default;

        List<ChatMessage>? turns = saved?.Turns.ToList();
        string? note = saved is null ? null : ReopenedNote(saved);
        if (_session is not null)
        {
            _session.Clear();
            if (turns is not null)
            {
                _session.ContinueFrom(turns, note);
            }

            _carriedTurns = null;
            _carriedNote = null;
        }
        else
        {
            _carriedTurns = turns;
            _carriedNote = note;
        }

        if (saved is not null)
        {
            foreach (TranscriptEntry entry in saved.Transcript)
            {
                Messages.Add(entry.Author == ChatAuthor.Approval
                    ? new ChatMessageViewModel(ApprovalRequestViewModel.Answered(entry.Text, entry.Approval ?? ApprovalState.Withdrawn))
                    : new ChatMessageViewModel(entry.Author, entry.Text, entry.IsError));
            }

            if (saved.Source is not null && _connectedSource is not null && saved.Source != _connectedSource)
            {
                Messages.Add(new ChatMessageViewModel(
                    ChatAuthor.Notice,
                    $"This chat was held while connected to {saved.Source}. You are now connected to {_connectedSource}, so figures above may not match."));
            }
        }

        MarkCurrentChat();
    }

    // What the model is told when a saved conversation is reopened: time has passed, the mode may differ from
    // the one the conversation was held in, and so may the database.
    private string ReopenedNote(SavedConversation saved)
    {
        string lastUsed = saved.UpdatedUtc.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        string mode = AllowChanges
            ? "Changes are allowed in this session, and each one still needs the user's approval on an approval card."
            : "This session is read-only: the tools that change data are not available.";
        string source = saved.Source is not null && _connectedSource is not null && saved.Source != _connectedSource
            ? $" It was held while connected to {saved.Source}; this session is connected to {_connectedSource}, which may be a different database."
            : "";
        return $"The app reopened this saved conversation, which was last used on {lastUsed}. {mode}{source} " +
            "The data may have changed since then, so look values up again before relying on figures above.";
    }

    // Forgets which saved conversation the window holds, without touching what is on screen. Used when the
    // connection is rebuilt for new settings: what follows is a new conversation.
    private void StartUnsavedConversation()
    {
        _currentId = null;
        _currentTitle = null;
        _currentCreatedUtc = default;
        MarkCurrentChat();
    }

    // Writes the conversation in the window to the store and puts it at the top of the list. Called when a
    // reply ends, however it ended. A conversation without a single message from the user is not saved.
    private void SaveConversation()
    {
        if (_store is null)
        {
            return;
        }

        ChatMessageViewModel? firstQuestion = Messages.FirstOrDefault(message => message.IsUser);
        if (firstQuestion is null)
        {
            return;
        }

        try
        {
            DateTimeOffset now = _clock.GetUtcNow();
            if (_currentId is null)
            {
                _currentId = Guid.NewGuid().ToString("N");
                _currentCreatedUtc = now;
            }

            _currentTitle ??= TitleFrom(firstQuestion.Text);

            IEnumerable<ChatMessage> history = _session?.History ?? (IEnumerable<ChatMessage>?)_carriedTurns ?? Array.Empty<ChatMessage>();
            _store.Save(new SavedConversation
            {
                Id = _currentId,
                Title = _currentTitle,
                Source = _connectedSource,
                CreatedUtc = _currentCreatedUtc,
                UpdatedUtc = now,
                Turns = history.Where(message => message.Role != ChatRole.System).ToList(),
                Transcript = Messages.Where(message => message.Text.Length > 0).Select(ToTranscriptEntry).ToList(),
            });

            PutCurrentChatFirst(now);
            _saveFailureShown = false;
        }
        catch (Exception ex)
        {
            if (!_saveFailureShown)
            {
                _saveFailureShown = true;
                Messages.Add(new ChatMessageViewModel(
                    ChatAuthor.Notice,
                    $"This chat could not be saved, so it will not be in your recent chats: {ex.Message}",
                    isError: true));
            }
        }
    }

    private static TranscriptEntry ToTranscriptEntry(ChatMessageViewModel message)
    {
        return message.Approval is ApprovalRequestViewModel approval
            ? new TranscriptEntry(ChatAuthor.Approval, approval.Message, Approval: approval.IsPending ? ApprovalState.Withdrawn : approval.State)
            : new TranscriptEntry(message.Author, message.Text, message.IsError);
    }

    private void PutCurrentChatFirst(DateTimeOffset now)
    {
        RecentChatViewModel? current = RecentChats.FirstOrDefault(chat => chat.Id == _currentId);
        if (current is null)
        {
            current = new RecentChatViewModel(_currentId!, _currentTitle!, DescribeWhen(now));
            RecentChats.Insert(0, current);
            while (RecentChats.Count > RecentChatLimit)
            {
                RecentChats.RemoveAt(RecentChats.Count - 1);
            }
        }
        else
        {
            current.WhenText = DescribeWhen(now);
            int index = RecentChats.IndexOf(current);
            if (index > 0)
            {
                RecentChats.Move(index, 0);
            }
        }

        MarkCurrentChat();
    }

    private void MarkCurrentChat()
    {
        foreach (RecentChatViewModel chat in RecentChats)
        {
            chat.IsCurrent = chat.Id == _currentId;
            chat.IsConfirmingDelete = false;
        }
    }

    private string DescribeWhen(DateTimeOffset whenUtc)
    {
        TimeZoneInfo zone = _clock.LocalTimeZone;
        return RecentChatViewModel.DescribeWhen(
            TimeZoneInfo.ConvertTime(whenUtc, zone),
            TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), zone),
            CultureInfo.CurrentCulture);
    }

    // The first line of the first question, shortened: what the chat is called in the list.
    private static string TitleFrom(string firstQuestion)
    {
        string line = firstQuestion.Split('\n').Select(part => part.Trim()).FirstOrDefault(part => part.Length > 0) ?? "Chat";
        return line.Length > TitleLength ? line.Substring(0, TitleLength).TrimEnd() + "..." : line;
    }
}
