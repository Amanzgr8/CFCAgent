using CommunityToolkit.Mvvm.ComponentModel;

namespace CFCAgent.App.Core;

public enum ChatAuthor
{
    User,
    Assistant,

    // Something the app itself wants to say (an error, for example), not the user or the model.
    Notice,

    // An "apply this change?" card: the server's preview with Apply and Cancel, left behind as a one-line record.
    Approval,
}

// One entry in the conversation as the window shows it. Text and IsStreaming change while a reply
// is arriving, so the window updates as they do. An approval card is an entry too, so the question and
// its answer stay in the conversation where they happened.
public sealed partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isStreaming;

    public ChatMessageViewModel(ChatAuthor author, string text)
    {
        Author = author;
        _text = text;
    }

    public ChatMessageViewModel(ApprovalRequestViewModel approval)
    {
        Author = ChatAuthor.Approval;
        _text = approval.Message;
        Approval = approval;
    }

    public ChatAuthor Author { get; }

    // Set only for an approval card.
    public ApprovalRequestViewModel? Approval { get; }

    public bool IsUser => Author == ChatAuthor.User;

    public bool IsAssistant => Author == ChatAuthor.Assistant;

    public bool IsNotice => Author == ChatAuthor.Notice;

    public bool IsApproval => Author == ChatAuthor.Approval;

    // Shown as plain text, never as Markdown: the user's own words and the app's notices.
    public bool IsPlainText => Author == ChatAuthor.User || Author == ChatAuthor.Notice;
}
