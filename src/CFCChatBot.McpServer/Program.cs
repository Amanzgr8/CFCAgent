using CFCChatBot.McpServer.Config;
using CFCChatBot.McpServer.Docs;
using CFCChatBot.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Npgsql;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for MCP protocol messages, so route logs to stderr.
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Which product this server instance serves. One product config = one schema map, one
// set of tools, one set of ground-rule instructions - see products/<name>/schema-map.yaml.
// Swapping products (or pointing at a different installation's config) is just changing
// these two settings; no code changes.
string productConfigPath = Environment.GetEnvironmentVariable("CFCCHATBOT_PRODUCT_CONFIG")
    ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "products", "cfc-fertilizer", "schema-map.yaml"));

McpServerTool[] dynamicTools = Array.Empty<McpServerTool>();
McpServerTool[] writeTools = Array.Empty<McpServerTool>();
string? serverInstructions = null;

if (!File.Exists(productConfigPath))
{
    Console.Error.WriteLine(
        $"[Startup] Product config not found at {productConfigPath}. Set CFCCHATBOT_PRODUCT_CONFIG to the schema-map.yaml path. No schema-map-driven tools, docs, or instructions will be registered.");
}
else
{
    string productDirectory = Path.GetDirectoryName(productConfigPath)!;
    ProductConfig productConfig = SchemaMapLoader.Load(productConfigPath);

    // instructions.md becomes this MCP server's own protocol-level "instructions" -
    // most clients surface it to the model automatically on connect, the same way we
    // saw cfc-postgres's instructions appear here. This is the "always in the prompt"
    // half of the hybrid doc strategy; search_docs/read_section (registered below via
    // WithToolsFromAssembly) are the "fetched on demand" half. schema-map.yaml's
    // ground_rules get appended too, so that list is the single source of truth for
    // result-interpretation rules instead of being hand-duplicated into instructions.md.
    string instructionsPath = Path.Combine(productDirectory, "instructions.md");
    string baseInstructions = "";
    if (File.Exists(instructionsPath))
    {
        baseInstructions = File.ReadAllText(instructionsPath);
    }
    else
    {
        Console.Error.WriteLine($"[Startup] No instructions.md found at {instructionsPath}.");
    }

    string groundRulesSection = productConfig.GroundRules.Count > 0
        ? "\n\n## Schema ground rules\n\n" + string.Join("\n", productConfig.GroundRules.Select(r => $"- {r}")) + "\n"
        : "";

    serverInstructions = baseInstructions.Length > 0 || groundRulesSection.Length > 0
        ? baseInstructions + groundRulesSection
        : null;

    DocsIndex docsIndex = new(Path.Combine(productDirectory, "docs"));
    builder.Services.AddSingleton(docsIndex);
    Console.Error.WriteLine($"[Startup] Loaded {docsIndex.Sections.Count} doc section(s) from {Path.Combine(productDirectory, "docs")}.");

    // The connection string is supplied by whoever runs the server (dev machine, customer
    // install) via environment variable - never hardcoded or typed into chat. Without it,
    // the server still starts so the rest of the stack can be developed/tested, but no
    // schema-map tools are registered since they'd have nothing to query.
    string? connectionString = Environment.GetEnvironmentVariable("CFCCHATBOT_DB_CONNECTION_STRING");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine(
            $"[Startup] CFCCHATBOT_DB_CONNECTION_STRING is not set. Loaded {productConfig.Tools.Count} tool definition(s) from {productConfigPath} but none will be registered until a connection string is provided.");
    }
    else
    {
        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        builder.Services.AddSingleton(dataSource);
        dynamicTools = SchemaMapToolProvider.BuildTools(productConfig, dataSource).ToArray();
        writeTools = SchemaMapWriteToolProvider.BuildTools(productConfig, dataSource).ToArray();
        Console.Error.WriteLine(
            $"[Startup] Loaded {dynamicTools.Length} read tool(s) and {writeTools.Length} write tool(s) for product '{productConfig.Product}' from {productConfigPath}.");
    }
}

builder.Services
    .AddMcpServer(options => options.ServerInstructions = serverInstructions)
    .WithStdioServerTransport()
    .WithTools(dynamicTools)
    .WithTools(writeTools)
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
