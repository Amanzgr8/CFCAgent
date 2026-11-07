using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CFCAgent.Backend;

// The main piece of the backend. Launches the MCP server as a child process over stdio and
// connects to it as an MCP client, turns the server's tools into AI tools a model can call,
// and exposes the server's instructions text. It only tells the server it can ask the user
// for confirmation (elicitation) when a confirmation callback is supplied. Everything else
// builds on this.
public sealed class McpToolHost : IAsyncDisposable
{
    private readonly McpClient _client;

    private McpToolHost(McpClient client, IReadOnlyList<AITool> tools)
    {
        _client = client;
        Tools = tools;
    }

    public IReadOnlyList<AITool> Tools { get; }

    public string? ServerInstructions => _client.ServerInstructions;

    public static async Task<McpToolHost> ConnectAsync(McpConnectionOptions options, CancellationToken cancellationToken = default)
    {
        StdioClientTransportOptions transportOptions = new StdioClientTransportOptions
        {
            Name = "cfc-fertilizer",
            Command = options.Command,
            Arguments = options.Arguments.ToList(),
            WorkingDirectory = options.WorkingDirectory,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["CFCCHATBOT_PRODUCT_CONFIG"] = options.ProductConfigPath,
            },
        };

        if (options.OnServerLog is not null)
        {
            Action<string> log = options.OnServerLog;
            transportOptions.StandardErrorLines = line => log(line);
        }

        StdioClientTransport transport = new StdioClientTransport(transportOptions);

        McpClientOptions clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "CFCAgent.Backend", Version = "0.1.0" },
        };

        if (options.ConfirmAsync is not null)
        {
            clientOptions.Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
            };
            clientOptions.Handlers = new McpClientHandlers
            {
                ElicitationHandler = ElicitationHandlers.Create(options.ConfirmAsync),
            };
        }

        McpClient client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken);
        IList<McpClientTool> mcpTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return new McpToolHost(client, mcpTools.Cast<AITool>().ToList());
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
