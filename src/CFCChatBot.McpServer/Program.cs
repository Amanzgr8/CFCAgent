using CFCChatBot.McpServer.Config;
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

if (!File.Exists(productConfigPath))
{
    Console.Error.WriteLine(
        $"[Startup] Product config not found at {productConfigPath}. Set CFCCHATBOT_PRODUCT_CONFIG to the schema-map.yaml path. No schema-map-driven tools will be registered.");
}
else
{
    ProductConfig productConfig = SchemaMapLoader.Load(productConfigPath);

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
        Console.Error.WriteLine(
            $"[Startup] Loaded {dynamicTools.Length} tool(s) for product '{productConfig.Product}' from {productConfigPath}.");
    }
}

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools(dynamicTools);

await builder.Build().RunAsync();
