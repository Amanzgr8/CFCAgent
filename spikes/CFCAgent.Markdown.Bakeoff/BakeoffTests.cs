using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CFCAgent.Markdown.Bakeoff;

// Shows the same assistant reply with each library and records what it looked like and how long it took.
// The numbers are for comparing the libraries with each other on this machine, not absolute targets.
//
// Two of the libraries batch their updates on a short timer, so a test cannot just render once and look:
// it has to let real time pass while the screen is being kept up to date, which is what Pump does.
public class BakeoffTests
{
    public static TheoryData<string> Libraries => new TheoryData<string> { "markview", "live", "classic", "baseline" };

    private static IMarkdownHost CreateHost(string key)
    {
        return key switch
        {
            "markview" => new MarkViewHost(),
            "live" => new LiveMarkdownHost(),
            "classic" => new ClassicMarkdownHost(),
            "baseline" => new BaselineHost(),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    private static string Sample => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sample-reply.md"));

    // Where screenshots and the results file go. Set CFCAGENT_SCREENSHOT_DIR to collect them somewhere specific.
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

    // A reply is a few hundred pixels wide in the real chat window; 760 is the width the spike's window gives it.
    private static Window ShowInWindow(IMarkdownHost host, double height)
    {
        Window window = new Window { Width = 760, Height = height, Content = host.Control };
        window.Show();
        return window;
    }

    // Lays out and draws everything that is due, the way one frame would. Returns how long that took.
    private static double RenderFrame(Window window)
    {
        Stopwatch watch = Stopwatch.StartNew();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return watch.Elapsed.TotalMilliseconds;
    }

    // Lets real time pass while frames keep being produced. The await is what lets the application's own
    // dispatcher run, so the libraries' update timers fire. Returns how long it really took: when the screen
    // work takes longer than the time asked for, the difference is the delay a user would feel.
    private static async Task<double> Pump(Window window, double milliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        do
        {
            RenderFrame(window);
            await Task.Delay(1);
        }
        while (clock.Elapsed.TotalMilliseconds < milliseconds);
        RenderFrame(window);
        return clock.Elapsed.TotalMilliseconds;
    }

    // Pumps until the condition holds (or 10 seconds pass); returns how long that took, or -1 on timeout.
    private static async Task<double> TimeUntil(Window window, Func<bool> condition)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 10)
        {
            RenderFrame(window);
            if (condition())
            {
                return clock.Elapsed.TotalMilliseconds;
            }

            await Task.Delay(1);
        }

        return -1;
    }

    private static async Task<string> Save(Window window, string name)
    {
        await Pump(window, 100);
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string path = Path.Combine(OutputDirectory, name + ".png");
        frame.Save(path, new PngBitmapEncoderOptions());
        return path;
    }

    private static void Record(string line)
    {
        string path = Path.Combine(OutputDirectory, "bakeoff-results.txt");
        lock (typeof(BakeoffTests))
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static string Ms(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    // The reply cut into pieces of 1 to 8 characters, in a fixed pattern, to stand in for model tokens.
    private static List<string> Chunks(string text)
    {
        List<string> pieces = new List<string>();
        int[] sizes = { 3, 5, 2, 7, 4, 1, 6, 8, 3, 4 };
        int position = 0;
        int index = 0;
        while (position < text.Length)
        {
            int size = Math.Min(sizes[index++ % sizes.Length], text.Length - position);
            pieces.Add(text.Substring(position, size));
            position += size;
        }

        return pieces;
    }

    private const string LastWords = "grouped by customer";

    private static readonly string[] MustAppear =
    {
        "Formulas affected by the D066 cost change",
        "Cost per ton (current)",
        "F1001",
        "F1050",
        "Open the formula in the app.",
        "Compare the new cost per ton",
        "SUM(pi.solution)",
        LastWords,
    };

    private static void AssertEverythingShown(IMarkdownHost host, string where, string picture)
    {
        string visible = VisibleText.Collect(host.Control);
        foreach (string expected in MustAppear)
        {
            Assert.True(visible.Contains(expected), $"{host.Name} {where} is missing \"{expected}\" (see {picture})");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Libraries))]
    public async Task WholeReply_ShowsEverythingAndIsTimed(string key)
    {
        IMarkdownHost host = CreateHost(key);
        Window window = ShowInWindow(host, 3600);
        await Pump(window, 50);

        // The clock starts when the text is handed over and stops when the last words are on screen.
        host.ShowAll(Sample);
        double untilLastWords = await TimeUntil(window, () => VisibleText.Collect(host.Control).Contains(LastWords));

        string picture = await Save(window, key + "-whole");
        Record($"{host.Name}: whole reply on screen after {Ms(untilLastWords)} ms (-1 means it never appeared)");
        AssertEverythingShown(host, "whole reply", picture);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Libraries))]
    public async Task StreamedReply_EndsUpCompleteAndIsTimed(string key)
    {
        IMarkdownHost host = CreateHost(key);
        Window window = ShowInWindow(host, 3600);
        await Pump(window, 50);

        List<string> pieces = Chunks(Sample);
        List<double> perPiece = new List<double>(pieces.Count);
        int sent = 0;
        string? midway = null;
        Stopwatch everything = Stopwatch.StartNew();
        TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;

        host.BeginStream();
        foreach (string piece in pieces)
        {
            // Aim for one piece every 15 ms (about 65 a second, a fast model). A piece that takes longer than
            // that is the UI falling behind the model: the excess is what a user would see as lag.
            Stopwatch one = Stopwatch.StartNew();
            host.Append(piece);
            await Pump(window, 15);
            perPiece.Add(one.Elapsed.TotalMilliseconds);
            sent += piece.Length;

            // A picture partway through the table, where half-finished syntax is hardest.
            if (midway is null && sent > 1100)
            {
                midway = await Save(window, key + "-streaming-midway");
            }
        }

        double streamingTime = everything.Elapsed.TotalMilliseconds;
        double cpuMilliseconds = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalMilliseconds;
        host.Complete();
        double untilLastWords = await TimeUntil(window, () => VisibleText.Collect(host.Control).Contains(LastWords));
        string picture = await Save(window, key + "-streamed-final");

        List<double> sorted = perPiece.OrderBy(value => value).ToList();
        Record($"{host.Name}: streamed {pieces.Count} pieces in {Ms(streamingTime)} ms, processor time used {Ms(cpuMilliseconds)} ms ({Ms(cpuMilliseconds / pieces.Count)} per piece) " +
               $"(time per piece: median {Ms(sorted[sorted.Count / 2])}, 95th percentile {Ms(sorted[(int)(sorted.Count * 0.95)])}, " +
               $"worst {Ms(sorted[^1])} ms; the wait itself is about 15); " +
               $"last words on screen {Ms(untilLastWords)} ms after the final piece");
        AssertEverythingShown(host, "streamed reply", picture);
    }
}
