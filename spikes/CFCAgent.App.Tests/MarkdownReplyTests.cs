using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CFCAgent.App.Core;

namespace CFCAgent.App.Tests;

// Checks that an assistant reply written in Markdown shows up formatted in the real chat window,
// including while it is still arriving, and that the user's own words are never turned into Markdown.
public class MarkdownReplyTests
{
    private const string Reply =
        "# Formulas using D066\n\n" +
        "Raising **D066** to `0.43` per pound touches *three* formulas:\n\n" +
        "| Formula | Grade | Cost per ton |\n|---|---|---:|\n" +
        "| F1001 | 10-10-10 | 549.42 |\n| F1002 | 28-0-0 | 478.53 |\n| F1003 | 8-32-16 | 328.15 |\n\n" +
        "- Re-formulate each one first.\n- Then compare the new cost per ton.\n\n" +
        "```sql\nSELECT productname FROM product;\n```\n\n" +
        "Want these grouped by customer?";

    private static string ScreenshotDirectory
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("CFCAGENT_SCREENSHOT_DIR");
            string directory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "screenshots")
                : configured;
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    private static void RenderFrame(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    // Lets real time pass (the viewer's update timer needs it) while frames keep being produced.
    private static async Task PumpAsync(MainWindow window, double milliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        do
        {
            RenderFrame(window);
            await Task.Delay(1);
        }
        while (clock.Elapsed.TotalMilliseconds < milliseconds);
        RenderFrame(window);
    }

    private static async Task<bool> WaitForAsync(MainWindow window, Func<bool> condition)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 5)
        {
            RenderFrame(window);
            if (condition())
            {
                return true;
            }

            await Task.Delay(1);
        }

        return false;
    }

    private static string InlineText(InlineCollection? inlines)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        foreach (Inline inline in inlines ?? new InlineCollection())
        {
            switch (inline)
            {
                case Run run:
                    text.Append(run.Text);
                    break;
                case LineBreak:
                    text.Append('\n');
                    break;
                case Span span:
                    text.Append(InlineText(span.Inlines));
                    break;
            }
        }

        return text.ToString();
    }

    // The words a person would see on screen (the Markdown library builds ordinary text blocks and editors).
    private static string VisibleText(Control root)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        foreach (Visual visual in root.GetVisualDescendants())
        {
            if (visual is TextBlock block)
            {
                if (block.IsVisible && block.IsEffectivelyVisible)
                {
                    text.Append(block.Text ?? InlineText(block.Inlines)).Append('\n');
                }
            }
            else if (visual.IsEffectivelyVisible
                     && visual.GetType().GetProperty("Text") is { } property
                     && property.PropertyType == typeof(string)
                     && property.GetValue(visual) is string value)
            {
                text.Append(value).Append('\n');
            }
        }

        return text.ToString();
    }

    private static string Capture(MainWindow window, string name)
    {
        RenderFrame(window);
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string path = Path.Combine(ScreenshotDirectory, name + ".png");
        frame.Save(path, new PngBitmapEncoderOptions());
        return path;
    }

    [AvaloniaFact]
    public async Task StreamedMarkdownReply_IsShownFormatted()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel, Width = 900, Height = 900 };
        window.Show();

        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.User, "Which formulas use D066?"));
        ChatMessageViewModel reply = new ChatMessageViewModel(ChatAuthor.Assistant, "") { IsStreaming = true };
        viewModel.Messages.Add(reply);

        // About 6 characters every 10 ms, like a quick model.
        for (int position = 0; position < Reply.Length; position += 6)
        {
            reply.Text += Reply.Substring(position, Math.Min(6, Reply.Length - position));
            await PumpAsync(window, 10);
        }

        reply.IsStreaming = false;
        bool arrived = await WaitForAsync(window, () => VisibleText(window).Contains("grouped by customer"));
        string picture = Capture(window, "chat-markdown-reply");
        string visible = VisibleText(window);

        Assert.True(arrived, $"the end of the reply never appeared (see {picture})");
        foreach (string expected in new[] { "Formulas using D066", "F1003", "328.15", "Re-formulate each one first.", "SELECT productname FROM product;" })
        {
            Assert.True(visible.Contains(expected), $"missing \"{expected}\" (see {picture})");
        }

        // Formatted, not raw: the Markdown markers must not be on screen.
        Assert.DoesNotContain("**D066**", visible);
        Assert.DoesNotContain("|---|", visible);
        Assert.DoesNotContain("```", visible);
    }

    [AvaloniaFact]
    public async Task UsersOwnWords_AreNeverTurnedIntoMarkdown()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel, Width = 900, Height = 600 };
        window.Show();

        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.User, "Set **D066** to `0.43`"));
        await PumpAsync(window, 100);

        Assert.Contains("Set **D066** to `0.43`", VisibleText(window));
    }

    [AvaloniaFact]
    public async Task FinishedAssistantMessage_IsShownAtOnce()
    {
        ChatViewModel viewModel = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = viewModel, Width = 900, Height = 600 };
        window.Show();

        viewModel.Messages.Add(new ChatMessageViewModel(ChatAuthor.Assistant, "Done: **three** formulas."));
        bool shown = await WaitForAsync(window, () => VisibleText(window).Contains("Done: three formulas."));

        Assert.True(shown, "the finished message was not shown formatted");
    }
}
