using CFCChatBot.McpServer.Config;
using ModelContextProtocol.Server;
using Npgsql;

namespace CFCChatBot.McpServer.Tools;

public static class SchemaMapWriteToolProvider
{
    public static IReadOnlyList<McpServerTool> BuildTools(ProductConfig config, NpgsqlDataSource dataSource)
    {
        List<McpServerTool> tools = new(config.WriteTools.Count);

        foreach (WriteToolDefinition definition in config.WriteTools)
        {
            SchemaMapWriteFunction function = new(definition, dataSource);
            tools.Add(McpServerTool.Create(function, new McpServerToolCreateOptions
            {
                ReadOnly = false,
                Destructive = false,
                Idempotent = true,
            }));
        }

        return tools;
    }
}
