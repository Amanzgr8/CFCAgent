using CFCChatBot.McpServer.Config;
using ModelContextProtocol.Server;
using Npgsql;

namespace CFCChatBot.McpServer.Tools;

public static class SchemaMapToolProvider
{
    public static IReadOnlyList<McpServerTool> BuildTools(ProductConfig config, NpgsqlDataSource dataSource)
    {
        List<McpServerTool> tools = new(config.Tools.Count);

        foreach (ToolDefinition definition in config.Tools)
        {
            SchemaMapSqlFunction function = new(definition, dataSource);
            tools.Add(McpServerTool.Create(function, new McpServerToolCreateOptions
            {
                ReadOnly = definition.ReadOnly,
            }));
        }

        return tools;
    }
}
