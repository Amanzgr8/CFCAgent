namespace CFCAgent.Backend;

// Settings needed to start the MCP server as a child process: the command and arguments
// that launch it, an optional working directory, the product config path, an optional database
// connection string, and two optional callbacks: one that asks the user to confirm a change, one
// that receives the server's log lines. The backend is handed these values; it never references
// the server project.
public sealed class McpConnectionOptions
{
    public required string Command { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public required string ProductConfigPath { get; init; }

    // Handed to the server process only, through its own environment. Null leaves whatever the
    // server inherits from its parent.
    public string? DatabaseConnectionString { get; init; }

    public Func<string, CancellationToken, Task<bool>>? ConfirmAsync { get; init; }

    public Action<string>? OnServerLog { get; init; }
}
