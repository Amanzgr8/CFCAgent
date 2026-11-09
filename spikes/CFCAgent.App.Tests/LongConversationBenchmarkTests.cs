using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CFCAgent.App.Core;

namespace CFCAgent.App.Tests;

// Measures the real chat window with a long conversation of table-heavy replies: how long it takes to lay out,
// how much memory it holds, and what a reply costs to stream when many earlier replies are already on screen.
// Opt-in (it takes about a minute): set CFCAGENT_BENCH=1. Results are appended to bench-results.txt in the
// screenshot folder. The numbers are for judging the framework, not hard limits, so only completion is asserted.
public class LongConversationBenchmarkTests
{
    // A reply like the ones the assistant gives: a heading, a paragraph, a 50-row table, a list and a code block.
    private static string SampleReply()
    {
        StringBuilder text = new StringBuilder();
        text.Append("# Formulas affected by the D066 cost change\n\n");
        text.Append("Raising the cost of **D066** from `0.41` to `0.43` per pound touches **50 formulas**.\n\n");
        text.Append("| Formula | Grade | Customer | Cost per ton (current) |\n|---|---|---|---:|\n");
        for (int row = 1; row <= 50; row++)
        {
            text.Append(CultureInfo.InvariantCulture, $"| F{1000 + row} | 10-10-10 | Riverbend Ag | {300 + row}.42 |\n");
        }

        text.Append("\n- Re-formulate each one first.\n- Then compare the new cost per ton.\n\n");
        text.Append("```sql\nSELECT productname FROM product WHERE ingredientid = 66;\n```\n\nWant these grouped by customer?");
        return text.ToString();
    }

    private static string OutputDirectory
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

    private static double Frame(MainWindow window)
    {
        Stopwatch watch = Stopwatch.StartNew();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return watch.Elapsed.TotalMilliseconds;
    }

    private static async Task PumpAsync(MainWindow window, double milliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        do
        {
            Frame(window);
            await Task.Delay(1);
        }
        while (clock.Elapsed.TotalMilliseconds < milliseconds);
    }

    private static long ManagedMegabytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true) / (1024 * 1024);
    }

    private static string Ms(double value) => value.ToString("F0", CultureInfo.InvariantCulture);

    [AvaloniaTheory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task ConversationOfTableHeavyReplies_LaysOutAndStreamsWithoutFallingBehind(int replies)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CFCAGENT_BENCH") == "1", "Set CFCAGENT_BENCH=1 to run the benchmark.");

        string reply = SampleReply();
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        MainWindow window = new MainWindow { DataContext = chat, Width = 900, Height = 700 };
        window.Show();
        await PumpAsync(window, 100);
        long managedBefore = ManagedMegabytes();

        // Cold: hand over all the finished replies and lay the window out once.
        Stopwatch cold = Stopwatch.StartNew();
        for (int i = 0; i < replies; i++)
        {
            chat.Messages.Add(new ChatMessageViewModel(ChatAuthor.User, $"Question number {i}"));
            chat.Messages.Add(new ChatMessageViewModel(ChatAuthor.Assistant, reply));
        }

        Frame(window);
        double firstLayoutMs = cold.Elapsed.TotalMilliseconds;
        await PumpAsync(window, 500);

        // Idle: a frame with nothing changed.
        double idleFrameMs = Frame(window);

        long managedAfter = ManagedMegabytes();
        int visuals = window.GetVisualDescendants().Count();
        using Process self = Process.GetCurrentProcess();
        long workingSetMb = self.WorkingSet64 / (1024 * 1024);

        // Streaming one more reply below all of them, a frame after every piece (the worst case).
        ChatMessageViewModel streaming = new ChatMessageViewModel(ChatAuthor.Assistant, "") { IsStreaming = true };
        chat.Messages.Add(streaming);
        List<double> frames = new List<double>();
        for (int position = 0; position < reply.Length; position += 6)
        {
            streaming.Text += reply.Substring(position, Math.Min(6, reply.Length - position));
            frames.Add(Frame(window));
            await Task.Delay(10);
            frames.Add(Frame(window));
        }

        streaming.IsStreaming = false;
        await PumpAsync(window, 300);
        frames.Sort();
        double median = frames[frames.Count / 2];
        double p95 = frames[(int)(frames.Count * 0.95)];
        double worst = frames[^1];

        string line = $"{replies} replies of 3.7 KB with a 50-row table ({replies * 2} messages, {visuals} visual elements): " +
                      $"first layout {Ms(firstLayoutMs)} ms, idle frame {Ms(idleFrameMs)} ms, " +
                      $"managed memory {managedBefore} -> {managedAfter} MB ({managedAfter - managedBefore} MB for the conversation), " +
                      $"process working set {workingSetMb} MB; " +
                      $"streaming one more reply: frame median {Ms(median)} ms, 95th percentile {Ms(p95)} ms, worst {Ms(worst)} ms";
        File.AppendAllText(Path.Combine(OutputDirectory, "bench-results.txt"), line + Environment.NewLine);

        Assert.Equal(replies * 2 + 1, chat.Messages.Count);
    }
}
