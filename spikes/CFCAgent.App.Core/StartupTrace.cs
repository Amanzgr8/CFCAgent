using System.Diagnostics;

namespace CFCAgent.App.Core;

// Writes "how long since the process started" next to a label, to the file named by the CFCAGENT_TRACE_FILE
// environment variable, so startup and shutdown can be timed from outside the app. Does nothing when the
// variable is not set, and never lets a tracing problem disturb the app.
public static class StartupTrace
{
    public const string FileVariable = "CFCAGENT_TRACE_FILE";

    public static void Mark(string what)
    {
        string? path = Environment.GetEnvironmentVariable(FileVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            using Process self = Process.GetCurrentProcess();
            double milliseconds = (DateTime.Now - self.StartTime).TotalMilliseconds;
            File.AppendAllText(path, $"{milliseconds:F0}\t{what}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Tracing is a measuring aid; it must never break the app.
        }
    }
}
