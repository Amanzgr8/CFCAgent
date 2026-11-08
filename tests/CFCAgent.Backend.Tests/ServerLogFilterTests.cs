using CFCAgent.Backend;

namespace CFCAgent.Backend.Tests;

public class ServerLogFilterTests
{
    [Theory]
    [InlineData("[Audit] 2026-11-08T22:38:14+00:00 update_ingredient ingredient[D066]: owncost 0.33 -> 0.34")]
    [InlineData("warn: Microsoft.Hosting.Lifetime[0]")]
    [InlineData("fail: ModelContextProtocol.Server.McpServer[1433779783]")]
    [InlineData("crit: Microsoft.Hosting.Lifetime[0]")]
    public void IsImportant_AuditLinesAndProblems_AreKept(string line)
    {
        Assert.True(ServerLogFilter.IsImportant(line));
    }

    [Theory]
    [InlineData("info: ModelContextProtocol.Server.McpServer[570385771]")]
    [InlineData("      Server (stream) (CFCAgent.McpServer) transport reading messages.")]
    [InlineData("[Startup] Loaded 13 read tool(s) and 7 write tool(s) for product 'cfc-fertilizer'.")]
    [InlineData("")]
    public void IsImportant_RoutineLines_AreHidden(string line)
    {
        Assert.False(ServerLogFilter.IsImportant(line));
    }
}
