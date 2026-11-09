using System.Text;
using Anthropic;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

// Console host for the backend: wires McpToolHost, ChatSession and a model together so the
// whole stack can be tested without a UI. Modes: interactive chat, --list-tools, and
// --call <tool> --args <json>, which runs read tools only (anything not declared read-only is refused),
// and --write <tool> --args <json>, which runs one write tool by hand. Uses the Anthropic model when
// ANTHROPIC_API_KEY is set, otherwise the stub. Changes are confirmed by typing yes at the console
// prompt. --read-only hides every tool the server does not declare read-only, so the model in that
// session cannot change data at all. The server's routine log lines are hidden (only audit lines
// and warnings or errors show) unless --verbose is given.
//
// Connection setup: --setup stores the database connection string in Windows Credential Manager
// (typing hidden) and writes a small tenant.json with the tenant name and product config path;
// --test-connection checks the stored connection; --forget removes both. With no tenant.json the
// CLI behaves as before and the server reads CFCCHATBOT_DB_CONNECTION_STRING from the environment.
// --tenant-config <path> uses a tenant.json somewhere other than the default location.
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

string defaultProductConfig = Path.Combine(repoRoot, "products", "cfc-fertilizer", "schema-map.yaml");
string serverDll = GetOption("--server-dll")
    ?? Path.Combine(repoRoot, "src", "CFCAgent.McpServer", "bin", "Debug", "net10.0", "CFCAgent.McpServer.dll");

bool verbose = args.Contains("--verbose");

TenantSettings tenantSettings = new TenantSettings(GetOption("--tenant-config") ?? TenantSettings.DefaultConfigPath, CredentialStoreFactory.Create());

if (args.Contains("--setup"))
{
    return RunSetup(tenantSettings, defaultProductConfig);
}

if (args.Contains("--forget"))
{
    bool removedSecret = tenantSettings.Forget();
    Console.WriteLine(removedSecret
        ? "Removed the stored database connection and the tenant setup."
        : "Removed the tenant setup (no stored database connection was found).");
    return 0;
}

ResolvedConnection resolved;
try
{
    resolved = TenantConnection.Resolve(tenantSettings, defaultProductConfig, GetOption("--product-config"));
}
catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

McpConnectionOptions connection = new McpConnectionOptions
{
    Command = "dotnet",
    Arguments = new[] { serverDll },
    ProductConfigPath = resolved.ProductConfigPath,
    DatabaseConnectionString = resolved.DatabaseConnectionString,
    ConfirmAsync = ConfirmOnConsoleAsync,
    OnServerLog = line =>
    {
        if (verbose || ServerLogFilter.IsImportant(line))
        {
            Console.Error.WriteLine($"[server] {line}");
        }
    },
};

await using McpToolHost host = await McpToolHost.ConnectAsync(connection);

bool readOnly = args.Contains("--read-only");
IReadOnlyList<AITool> tools = readOnly ? ToolFilter.ReadOnlyTools(host.Tools) : host.Tools;
string readOnlyNote = readOnly ? $"; read-only: {host.Tools.Count - tools.Count} write tool(s) hidden" : "";

if (args.Contains("--test-connection"))
{
    Console.WriteLine($"Using {resolved.Source}.");
    AIFunction? check = host.Tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == "check_database_connection");
    if (check is null)
    {
        Console.Error.WriteLine("The server did not offer check_database_connection, which means it has no database connection string.");
        return 2;
    }

    object? checkResult = await check.InvokeAsync(new AIFunctionArguments());
    Console.WriteLine(checkResult);
    return checkResult?.ToString()?.Contains("Database connection OK") == true ? 0 : 3;
}

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
    systemPrompt += ToolFilter.ReadOnlySessionNote;
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

static int RunSetup(TenantSettings settings, string defaultProductConfig)
{
    TenantConfig? existing = settings.Load();
    string defaultTenantName = existing?.TenantName ?? "default";
    string defaultConfig = existing?.ProductConfigPath ?? defaultProductConfig;

    Console.WriteLine("Set up this install's database connection. Press Enter to accept a value in [brackets].");
    Console.Write($"Tenant name [{defaultTenantName}]: ");
    string tenantName = ReadLineOrDefault(defaultTenantName);
    Console.Write($"Product config [{defaultConfig}]: ");
    string productConfigPath = ReadLineOrDefault(defaultConfig);
    string connectionString = ReadSecret("Database connection string (typing is hidden): ");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("No connection string entered. Nothing was saved.");
        return 1;
    }

    settings.Save(new TenantConfig { TenantName = tenantName, ProductConfigPath = productConfigPath }, connectionString);
    Console.WriteLine($"Saved. The connection string is in Windows Credential Manager under '{TenantSettings.CredentialTarget(tenantName)}'.");
    Console.WriteLine($"{settings.ConfigPath} holds only the tenant name and the product config path.");
    Console.WriteLine("Next, run with --test-connection to check it.");
    return 0;
}

static string ReadLineOrDefault(string defaultValue)
{
    string? entered = Console.ReadLine();
    return string.IsNullOrWhiteSpace(entered) ? defaultValue : entered.Trim();
}

// Reads a secret without echoing it. In a terminal that cannot hide typing (for example Git Bash,
// whose input is not a real Windows console) it falls back to a visible line and says so.
static string ReadSecret(string prompt)
{
    Console.Write(prompt);

    if (!Console.IsInputRedirected)
    {
        try
        {
            StringBuilder typed = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return typed.ToString().Trim();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (typed.Length > 0)
                    {
                        typed.Length--;
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    typed.Append(key.KeyChar);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    Console.WriteLine();
    Console.WriteLine("This terminal cannot hide typing, so what you type will be visible. For privacy, run --setup from PowerShell or Windows Terminal.");
    Console.Write(prompt);
    return (Console.ReadLine() ?? string.Empty).Trim();
}

static Task<bool> ConfirmOnConsoleAsync(string message, CancellationToken cancellationToken)
{
    Console.WriteLine();
    Console.WriteLine(message);
    Console.Write("Apply this change? Type 'yes' to confirm: ");
    string? answer = Console.ReadLine();
    return Task.FromResult(string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase));
}
