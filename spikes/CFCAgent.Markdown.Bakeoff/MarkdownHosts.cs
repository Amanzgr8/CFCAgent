using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using LiveMarkdown.Avalonia;
using MarkView.Avalonia;
using ClassicMarkdown = Markdown.Avalonia;

namespace CFCAgent.Markdown.Bakeoff;

// One small wrapper per library so the same tests can drive all of them. Every wrapper offers the same two
// ways of showing text: all at once, or piece by piece the way a model's reply arrives.
public interface IMarkdownHost
{
    string Name { get; }

    // The control to put in a window. HasOwnScroller says whether the library's control already scrolls.
    Control Control { get; }
    bool HasOwnScroller { get; }

    void ShowAll(string markdown);

    // Starts a fresh streamed reply; Append adds a piece, Complete tells the library the reply has ended.
    void BeginStream();
    void Append(string piece);
    void Complete();
}

public sealed class MarkViewHost : IMarkdownHost
{
    private readonly MarkdownViewer _viewer = new MarkdownViewer();
    private MarkdownStreamingSession? _session;

    public string Name => "MarkView.Avalonia 12.4.0";
    public Control Control => _viewer;
    public bool HasOwnScroller => true;

    public void ShowAll(string markdown) => _viewer.Markdown = markdown;

    public void BeginStream()
    {
        _viewer.Markdown = "";
        _session = _viewer.BeginStreaming();
    }

    public void Append(string piece) => _session!.Append(piece);

    public void Complete()
    {
        _session!.Dispose();
        _session = null;
    }
}

public sealed class LiveMarkdownHost : IMarkdownHost
{
    private readonly MarkdownRenderer _renderer = new MarkdownRenderer();
    private readonly ScrollViewer _scroller;
    private ObservableStringBuilder? _builder;

    public LiveMarkdownHost()
    {
        _scroller = new ScrollViewer { Content = _renderer };
    }

    public string Name => "LiveMarkdown.Avalonia 2.5.1";
    public Control Control => _scroller;
    public bool HasOwnScroller => false;

    public void ShowAll(string markdown)
    {
        _builder = new ObservableStringBuilder();
        _renderer.MarkdownBuilder = _builder;
        _builder.Append(markdown);
    }

    public void BeginStream()
    {
        _builder = new ObservableStringBuilder();
        _renderer.MarkdownBuilder = _builder;
    }

    public void Append(string piece) => _builder!.Append(piece);

    public void Complete()
    {
        // Nothing to do: the renderer re-renders on every change and has no "finished" signal.
    }
}

public sealed class ClassicMarkdownHost : IMarkdownHost
{
    private readonly ClassicMarkdown.MarkdownScrollViewer _viewer = new ClassicMarkdown.MarkdownScrollViewer();
    private readonly System.Text.StringBuilder _text = new System.Text.StringBuilder();

    public string Name => "Markdown.Avalonia 12.0.2";
    public Control Control => _viewer;
    public bool HasOwnScroller => true;

    public void ShowAll(string markdown) => _viewer.Markdown = markdown;

    public void BeginStream()
    {
        _text.Clear();
        _viewer.Markdown = "";
    }

    // This library has no streaming API: the only way to grow the text is to set the whole string again.
    public void Append(string piece)
    {
        _text.Append(piece);
        _viewer.Markdown = _text.ToString();
    }

    public void Complete()
    {
    }
}

// Reads back the words the user would see, so a test can check the table and list really made it on screen.
public static class VisibleText
{
    private static string InlineText(Avalonia.Controls.Documents.InlineCollection? inlines)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        if (inlines is null)
        {
            return string.Empty;
        }

        foreach (Avalonia.Controls.Documents.Inline inline in inlines)
        {
            switch (inline)
            {
                case Avalonia.Controls.Documents.Run run:
                    text.Append(run.Text);
                    break;
                case Avalonia.Controls.Documents.LineBreak:
                    text.Append('\n');
                    break;
                case Avalonia.Controls.Documents.Span span:
                    text.Append(InlineText(span.Inlines));
                    break;
            }
        }

        return text.ToString();
    }

    public static string Collect(Control root)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        foreach (Visual visual in root.GetVisualDescendants())
        {
            if (visual is TextBlock block)
            {
                text.Append(block.Text ?? InlineText(block.Inlines));
                text.Append('\n');
            }
            else
            {
                // Code blocks are often an editor control rather than a TextBlock.
                System.Reflection.PropertyInfo? property = visual.GetType().GetProperty("Text");
                if (property?.PropertyType == typeof(string) && property.GetValue(visual) is string value)
                {
                    text.Append(value);
                    text.Append('\n');
                }
            }
        }

        return text.ToString();
    }
}

// A plain TextBlock that just appends text: the cheapest possible "renderer". Its numbers are the floor the
// real libraries are measured against (waiting between pieces is never exactly zero).
public sealed class BaselineHost : IMarkdownHost
{
    private readonly TextBlock _block = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ScrollViewer _scroller;

    public BaselineHost()
    {
        _scroller = new ScrollViewer { Content = _block };
    }

    public string Name => "Plain TextBlock (baseline)";
    public Control Control => _scroller;
    public bool HasOwnScroller => false;

    public void ShowAll(string markdown) => _block.Text = markdown;

    public void BeginStream() => _block.Text = "";

    public void Append(string piece) => _block.Text += piece;

    public void Complete()
    {
    }
}
