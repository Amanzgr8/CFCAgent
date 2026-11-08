namespace CFCAgent.Backend;

// Decides which of the MCP server's stderr lines a console host shows by default. The server
// logs about twenty routine "info:" lines per call, which buries the few that matter, so only
// audit lines (what was or was not changed) and warnings, errors and crashes are kept. A host
// can still show everything with a verbose switch.
public static class ServerLogFilter
{
    public static bool IsImportant(string line)
    {
        return line.StartsWith("[Audit]", StringComparison.Ordinal)
            || line.StartsWith("warn:", StringComparison.Ordinal)
            || line.StartsWith("fail:", StringComparison.Ordinal)
            || line.StartsWith("crit:", StringComparison.Ordinal);
    }
}
