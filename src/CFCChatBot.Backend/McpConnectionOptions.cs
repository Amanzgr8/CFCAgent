namespace CFCChatBot.Backend;

public sealed class McpConnectionOptions
{
    public required string Command { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public required string ProductConfigPath { get; init; }

    public Func<string, CancellationToken, Task<bool>>? ConfirmAsync { get; init; }

    public Action<string>? OnServerLog { get; init; }
}
