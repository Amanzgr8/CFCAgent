using Anthropic;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

const string DefaultModel = "claude-sonnet-5-5";

string? FindRepoRoot()
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

    return null;
}

string? GetOption(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

string? repoRoot = FindRepoRoot();
if (repoRoot is null)
{
    Console.Error.WriteLine("Could not locate the repository root (CFCAgent.sln).");
    return 1;
}

string productConfig = GetOption("--product-config")
    ?? Path.Combine(repoRoot, "products", "cfc-fertilizer", "schema-map.yaml");
string serverDll = GetOption("--server-dll")
    ?? Path.Combine(repoRoot, "src", "CFCAgent.McpServer", "bin", "Debug", "net10.0", "CFCAgent.McpServer.dll");

McpConnectionOptions connection = new McpConnectionOptions
{
    Command = "dotnet",
    Arguments = new[] { serverDll },
    ProductConfigPath = productConfig,
    ConfirmAsync = ConfirmOnConsoleAsync,
    OnServerLog = line => Console.Error.WriteLine($"[server] {line}"),
};

await using McpToolHost host = await McpToolHost.ConnectAsync(connection);

if (args.Contains("--list-tools"))
{
    foreach (AITool tool in host.Tools)
    {
        Console.WriteLine(tool.Name);
    }

    Console.WriteLine($"({host.Tools.Count} tools; instructions {host.ServerInstructions?.Length ?? 0} chars)");
    return 0;
}

string? callName = GetOption("--call");
if (callName is not null)
{
    // Verification aid: write tools are refused here so this path can never change data.
    if (callName.StartsWith("update_", StringComparison.Ordinal) || callName.StartsWith("set_", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("--call only runs read tools.");
        return 2;
    }

    AIFunction? function = host.Tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == callName);
    if (function is null)
    {
        Console.Error.WriteLine($"No such tool: {callName}");
        return 2;
    }

    string json = GetOption("--args") ?? "{}";
    Dictionary<string, object?> parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new Dictionary<string, object?>();
    object? result = await function.InvokeAsync(new AIFunctionArguments(parsed));
    Console.WriteLine(result);
    return 0;
}

string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
IChatClient chatClient;
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("ANTHROPIC_API_KEY is not set: using the stub model (it only echoes). Tools are still connected.");
    chatClient = new StubChatClient();
}
else
{
    string model = GetOption("--model") ?? DefaultModel;
    chatClient = new AnthropicClient().AsIChatClient(model, 4096);
}

ChatSession session = new ChatSession(chatClient, host.Tools, host.ServerInstructions);
Console.WriteLine($"Connected: {host.Tools.Count} tools. Type a message, or 'exit' to quit.");

while (true)
{
    Console.Write("> ");
    string? line = Console.ReadLine();
    if (line is null || line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (line.Trim().Length == 0)
    {
        continue;
    }

    Console.WriteLine(await session.SendAsync(line));
}

return 0;

static Task<bool> ConfirmOnConsoleAsync(string message, CancellationToken cancellationToken)
{
    Console.WriteLine();
    Console.WriteLine(message);
    Console.Write("Apply this change? Type 'yes' to confirm: ");
    string? answer = Console.ReadLine();
    return Task.FromResult(string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase));
}
