using Anthropic;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

// Console host for the backend: wires McpToolHost, ChatSession and a model together so the
// whole stack can be tested without a UI. Modes: interactive chat, --list-tools, and
// --call <tool> --args <json>, which runs read tools only (anything not declared read-only is refused),
// and --write <tool> --args <json>, which runs one write tool by hand. Uses the Anthropic model when
// ANTHROPIC_API_KEY is set, otherwise the stub. Changes are confirmed by typing yes at the console
// prompt. --read-only hides every tool the server does not declare read-only, so the model in that
// session cannot change data at all.
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

bool readOnly = args.Contains("--read-only");
IReadOnlyList<AITool> tools = readOnly ? ToolFilter.ReadOnlyTools(host.Tools) : host.Tools;
string readOnlyNote = readOnly ? $"; read-only: {host.Tools.Count - tools.Count} write tool(s) hidden" : "";

if (args.Contains("--list-tools"))
{
    foreach (AITool tool in tools)
    {
        Console.WriteLine(tool.Name);
    }

    Console.WriteLine($"({tools.Count} tools; instructions {host.ServerInstructions?.Length ?? 0} chars{readOnlyNote})");
    return 0;
}

string? callName = GetOption("--call");
string? writeName = GetOption("--write");
if (callName is not null || writeName is not null)
{
    string toolName = (callName ?? writeName)!;

    // --call is a verification aid that can never change data: it refuses anything the server
    // does not declare read-only. --write is the explicit way to run a write tool: without
    // "confirm": true in --args it only previews, and with it the server asks for approval,
    // which shows up here as the console prompt (type yes to apply).
    AIFunction? function = tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolName);
    if (function is null)
    {
        Console.Error.WriteLine(readOnly && writeName is not null
            ? "--write cannot be combined with --read-only."
            : $"No such tool: {toolName}");
        return 2;
    }

    bool declaredReadOnly = ToolFilter.IsDeclaredReadOnly(function);
    if (callName is not null && !declaredReadOnly)
    {
        Console.Error.WriteLine("--call only runs read tools. Use --write for tools that change data.");
        return 2;
    }

    if (writeName is not null && declaredReadOnly)
    {
        Console.Error.WriteLine("--write is for tools that change data. Use --call for read tools.");
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

string? systemPrompt = host.ServerInstructions;
if (readOnly)
{
    systemPrompt += "\n\n## This session is read-only\n\nNo write tools are available in this session, whatever the instructions above say. " +
                    "If the user asks for a change, say you cannot make it here and tell them how to make it in the app.";
}

ChatSession session = new ChatSession(chatClient, tools, systemPrompt);
Console.WriteLine($"Connected: {tools.Count} tools{(readOnly ? " (read-only session)" : "")}. Type a message, or 'exit' to quit.");

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
