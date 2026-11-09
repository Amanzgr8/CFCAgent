using Anthropic;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core;

public sealed class LocalBackendOptions
{
    public const string DefaultModel = "claude-sonnet-5-5";

    // Read-only is the default: the model is not even offered the tools that change data. Allowing writes
    // needs the approval dialog (spike step 5), so nothing in the app turns this off yet.
    public bool ReadOnly { get; init; } = true;

    public string Model { get; init; } = DefaultModel;

    // Where tenant.json lives. Null means the usual per-user location.
    public string? TenantConfigPath { get; init; }

    // Null means the real Windows (or other OS) credential store; tests supply an in-memory one.
    public ICredentialStore? Credentials { get; init; }

    // The built MCP server. Null means the Debug/Release build next to the repository (a development
    // arrangement; a shipped app would bundle the server).
    public string? ServerDllPath { get; init; }

    public string? ProductConfigPath { get; init; }

    // Use the echo-only stub even when ANTHROPIC_API_KEY is set (tests).
    public bool ForceStubModel { get; init; }

    // How the user is asked to approve a change (the approval dialog). Only used when ReadOnly is false: a
    // read-only session never registers it, so the server is never told this client can be asked, and it
    // refuses every change.
    public Func<string, CancellationToken, Task<bool>>? ConfirmAsync { get; init; }
}

// Connects the app to a local backend the way the console host does: reads the tenant setup (the connection
// string comes from the credential store), launches the MCP server as a child process, and builds a chat
// session on Claude when ANTHROPIC_API_KEY is set, or on the echo-only stub when it is not. It holds no secret
// itself: the connection string goes straight to the server's environment and nowhere else.
public sealed class LocalBackendConnector : IChatConnector
{
    private readonly LocalBackendOptions _options;

    public LocalBackendConnector(LocalBackendOptions options)
    {
        _options = options;
    }

    public async Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        string root = FindRepoRoot();
        string serverDll = _options.ServerDllPath ?? DefaultServerDll(root);
        if (!File.Exists(serverDll))
        {
            throw new FileNotFoundException($"The MCP server has not been built: {serverDll} does not exist. Build the solution first.");
        }

        TenantSettings settings = new TenantSettings(
            _options.TenantConfigPath ?? TenantSettings.DefaultConfigPath,
            _options.Credentials ?? CredentialStoreFactory.Create());
        string defaultProductConfig = Path.Combine(root, "products", "cfc-fertilizer", "schema-map.yaml");
        ResolvedConnection resolved = TenantConnection.Resolve(settings, defaultProductConfig, _options.ProductConfigPath);

        ServerLogBuffer log = new ServerLogBuffer();
        McpToolHost host;
        try
        {
            host = await McpToolHost.ConnectAsync(
                new McpConnectionOptions
                {
                    Command = "dotnet",
                    Arguments = new[] { serverDll },
                    ProductConfigPath = resolved.ProductConfigPath,
                    DatabaseConnectionString = resolved.DatabaseConnectionString,

                    // Only a session that allows changes registers the approval callback. Without one the server is
                    // never told the client can ask the user, so it refuses every change: even a write tool that
                    // slipped into a read-only session would fail closed.
                    ConfirmAsync = _options.ReadOnly ? null : _options.ConfirmAsync,
                    OnServerLog = log.Add,
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string printed = string.Join(" | ", log.RecentLines(3));
            throw new InvalidOperationException(
                printed.Length == 0
                    ? $"The MCP server could not be started: {ex.Message}"
                    : $"The MCP server could not be started: {ex.Message} (server said: {printed})",
                ex);
        }

        try
        {
            IReadOnlyList<AITool> tools = _options.ReadOnly ? ToolFilter.ReadOnlyTools(host.Tools) : host.Tools;
            string? systemPrompt = host.ServerInstructions;
            if (_options.ReadOnly)
            {
                systemPrompt += ToolFilter.ReadOnlySessionNote;
            }

            (IChatClient client, string modelNote) = CreateModelClient();
            ChatSession session = new ChatSession(client, tools, systemPrompt);

            string toolNote;
            if (_options.ReadOnly)
            {
                toolNote = $"{tools.Count} tools, read-only ({host.Tools.Count - tools.Count} write tools hidden)";
            }
            else if (_options.ConfirmAsync is not null)
            {
                toolNote = $"{tools.Count} tools, changes allowed (each one needs your approval)";
            }
            else
            {
                toolNote = $"{tools.Count} tools, but no approval prompt is available, so every change will be refused";
            }

            string description = $"{resolved.Source} · {toolNote} · {modelNote}";

            return new ChatConnection(session, description, tools, log, host.DisposeAsync);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private (IChatClient Client, string Note) CreateModelClient()
    {
        string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (_options.ForceStubModel || string.IsNullOrWhiteSpace(apiKey))
        {
            return (new StubChatClient(TimeSpan.FromMilliseconds(40)), "stub model (ANTHROPIC_API_KEY is not set)");
        }

        return (new AnthropicClient().AsIChatClient(_options.Model, 4096), $"model {_options.Model}");
    }

    // Development layout: the repository root holds CFCAgent.sln, and the server is built under src/.
    private static string FindRepoRoot()
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

    // Uses the same build configuration as the running app (Debug beside Debug), falling back to Debug.
    private static string DefaultServerDll(string root)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name is "Release" ? "Release" : "Debug";
        return Path.Combine(root, "src", "CFCAgent.McpServer", "bin", configuration, "net10.0", "CFCAgent.McpServer.dll");
    }
}
