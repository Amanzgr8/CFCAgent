using CFCAgent.Backend;

namespace CFCAgent.Backend.Tests;

// Launches the real, built MCP server through McpToolHost for the integration tests. The test
// project builds the server before it runs (a build-order-only project reference), and finds it
// under the same Debug or Release folder the tests themselves run from.
internal static class TestServer
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

        throw new InvalidOperationException("Could not locate the repository root (CFCAgent.sln).");
    }

    public static Task<McpToolHost> ConnectAsync(
        Func<string, CancellationToken, Task<bool>>? confirm,
        Action<string>? onServerLog = null,
        string? databaseConnectionString = null)
    {
        string root = FindRepoRoot();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string serverDll = Path.Combine(root, "src", "CFCAgent.McpServer", "bin", configuration, "net10.0", "CFCAgent.McpServer.dll");

        return McpToolHost.ConnectAsync(new McpConnectionOptions
        {
            Command = "dotnet",
            Arguments = new[] { serverDll },
            ProductConfigPath = Path.Combine(root, "products", "cfc-fertilizer", "schema-map.yaml"),
            DatabaseConnectionString = databaseConnectionString,
            ConfirmAsync = confirm,
            OnServerLog = onServerLog,
        });
    }
}
