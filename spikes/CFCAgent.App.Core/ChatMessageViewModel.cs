using CommunityToolkit.Mvvm.ComponentModel;

namespace CFCAgent.App.Core;

public enum ChatAuthor
{
    User,
    Assistant,

    // Something the app itself wants to say (an error, for example), not the user or the model.
    Notice,
}

// One message in the conversation as the window shows it. Text and IsStreaming change while a reply
// is arriving, so the window updates as they do.
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

    public ChatAuthor Author { get; }

    public bool IsUser => Author == ChatAuthor.User;

    public bool IsAssistant => Author == ChatAuthor.Assistant;

    public bool IsNotice => Author == ChatAuthor.Notice;
}
