using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using CFCAgent.App.Core;
using MarkView.Avalonia;

namespace CFCAgent.App;

// Connects one chat message to one MarkdownViewer. While the reply is arriving, only the new text is handed
// to the viewer's streaming session (the viewer then redraws just the end of the document); when the reply
// ends, the session is completed. A message that is already finished is shown in one go.
//
// Used in XAML as:  <mv:MarkdownViewer local:MarkdownMessage.Source="{Binding}" />
// Only assistant messages are shown as Markdown; the user's own words and app notices stay plain text.
public static class MarkdownMessage
{
    public static readonly AttachedProperty<ChatMessageViewModel?> SourceProperty =
        AvaloniaProperty.RegisterAttached<MarkdownViewer, ChatMessageViewModel?>("Source", typeof(MarkdownMessage));

    // Keeps each viewer's feed alive exactly as long as the viewer itself.
    private static readonly ConditionalWeakTable<MarkdownViewer, MessageFeed> Feeds = new ConditionalWeakTable<MarkdownViewer, MessageFeed>();

    static MarkdownMessage()
    {
        SourceProperty.Changed.AddClassHandler<MarkdownViewer>(OnSourceChanged);
    }

    public static ChatMessageViewModel? GetSource(MarkdownViewer viewer) => viewer.GetValue(SourceProperty);

    public static void SetSource(MarkdownViewer viewer, ChatMessageViewModel? value) => viewer.SetValue(SourceProperty, value);

    private static void OnSourceChanged(MarkdownViewer viewer, AvaloniaPropertyChangedEventArgs e)
    {
        if (Feeds.TryGetValue(viewer, out MessageFeed? old))
        {
            old.Detach();
            Feeds.Remove(viewer);
        }

        if (e.NewValue is ChatMessageViewModel message && message.IsAssistant)
        {
            MessageFeed feed = new MessageFeed(viewer, message);
            Feeds.Add(viewer, feed);
            feed.Attach();
        }
    }

    private sealed class MessageFeed
    {
        private readonly MarkdownViewer _viewer;
        private readonly ChatMessageViewModel _message;
        private MarkdownStreamingSession? _session;
        private int _shown;

        public MessageFeed(MarkdownViewer viewer, ChatMessageViewModel message)
        {
            _viewer = viewer;
            _message = message;
        }

        public void Attach()
        {
            _message.PropertyChanged += OnMessageChanged;
            _viewer.Markdown = "";
            Sync();
        }

        public void Detach()
        {
            _message.PropertyChanged -= OnMessageChanged;
            EndSession();
        }

        private void OnMessageChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ChatMessageViewModel.Text) or nameof(ChatMessageViewModel.IsStreaming))
            {
                Sync();
            }
        }

        private void Sync()
        {
            string text = _message.Text;

            if (_message.IsStreaming)
            {
                _session ??= BeginSession();
                if (text.Length >= _shown)
                {
                    if (text.Length > _shown)
                    {
                        _session.Append(text.Substring(_shown));
                        _shown = text.Length;
                    }

                    return;
                }

                // The text got shorter, which streaming cannot express: start over with what is there now.
                EndSession();
                _viewer.Markdown = text;
                return;
            }

            // Not streaming (any more): bring the viewer up to date and close the session if there is one.
            if (_session is not null)
            {
                if (text.Length > _shown)
                {
                    _session.Append(text.Substring(_shown));
                }

                _shown = text.Length;
                EndSession();
                return;
            }

            if (_viewer.Markdown != text)
            {
                _viewer.Markdown = text;
                _shown = text.Length;
            }
        }

        private MarkdownStreamingSession BeginSession()
        {
            _viewer.Markdown = "";
            _shown = 0;
            return _viewer.BeginStreaming();
        }

        private void EndSession()
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
