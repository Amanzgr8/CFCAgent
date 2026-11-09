using CFCAgent.App.Core;

namespace CFCAgent.App.Core.Tests;

// Only this class touches CFCAGENT_TRACE_FILE, so setting it here cannot disturb another test.
public class StartupTraceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "cfcagent-trace-" + Guid.NewGuid().ToString("N") + ".txt");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(StartupTrace.FileVariable, null);
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    [Fact]
    public void Mark_WritesTheElapsedTimeAndTheLabel_WhenTheVariableIsSet()
    {
        Environment.SetEnvironmentVariable(StartupTrace.FileVariable, _file);

        StartupTrace.Mark("first");
        StartupTrace.Mark("second");

        string[] lines = File.ReadAllLines(_file);
        Assert.Equal(2, lines.Length);
        string[] parts = lines[0].Split('\t');
        Assert.True(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) >= 0);
        Assert.Equal("first", parts[1]);
        Assert.EndsWith("second", lines[1]);
    }

    [Fact]
    public void Mark_DoesNothing_WhenTheVariableIsNotSet()
    {
        Environment.SetEnvironmentVariable(StartupTrace.FileVariable, null);

        StartupTrace.Mark("ignored");

        Assert.False(File.Exists(_file));
    }

    [Fact]
    public void Mark_NeverThrows_WhenTheFileCannotBeWritten()
    {
        Environment.SetEnvironmentVariable(StartupTrace.FileVariable, Path.Combine(_file, "not", "a", "folder", "trace.txt"));

        StartupTrace.Mark("nowhere to go");
    }
}
