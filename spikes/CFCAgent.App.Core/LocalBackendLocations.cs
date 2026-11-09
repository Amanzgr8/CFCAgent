namespace CFCAgent.App.Core;

// Where the pieces of a local backend live, in the development layout: the repository root holds CFCAgent.sln,
// the MCP server is built under src/, and each product's config is under products/. A shipped app would
// bundle the server and its product configs instead, and this is the one place that would change.
public static class LocalBackendLocations
{
    public static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CFCAgent.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (CFCAgent.sln), so the MCP server cannot be found.");
    }

    public static string DefaultProductConfigPath()
    {
        return Path.Combine(FindRepoRoot(), "products", "cfc-fertilizer", "schema-map.yaml");
    }

    // Uses the same build configuration as the running app (Debug beside Debug), falling back to Debug.
    public static string DefaultServerDll()
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name is "Release" ? "Release" : "Debug";
        return Path.Combine(FindRepoRoot(), "src", "CFCAgent.McpServer", "bin", configuration, "net10.0", "CFCAgent.McpServer.dll");
    }
}
